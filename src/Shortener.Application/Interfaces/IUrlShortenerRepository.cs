using Shortener.Domain;

namespace Shortener.Application.Interfaces;

public interface IUrlShortenerRepository
{
    Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
    Task<UrlMapping?> GetByOriginalUrlAsync(string originalUrl, CancellationToken cancellationToken = default);
    Task SaveAsync(UrlMapping mapping, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UrlMapping>> GetAllAsync(CancellationToken cancellationToken = default);
}
