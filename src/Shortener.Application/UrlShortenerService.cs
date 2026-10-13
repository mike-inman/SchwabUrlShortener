using System.Security.Cryptography;
using Shortener.Application.Interfaces;
using Shortener.Domain;

namespace Shortener.Application;

public sealed class UrlShortenerService : IUrlShortenerService
{
    private const string AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private readonly IUrlShortenerRepository _repository;
    private readonly TimeProvider _clock;

    public UrlShortenerService(IUrlShortenerRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<UrlMapping> CreateGuestAsync(string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default)
        => (await CreateAsync(null, originalUrl, requestedShortCode, cancellationToken)).Mapping;

    public async Task<UrlMapping> CreateManagedAsync(Guid tenantId, string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default)
        => (await CreateManagedWithOutcomeAsync(tenantId, originalUrl, requestedShortCode, cancellationToken)).Mapping;

    public Task<ManagedCreateResult> CreateManagedWithOutcomeAsync(Guid tenantId, string originalUrl, string? requestedShortCode = null, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A valid tenant is required.", nameof(tenantId));
        return CreateAsync(tenantId, originalUrl, requestedShortCode, cancellationToken);
    }

    private async Task<ManagedCreateResult> CreateAsync(Guid? tenantId, string originalUrl, string? requestedShortCode, CancellationToken cancellationToken)
    {
        var normalizedUrl = UrlValidation.NormalizeUrl(originalUrl);
        if (!UrlValidation.IsValidAbsoluteUrl(normalizedUrl))
            throw new ArgumentException("Original URL must be an absolute http or https URL.", nameof(originalUrl));

        var custom = !string.IsNullOrWhiteSpace(requestedShortCode);
        // A destination may have many generated links. Only an explicit code can identify a repeat request.
        if (tenantId is { } owner && custom)
        {
            var existing = await _repository.GetManagedByShortCodeAsync(owner, requestedShortCode!.Trim(), cancellationToken);
            if (existing is not null && existing.OriginalUrl == normalizedUrl)
                return new(existing, AlreadyExists: true);
        }
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = custom ? requestedShortCode!.Trim() : GenerateShortCode();
            var now = _clock.GetUtcNow();
            var mapping = tenantId is { } id
                ? UrlMapping.CreateManaged(normalizedUrl, code, id, now)
                : UrlMapping.CreateGuest(normalizedUrl, code, now);
            if (await _repository.TryAddAsync(mapping, cancellationToken)) return new(mapping, AlreadyExists: false);
            if (custom)
            {
                if (tenantId is { } ownerOnConflict)
                {
                    var existing = await _repository.GetManagedByShortCodeAsync(ownerOnConflict, code, cancellationToken);
                    if (existing is not null && existing.OriginalUrl == normalizedUrl)
                        return new(existing, AlreadyExists: true);
                }
                throw new ShortCodeConflictException();
            }
        }
        throw new InvalidOperationException("Could not allocate a unique short code. Try again.");
    }

    public async Task<UrlMapping?> ResolveAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        if (!UrlValidation.IsValidShortCode(shortCode)) return null;
        var mapping = await _repository.GetByShortCodeAsync(shortCode.Trim(), cancellationToken);
        if (mapping is null || mapping.IsExpired(_clock.GetUtcNow())) return null;
        if (mapping.Mode == LinkMode.Managed)
            await _repository.RecordManagedHitAsync(mapping, _clock.GetUtcNow(), cancellationToken);
        return mapping;
    }

    public Task<UrlMapping?> GetManagedAsync(Guid tenantId, string shortCode, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A valid tenant is required.", nameof(tenantId));
        if (!UrlValidation.IsValidShortCode(shortCode)) return Task.FromResult<UrlMapping?>(null);
        return _repository.GetManagedByShortCodeAsync(tenantId, shortCode.Trim(), cancellationToken);
    }

    public Task<IReadOnlyCollection<UrlMapping>> GetManagedAnalyticsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A valid tenant is required.", nameof(tenantId));
        return _repository.GetManagedByTenantAsync(tenantId, cancellationToken);
    }

    private static string GenerateShortCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++) chars[i] = AllowedChars[RandomNumberGenerator.GetInt32(AllowedChars.Length)];
        return new string(chars);
    }
}
