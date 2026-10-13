using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shortener.Application.Interfaces;
using Shortener.Domain;

namespace Shortener.Infrastructure;

public sealed class PostgresUrlShortenerRepository(
    IDbContextFactory<ShortenerDbContext> contextFactory,
    ILogger<PostgresUrlShortenerRepository> logger) : IUrlShortenerRepository
{
    public Task<bool> TryAddAsync(UrlMapping mapping, CancellationToken cancellationToken = default)
        => WithDbAsync("AddLink", async db =>
        {
            db.UrlMappings.Add(mapping);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_url_mappings_short_code" })
            {
                return false;
            }
        }, cancellationToken);

    public Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
        => WithDbAsync("ResolveCode", db => db.UrlMappings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ShortCode == shortCode, cancellationToken), cancellationToken);

    public Task<UrlMapping?> GetManagedByShortCodeAsync(Guid tenantId, string shortCode, CancellationToken cancellationToken = default)
        => WithDbAsync("GetManagedCode", db => db.UrlMappings.AsNoTracking().SingleOrDefaultAsync(
            x => x.Mode == LinkMode.Managed && x.TenantId == tenantId && x.ShortCode == shortCode, cancellationToken), cancellationToken);

    public Task<UrlMapping?> GetManagedByOriginalUrlAsync(Guid tenantId, string originalUrl, CancellationToken cancellationToken = default)
        => WithDbAsync("GetManagedOriginal", db => db.UrlMappings.AsNoTracking().OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(
            x => x.Mode == LinkMode.Managed && x.TenantId == tenantId && x.OriginalUrl == originalUrl, cancellationToken), cancellationToken);

    public async Task<IReadOnlyCollection<UrlMapping>> GetManagedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await WithDbAsync("GetManagedAnalytics", db => db.UrlMappings.AsNoTracking()
            .Where(x => x.Mode == LinkMode.Managed && x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc).ToArrayAsync(cancellationToken), cancellationToken);

    public async Task<Guid> CaptureManagedClickAsync(string mappingId, Guid tenantId, DateTimeOffset occurredAtUtc,
        string? traceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mappingId) || tenantId == Guid.Empty)
            throw new ArgumentException("A managed link and tenant are required.");
        var eventId = Guid.NewGuid();
        var count = await WithDbAsync("CaptureManagedClick", db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO click_outbox (event_id, tenant_id, mapping_id, short_code, occurred_at_utc, trace_id) SELECT {eventId}, tenant_id, id, short_code, {occurredAtUtc}, {traceId} FROM url_mappings WHERE id = {mappingId} AND mode = 1 AND tenant_id = {tenantId}",
            cancellationToken), cancellationToken);
        if (count != 1) throw new InvalidOperationException("Managed link was not found for click capture.");
        return eventId;
    }

    private async Task<T> WithDbAsync<T>(string operation, Func<ShortenerDbContext, Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await action(db);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("PostgreSQL operation failed. Operation={Operation} ExceptionType={ExceptionType}", operation, ex.GetType().Name);
            throw;
        }
    }
}
