using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Shortener.Application;
using Shortener.Infrastructure;
using Shortener.Shared;

namespace Shortener.Api;

public static class ContactEndpoints
{
    public static void MapContactEndpoints(this WebApplication app)
    {
        var protector = app.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("contact-form-v1").ToTimeLimitedDataProtector();
        app.MapGet("/api/contact/form-token", () => Results.Ok(new ContactFormToken(
                protector.Protect(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    TimeSpan.FromMinutes(30)))))
            .AllowAnonymous();
        app.MapPost("/api/contact", async (ContactSubmission submission, HttpContext context,
            IDbContextFactory<ShortenerDbContext> dbFactory, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Contact");
            if (!string.IsNullOrEmpty(submission.Website)) return Rejected("honeypot", logger);
            long issuedAt;
            try
            {
                var unprotected = protector.Unprotect(submission.FormToken ?? "");
                if (!long.TryParse(unprotected, NumberStyles.None, CultureInfo.InvariantCulture, out issuedAt))
                    return Rejected("invalid_token", logger);
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
            {
                return Rejected("invalid_token", logger);
            }
            var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(issuedAt);
            if (elapsed < TimeSpan.FromSeconds(3)) return Rejected("too_fast", logger);
            var errors = ContactSubmissionValidator.Validate(submission);
            if (errors.Count != 0)
            {
                ContactMetrics.Rejected.Add(1, new KeyValuePair<string, object?>("reason", "validation"));
                return Results.ValidationProblem(errors);
            }
            var now = DateTimeOffset.UtcNow;
            var message = new ContactMessage
            {
                Id = Guid.NewGuid(), CreatedAtUtc = now, Name = submission.Name!.Trim(),
                Email = submission.Email!.Trim(), Subject = string.IsNullOrWhiteSpace(submission.Subject) ? null : submission.Subject.Trim(),
                Message = submission.Message!.Trim(), ClientKeyHash = HardeningHostConfiguration.ClientKeyHash(context),
                NextAttemptAtUtc = now
            };
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.ContactMessages.Add(message);
            await db.SaveChangesAsync(cancellationToken);
            ContactMetrics.Received.Add(1);
            logger.LogInformation("Contact received. MessageId={MessageId}", message.Id);
            return Results.Created("/api/contact/" + message.Id.ToString("N"), new ContactReceipt(message.Id));
        }).AllowAnonymous().RequireRateLimiting(HardeningHostConfiguration.ContactPolicy);
    }

    private static IResult Rejected(string reason, ILogger logger)
    {
        ContactMetrics.Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
        logger.LogInformation("Contact spam rejected. Reason={Reason}", reason);
        return Results.Created("/api/contact/received", new ContactReceipt(null));
    }
}

public sealed record ContactFormToken(string Token);
public sealed record ContactReceipt(Guid? Id);
