using Shortener.Domain;

namespace Shortener.Application.Interfaces;

public interface IUrlShortenerRepository
{
    // Code uniqueness is global across both modes. Add must be atomic with respect to this key.
    Task<bool> TryAddAsync(UrlMapping mapping, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetManagedByShortCodeAsync(Guid tenantId, string shortCode, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetManagedByOriginalUrlAsync(Guid tenantId, string originalUrl, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UrlMapping>> GetManagedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task RecordManagedHitAsync(UrlMapping mapping, DateTimeOffset timestampUtc, CancellationToken cancellationToken = default);
}
