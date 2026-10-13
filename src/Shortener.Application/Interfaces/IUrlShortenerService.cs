using Shortener.Domain;

namespace Shortener.Application.Interfaces;

public readonly record struct ManagedCreateResult(UrlMapping Mapping, bool AlreadyExists);

public interface IUrlShortenerService
{
    Task<UrlMapping> CreateGuestAsync(string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default);
    Task<UrlMapping> CreateManagedAsync(Guid tenantId, string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default);
    Task<ManagedCreateResult> CreateManagedWithOutcomeAsync(Guid tenantId, string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default);
    Task<ManagedCreateResult?> CreateWorkspaceManagedAsync(string issuer, string subject, Guid workspaceId, string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetWorkspaceManagedAsync(string issuer, string subject, Guid workspaceId, string shortCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UrlMapping>?> GetWorkspaceAnalyticsAsync(string issuer, string subject, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<RedirectTarget?> ResolveAsync(string shortCode, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetManagedAsync(Guid tenantId, string shortCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UrlMapping>> GetManagedAnalyticsAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
