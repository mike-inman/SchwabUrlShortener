using Shortener.Domain;

namespace Shortener.Application.Interfaces;

public sealed record RedirectTarget(
    string Id, string ShortCode, string OriginalUrl, LinkMode Mode,
    Guid? TenantId, DateTimeOffset? ExpiresAtUtc, Guid? OwnerWorkspaceId = null)
{
    public static RedirectTarget FromMapping(UrlMapping mapping) => new(
        mapping.Id, mapping.ShortCode, mapping.OriginalUrl,
        mapping.Mode, mapping.TenantId, mapping.ExpiresAtUtc, mapping.OwnerWorkspaceId);

    public bool IsValidFor(string shortCode) =>
        !string.IsNullOrWhiteSpace(Id) &&
        string.Equals(ShortCode, shortCode, StringComparison.OrdinalIgnoreCase) &&
        UrlValidation.IsValidAbsoluteUrl(OriginalUrl) &&
        (Mode == LinkMode.Guest && TenantId is null && OwnerWorkspaceId is null && ExpiresAtUtc is not null ||
         Mode == LinkMode.Managed && ExpiresAtUtc is null &&
         (TenantId is { } tenant && tenant != Guid.Empty || OwnerWorkspaceId is { } workspace && workspace != Guid.Empty));

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc is { } expiry && nowUtc >= expiry;
}

public interface IRedirectCache
{
    Task<RedirectTarget?> GetAsync(string shortCode, CancellationToken cancellationToken = default);
    Task SetAsync(RedirectTarget target, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
