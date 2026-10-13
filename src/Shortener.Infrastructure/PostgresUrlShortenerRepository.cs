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
            if (mapping.Mode == LinkMode.Managed && mapping.TenantId is { } legacyTenant)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO workspaces (id, kind, personal_owner_user_id, legacy_tenant_id, name, created_at_utc)
                    VALUES (md5('schwab-url-shortener:legacy-entra-workspace:' || {legacyTenant}::text)::uuid,
                            1, NULL, {legacyTenant}, 'Legacy Entra team', now())
                    ON CONFLICT (legacy_tenant_id) DO NOTHING
                    """, cancellationToken);
                var legacyWorkspace = await db.Workspaces.AsNoTracking()
                    .Where(x => x.LegacyTenantId == legacyTenant).Select(x => x.Id).SingleAsync(cancellationToken);
                mapping.AssignLegacyWorkspace(legacyWorkspace);
            }
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

    public Task<bool> TryAddWorkspaceAsync(UrlMapping mapping, string issuer, string subject,
        CancellationToken cancellationToken = default)
    {
        if (mapping.Mode != LinkMode.Managed || mapping.TenantId is not null || mapping.OwnerWorkspaceId is not { } workspaceId)
            throw new ArgumentException("A workspace-owned managed link is required.", nameof(mapping));
        return WithDbAsync("AddWorkspaceLink", async db =>
        {
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO url_mappings (id, short_code, original_url, mode, tenant_id, owner_workspace_id,
                                          created_at_utc, expires_at_utc, last_accessed_at_utc, hit_count)
                SELECT {mapping.Id}, {mapping.ShortCode}, {mapping.OriginalUrl}, 1, NULL, {workspaceId},
                       {mapping.CreatedAtUtc}, NULL, NULL, 0
                FROM workspaces AS workspace
                JOIN external_identities AS identity ON identity.issuer = {issuer} AND identity.subject = {subject}
                WHERE workspace.id = {workspaceId}
                  AND ((workspace.kind = 0 AND workspace.personal_owner_user_id = identity.user_id)
                    OR (workspace.kind = 1 AND EXISTS (
                        SELECT 1 FROM workspace_memberships AS membership
                        WHERE membership.workspace_id = workspace.id AND membership.user_id = identity.user_id
                        FOR SHARE)))
                FOR SHARE OF workspace, identity
                ON CONFLICT (short_code) DO NOTHING
                """, cancellationToken);
            return inserted == 1;
        }, cancellationToken);
    }

    public Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
        => WithDbAsync("ResolveCode", db => db.UrlMappings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ShortCode == shortCode, cancellationToken), cancellationToken);

    public Task<UrlMapping?> GetManagedByShortCodeAsync(Guid tenantId, string shortCode, CancellationToken cancellationToken = default)
        => WithDbAsync("GetManagedCode", db => db.UrlMappings.AsNoTracking().SingleOrDefaultAsync(
            x => x.Mode == LinkMode.Managed && x.TenantId == tenantId && x.ShortCode == shortCode, cancellationToken), cancellationToken);

    public Task<UrlMapping?> GetWorkspaceManagedByShortCodeAsync(Guid workspaceId, string shortCode,
        string issuer, string subject, CancellationToken cancellationToken = default)
        => WithDbAsync("GetWorkspaceManagedCode", db => db.UrlMappings.AsNoTracking().SingleOrDefaultAsync(
            x => x.Mode == LinkMode.Managed && x.ShortCode == shortCode &&
                db.ExternalIdentities.Any(identity => identity.Issuer == issuer && identity.Subject == subject &&
                    db.Workspaces.Any(workspace => workspace.Id == workspaceId &&
                        (workspace.Kind == WorkspaceKind.Personal && workspace.PersonalOwnerUserId == identity.UserId ||
                         workspace.Kind == WorkspaceKind.Team && db.WorkspaceMemberships.Any(membership =>
                             membership.WorkspaceId == workspaceId && membership.UserId == identity.UserId)))) &&
                (x.OwnerWorkspaceId == workspaceId || x.OwnerWorkspaceId == null &&
                 db.Workspaces.Any(w => w.Id == workspaceId && w.LegacyTenantId != null && w.LegacyTenantId == x.TenantId)),
            cancellationToken), cancellationToken);

    public async Task<IReadOnlyCollection<UrlMapping>> GetManagedByWorkspaceAsync(Guid workspaceId,
        string issuer, string subject, CancellationToken cancellationToken = default)
        => await WithDbAsync("GetWorkspaceAnalytics", db => db.UrlMappings.AsNoTracking()
            .Where(x => x.Mode == LinkMode.Managed &&
                db.ExternalIdentities.Any(identity => identity.Issuer == issuer && identity.Subject == subject &&
                    db.Workspaces.Any(workspace => workspace.Id == workspaceId &&
                        (workspace.Kind == WorkspaceKind.Personal && workspace.PersonalOwnerUserId == identity.UserId ||
                         workspace.Kind == WorkspaceKind.Team && db.WorkspaceMemberships.Any(membership =>
                             membership.WorkspaceId == workspaceId && membership.UserId == identity.UserId)))) &&
                (x.OwnerWorkspaceId == workspaceId || x.OwnerWorkspaceId == null &&
                 db.Workspaces.Any(w => w.Id == workspaceId && w.LegacyTenantId != null && w.LegacyTenantId == x.TenantId)))
            .OrderByDescending(x => x.CreatedAtUtc).ToArrayAsync(cancellationToken), cancellationToken);

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

    public async Task<Guid> CaptureWorkspaceClickAsync(string mappingId, Guid workspaceId, DateTimeOffset occurredAtUtc,
        string? traceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mappingId) || workspaceId == Guid.Empty)
            throw new ArgumentException("A managed link and workspace are required.");
        var eventId = Guid.NewGuid();
        var count = await WithDbAsync("CaptureWorkspaceClick", db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO click_outbox (event_id, tenant_id, owner_workspace_id, event_version, mapping_id, short_code, occurred_at_utc, trace_id) SELECT {eventId}, NULL, owner_workspace_id, 2, id, short_code, {occurredAtUtc}, {traceId} FROM url_mappings WHERE id = {mappingId} AND mode = 1 AND tenant_id IS NULL AND owner_workspace_id = {workspaceId}",
            cancellationToken), cancellationToken);
        if (count != 1) throw new InvalidOperationException("Workspace link was not found for click capture.");
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
