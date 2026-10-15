using Azure;
using Azure.Communication.Email;
using Azure.Identity;
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

    public AzureCommunicationContactNotifier(IConfiguration configuration)
    {
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
        var content = new EmailContent("Ziplio contact message " + message.Id.ToString("N"))
        {
            PlainText = $"Name: {message.Name}\nEmail: {message.Email}\nSubject: {message.Subject ?? "(none)"}\n\n{message.Message}"
        };
        var email = new EmailMessage(_sender, _recipient, content);
        var operation = await _client.SendAsync(WaitUntil.Completed, email, message.Id, cancellationToken);
        if (!operation.HasValue || operation.Value.Status != EmailSendStatus.Succeeded)
            throw new InvalidOperationException("Contact email delivery was not confirmed by Azure Communication Services.");
    }
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
            failure.Parameters.AddWithValue("code", ex.GetType().Name);
            await failure.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            ContactMetrics.NotifyFailed.Add(1, new KeyValuePair<string, object?>("terminal", terminal));
            logger.LogWarning("Contact notification {Outcome}. MessageId={MessageId} Attempt={Attempt} ExceptionType={ExceptionType}",
                terminal ? "failed" : "retry", message.Id, attempts, ex.GetType().Name);
            return terminal ? ContactNotificationOutcome.Failed : ContactNotificationOutcome.Retried;
        }
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
