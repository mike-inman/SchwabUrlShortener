using Shortener.Application.Interfaces;
using Shortener.Domain;

namespace Shortener.Application;

public sealed class UrlShortenerService : IUrlShortenerService
{
    private const string AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private readonly IUrlShortenerRepository _repository;

    public UrlShortenerService(IUrlShortenerRepository repository)
    {
        _repository = repository;
    }

    public async Task<UrlMapping> CreateAsync(string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default)
    {
        var normalizedUrl = UrlValidation.NormalizeUrl(originalUrl);

        if (!UrlValidation.IsValidAbsoluteUrl(normalizedUrl))
        {
            throw new ArgumentException("Original URL must be an absolute http or https URL.", nameof(originalUrl));
        }

        var existing = await _repository.GetByOriginalUrlAsync(normalizedUrl, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var finalShortCode = string.IsNullOrWhiteSpace(requestedShortCode)
            ? GenerateShortCode()
            : requestedShortCode.Trim();

        if (!UrlValidation.IsValidShortCode(finalShortCode))
        {
            throw new ArgumentException("The short code must be 4 to 12 characters using letters or numbers only.", nameof(requestedShortCode));
        }

        var existingByCode = await _repository.GetByShortCodeAsync(finalShortCode, cancellationToken);
        if (existingByCode is not null)
        {
            throw new InvalidOperationException($"The short code '{finalShortCode}' is already in use.");
        }

        var mapping = UrlMapping.Create(normalizedUrl, finalShortCode);
        await _repository.SaveAsync(mapping, cancellationToken);
        return mapping;
    }

    public async Task<UrlMapping?> ResolveAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var mapping = await _repository.GetByShortCodeAsync(shortCode, cancellationToken);
        if (mapping is null)
        {
            return null;
        }

        mapping.RecordHit();
        await _repository.SaveAsync(mapping, cancellationToken);
        return mapping;
    }

    public Task<UrlMapping?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        return _repository.GetByShortCodeAsync(shortCode, cancellationToken);
    }

    public Task<IReadOnlyCollection<UrlMapping>> GetAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetAllAsync(cancellationToken);
    }

    private static string GenerateShortCode()
    {
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = AllowedChars[Random.Shared.Next(AllowedChars.Length)];
        }

        return new string(chars);
    }
}
