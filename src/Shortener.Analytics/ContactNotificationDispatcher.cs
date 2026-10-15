using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using System.ComponentModel.DataAnnotations;
using Npgsql;
using Shortener.Application;
using Shortener.Infrastructure;

namespace Shortener.Analytics;

public interface IContactNotifier
{
    Task NotifyAsync(ContactMessage message, CancellationToken cancellationToken);
}

public sealed class NoContactNotifier(ILogger<NoContactNotifier> logger) : IContactNotifier
{
    public Task NotifyAsync(ContactMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Would notify for contact. MessageId={MessageId}", message.Id);
        return Task.CompletedTask;
    }
}

public sealed class AzureCommunicationContactNotifier : IContactNotifier
{
    private readonly EmailClient _client;
    private readonly string _sender;
    private readonly string _recipient;
    private readonly ILogger<AzureCommunicationContactNotifier> _logger;

    public AzureCommunicationContactNotifier(IConfiguration configuration, ILogger<AzureCommunicationContactNotifier> logger)
    {
        _logger = logger;
        if (!Uri.TryCreate(configuration["ContactNotifications:Endpoint"], UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https")
            throw new InvalidOperationException("ContactNotifications:Endpoint must be an HTTPS Azure Communication Services endpoint.");
        _sender = configuration["ContactNotifications:Sender"] ?? "";
        _recipient = configuration["Contact:NotifyTo"] ?? "";
        if (string.IsNullOrWhiteSpace(_sender) || string.IsNullOrWhiteSpace(_recipient))
            throw new InvalidOperationException("ContactNotifications:Sender and Contact:NotifyTo are required for AzureCommunicationEmail mode.");
        var clientId = configuration["ContactNotifications:ManagedIdentityClientId"];
        var identity = string.IsNullOrWhiteSpace(clientId)
            ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(clientId);
        _client = new EmailClient(endpoint, new ManagedIdentityCredential(identity));
    }

    public async Task NotifyAsync(ContactMessage message, CancellationToken cancellationToken)
    {
        var email = BuildMessage(message, _sender, _recipient);
        var operation = await _client.SendAsync(WaitUntil.Completed, email, message.Id, cancellationToken);
        EnsureSucceeded(operation.HasValue ? operation.Value.Status : null);
        _logger.LogInformation("ACS contact email accepted. MessageId={MessageId} OperationId={OperationId}", message.Id, operation.Id);
    }

    internal static void EnsureSucceeded(EmailSendStatus? status)
    {
        if (status != EmailSendStatus.Succeeded)
            throw new ContactEmailDeliveryException("EmailSendFailed");
    }

    internal static EmailMessage BuildMessage(ContactMessage message, string sender, string recipient)
    {
        if (message.Email.Contains('\r') || message.Email.Contains('\n') || !new EmailAddressAttribute().IsValid(message.Email))
            throw new ContactEmailDeliveryException("InvalidReplyTo");
        var cleanSubject = (message.Subject ?? "").Replace("\r", "").Replace("\n", "").Trim();
        if (cleanSubject.Length == 0) cleanSubject = "(no subject)";
        var subject = "Ziplio contact: " + cleanSubject;
        if (subject.Length > 120) subject = subject[..120];
        var content = new EmailContent(subject)
        {
            PlainText = $"Name: {message.Name}\nEmail: {message.Email}\nSubject: {message.Subject ?? "(none)"}\n\n{message.Message}"
        };
        var email = new EmailMessage(sender, recipient, content);
        var cleanName = message.Name.Replace("\r", "").Replace("\n", "").Trim();
        email.ReplyTo.Add(new EmailAddress(message.Email, cleanName));
        return email;
    }
}

internal sealed class ContactEmailDeliveryException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public enum ContactNotificationOutcome { NoWork, Handled, Retried, Failed }

public sealed class ContactNotificationDispatcher(
    NpgsqlDataSource dataSource, IContactNotifier notifier, ILogger<ContactNotificationDispatcher> logger)
{
    public async Task<ContactNotificationOutcome> ProcessOneAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand("""
            SELECT id, created_at_utc, name, email, subject, message, notification_attempts
            FROM contact_messages
            WHERE notified_at_utc IS NULL AND failed_at_utc IS NULL AND next_attempt_at_utc <= now()
            ORDER BY created_at_utc, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """, connection, transaction);
        ContactMessage? message = null;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
                message = new ContactMessage
                {
                    Id = reader.GetGuid(0), CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(1),
                    Name = reader.GetString(2), Email = reader.GetString(3),
                    Subject = reader.IsDBNull(4) ? null : reader.GetString(4), Message = reader.GetString(5),
                    NotificationAttempts = reader.GetInt32(6)
                };
        }
        if (message is null) return ContactNotificationOutcome.NoWork;
        try
        {
            await notifier.NotifyAsync(message, cancellationToken);
            await using var success = new NpgsqlCommand("""
                UPDATE contact_messages SET notified_at_utc = now(), notification_attempts = notification_attempts + 1,
                    last_notification_error_code = NULL WHERE id = @id
                """, connection, transaction);
            success.Parameters.AddWithValue("id", message.Id);
            await success.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            ContactMetrics.Notified.Add(1);
            logger.LogInformation("Contact notification handled. MessageId={MessageId}", message.Id);
            return ContactNotificationOutcome.Handled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var attempts = message.NotificationAttempts + 1;
            var terminal = attempts >= 5;
            var backoff = TimeSpan.FromSeconds(Math.Min(300, 1 << Math.Min(attempts, 8)));
            await using var failure = new NpgsqlCommand("""
                UPDATE contact_messages SET notification_attempts = notification_attempts + 1,
                    next_attempt_at_utc = now() + @backoff,
                    failed_at_utc = CASE WHEN @terminal THEN now() ELSE NULL END,
                    last_notification_error_code = @code WHERE id = @id
                """, connection, transaction);
            failure.Parameters.AddWithValue("id", message.Id);
            failure.Parameters.AddWithValue("backoff", backoff);
            failure.Parameters.AddWithValue("terminal", terminal);
            var errorCode = ex switch
            {
                RequestFailedException azureFailure => SafeErrorCode(azureFailure.ErrorCode),
                ContactEmailDeliveryException deliveryFailure => deliveryFailure.Code,
                _ => ex.GetType().Name
            };
            failure.Parameters.AddWithValue("code", errorCode);
            await failure.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            ContactMetrics.NotifyFailed.Add(1, new KeyValuePair<string, object?>("terminal", terminal));
            logger.LogWarning("Contact notification {Outcome}. MessageId={MessageId} Attempt={Attempt} ErrorCode={ErrorCode}",
                terminal ? "failed" : "retry", message.Id, attempts, errorCode);
            return terminal ? ContactNotificationOutcome.Failed : ContactNotificationOutcome.Retried;
        }
    }

    private static string SafeErrorCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return nameof(RequestFailedException);
        var safe = new string(code.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').Take(100).ToArray());
        return safe.Length == 0 ? nameof(RequestFailedException) : safe;
    }
}

public sealed class ContactNotificationWorker(ContactNotificationDispatcher dispatcher, ILogger<ContactNotificationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await dispatcher.ProcessOneAsync(stoppingToken);
                if (outcome != ContactNotificationOutcome.Handled)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Contact notification worker failed; retrying. ExceptionType={ExceptionType}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
