using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shortener.Application;
using Shortener.Infrastructure;
using Shortener.Shared;

namespace Shortener.Api;

public static class ContactAdminEndpoints
{
    public static void MapContactAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/api/me", async (ClaimsPrincipal principal, ContactInboxAdmin admin,
            IDbContextFactory<ShortenerDbContext> dbFactory, CancellationToken cancellationToken) =>
        {
            if (!admin.IsAdmin(principal)) return Results.Ok(new ContactAdminInfo(false, 0));
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var count = await db.ContactMessages.CountAsync(x => x.HandledAtUtc == null, cancellationToken);
            return Results.Ok(new ContactAdminInfo(true, count));
        }).RequireAuthorization().RequireRateLimiting(HardeningHostConfiguration.ManagedPolicy);

        var inbox = app.MapGroup("/api/admin/contact-messages")
            .RequireAuthorization(ContactInboxAdmin.Policy)
            .RequireRateLimiting(HardeningHostConfiguration.ManagedPolicy);

        inbox.MapGet("", async (string? cursor, int? take, ClaimsPrincipal principal,
            IDbContextFactory<ShortenerDbContext> dbFactory, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (take is < 1 or > 100)
                return Results.Problem(title: "take must be between 1 and 100.", statusCode: 400);
            if (cursor is not null && !TryDecodeCursor(cursor, out _, out _))
                return Results.Problem(title: "Invalid cursor.", statusCode: 400);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var query = db.ContactMessages.AsNoTracking();
            if (cursor is not null)
            {
                TryDecodeCursor(cursor, out var created, out var id);
                query = query.Where(x => x.CreatedAtUtc < created ||
                    (x.CreatedAtUtc == created && x.Id.CompareTo(id) < 0));
            }
            var size = take ?? 50;
            var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                .Select(x => new ContactMessageView(x.Id, x.CreatedAtUtc, x.Name, x.Email,
                    x.Subject, x.Message, x.NotifiedAtUtc, x.FailedAtUtc, x.HandledAtUtc))
                .Take(size + 1).ToListAsync(cancellationToken);
            var hasMore = rows.Count > size;
            var items = rows.Take(size).ToArray();
            var next = hasMore ? EncodeCursor(items[^1].CreatedAtUtc, items[^1].Id) : null;
            var count = await db.ContactMessages.CountAsync(x => x.HandledAtUtc == null, cancellationToken);
            var userId = await FindUserIdAsync(db, principal, cancellationToken);
            loggerFactory.CreateLogger("ContactInbox").LogInformation(
                "Contact inbox read. UserId={UserId}", userId);
            return Results.Ok(new ContactMessagePage(items, next, count));
        });

        inbox.MapPost("/{id:guid}/handled", async (Guid id, ClaimsPrincipal principal,
            IDbContextFactory<ShortenerDbContext> dbFactory, ILinkChangePublisher publisher,
            ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var updated = await db.ContactMessages.Where(x => x.Id == id && x.HandledAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.HandledAtUtc, now), cancellationToken);
            var handled = await db.ContactMessages.AsNoTracking().Where(x => x.Id == id)
                .Select(x => x.HandledAtUtc).SingleOrDefaultAsync(cancellationToken);
            if (handled is null)
                return Results.Problem(title: "Contact message not found.", statusCode: 404);
            var userId = await FindUserIdAsync(db, principal, cancellationToken);
            loggerFactory.CreateLogger("ContactInbox").LogInformation(
                "Contact message handled. UserId={UserId} MessageId={MessageId}", userId, id);
            if (updated != 0)
                await publisher.PublishAsync(ContactInboxHint.Change, cancellationToken);
            return Results.Ok(new ContactHandled(handled.Value));
        });
    }

    private static async Task<Guid?> FindUserIdAsync(ShortenerDbContext db, ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var issuer = principal.FindFirst("iss")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        return await db.ExternalIdentities.AsNoTracking().Where(x => x.Issuer == issuer && x.Subject == subject)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(cancellationToken);
    }

    private static string EncodeCursor(DateTimeOffset created, Guid id) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{created.UtcTicks}:{id:N}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset created, out Guid id)
    {
        created = default;
        id = default;
        if (cursor.Length is < 1 or > 128 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            return false;
        try
        {
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
            var parts = Encoding.ASCII.GetString(bytes).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out id) || id == Guid.Empty) return false;
            created = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { return false; }
    }
}

public sealed record ContactAdminInfo(bool IsContactAdmin, int UnhandledContactCount);
public sealed record ContactMessageView(Guid Id, DateTimeOffset CreatedAtUtc, string Name, string Email,
    string? Subject, string Message, DateTimeOffset? NotifiedAtUtc, DateTimeOffset? FailedAtUtc,
    DateTimeOffset? HandledAtUtc);
public sealed record ContactMessagePage(IReadOnlyList<ContactMessageView> Items, string? NextCursor,
    int UnhandledContactCount);
public sealed record ContactHandled(DateTimeOffset HandledAtUtc);
