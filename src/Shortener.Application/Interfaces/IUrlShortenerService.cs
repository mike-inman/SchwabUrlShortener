using Shortener.Domain;

namespace Shortener.Application.Interfaces;

public interface IUrlShortenerService
{
    Task<UrlMapping> CreateAsync(string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default);
    Task<UrlMapping?> ResolveAsync(string shortCode, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UrlMapping>> GetAnalyticsAsync(CancellationToken cancellationToken = default);
}
