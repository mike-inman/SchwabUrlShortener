using Shortener.Domain;

namespace Shortener.Application.Interfaces;

public interface IUrlShortenerRepository
{
    // Code uniqueness is global across both modes. Add must be atomic with respect to this key.
    Task<bool> TryAddAsync(UrlMapping mapping, CancellationToken cancellationToken = default);
    Task<bool> TryAddWorkspaceAsync(UrlMapping mapping, string issuer, string subject, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetManagedByShortCodeAsync(Guid tenantId, string shortCode, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetWorkspaceManagedByShortCodeAsync(Guid workspaceId, string shortCode,
        string issuer, string subject, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UrlMapping>> GetManagedByWorkspaceAsync(Guid workspaceId,
        string issuer, string subject, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetManagedByOriginalUrlAsync(Guid tenantId, string originalUrl, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UrlMapping>> GetManagedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Guid> CaptureManagedClickAsync(string mappingId, Guid tenantId, DateTimeOffset occurredAtUtc,
        string? traceId, CancellationToken cancellationToken = default);
    Task<Guid> CaptureWorkspaceClickAsync(string mappingId, Guid workspaceId, DateTimeOffset occurredAtUtc,
        string? traceId, ManagedClickSignal signal, CancellationToken cancellationToken = default);
}
