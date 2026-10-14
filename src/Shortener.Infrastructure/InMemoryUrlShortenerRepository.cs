using Shortener.Application.Interfaces;
using Shortener.Domain;

namespace Shortener.Infrastructure;

public sealed class InMemoryUrlShortenerRepository : IUrlShortenerRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UrlMapping> _byCode = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> TryAddAsync(UrlMapping mapping, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_byCode.ContainsKey(mapping.ShortCode)) return Task.FromResult(false);
            _byCode.Add(mapping.ShortCode, mapping);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryAddWorkspaceAsync(UrlMapping mapping, string issuer, string subject,
        CancellationToken cancellationToken = default) => TryAddAsync(mapping, cancellationToken);

    public Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_byCode.GetValueOrDefault(shortCode));
    }

    public Task<UrlMapping?> GetManagedByShortCodeAsync(Guid tenantId, string shortCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var mapping = _byCode.GetValueOrDefault(shortCode);
            return Task.FromResult(mapping is { Mode: LinkMode.Managed } && mapping.TenantId == tenantId ? mapping : null);
        }
    }

    public Task<UrlMapping?> GetWorkspaceManagedByShortCodeAsync(Guid workspaceId, string shortCode,
        string issuer, string subject, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var mapping = _byCode.GetValueOrDefault(shortCode);
            return Task.FromResult(mapping is { Mode: LinkMode.Managed } && mapping.OwnerWorkspaceId == workspaceId ? mapping : null);
        }
    }

    public Task<IReadOnlyCollection<UrlMapping>> GetManagedByWorkspaceAsync(Guid workspaceId,
        string issuer, string subject, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyCollection<UrlMapping>>(_byCode.Values.Where(x => x.Mode == LinkMode.Managed && x.OwnerWorkspaceId == workspaceId).ToArray());
    }

    public Task<UrlMapping?> GetManagedByOriginalUrlAsync(Guid tenantId, string originalUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_byCode.Values.FirstOrDefault(x => x.Mode == LinkMode.Managed && x.TenantId == tenantId && x.OriginalUrl == originalUrl));
    }

    public Task<IReadOnlyCollection<UrlMapping>> GetManagedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyCollection<UrlMapping>>(_byCode.Values.Where(x => x.Mode == LinkMode.Managed && x.TenantId == tenantId).OrderByDescending(x => x.CreatedAtUtc).ToArray());
    }

    public Task<Guid> CaptureManagedClickAsync(string mappingId, Guid tenantId, DateTimeOffset occurredAtUtc,
        string? traceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var stored = _byCode.Values.SingleOrDefault(x => x.Id == mappingId && x.TenantId == tenantId && x.Mode == LinkMode.Managed);
            if (stored is null)
                throw new InvalidOperationException("Managed link was not found.");
        }
        // This repository is a test double only. It cannot model a durable outbox.
        return Task.FromResult(Guid.NewGuid());
    }

    public Task<Guid> CaptureWorkspaceClickAsync(string mappingId, Guid workspaceId, DateTimeOffset occurredAtUtc,
        string? traceId, ManagedClickSignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_byCode.Values.Any(x => x.Id == mappingId && x.OwnerWorkspaceId == workspaceId && x.Mode == LinkMode.Managed))
                throw new InvalidOperationException("Workspace link was not found.");
        }
        return Task.FromResult(Guid.NewGuid());
    }
}
