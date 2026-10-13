using System.Collections.Concurrent;
using Shortener.Application.Interfaces;
using Shortener.Domain;

namespace Shortener.Infrastructure;

public sealed class InMemoryUrlShortenerRepository : IUrlShortenerRepository
{
    private readonly ConcurrentDictionary<string, UrlMapping> _byShortCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _byOriginalUrl = new(StringComparer.OrdinalIgnoreCase);

    public Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byShortCode.TryGetValue(shortCode.Trim(), out var mapping);
        return Task.FromResult(mapping);
    }

    public Task<UrlMapping?> GetByOriginalUrlAsync(string originalUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_byOriginalUrl.TryGetValue(originalUrl.Trim(), out var shortCode))
        {
            _byShortCode.TryGetValue(shortCode, out var mapping);
            return Task.FromResult(mapping);
        }

        return Task.FromResult<UrlMapping?>(null);
    }

    public Task SaveAsync(UrlMapping mapping, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byShortCode[mapping.ShortCode] = mapping;
        _byOriginalUrl[mapping.OriginalUrl] = mapping.ShortCode;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<UrlMapping>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = _byShortCode.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray();
        return Task.FromResult<IReadOnlyCollection<UrlMapping>>(items);
    }
}
