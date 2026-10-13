namespace Shortener.Domain;

public enum LinkMode { Guest, Managed }

public sealed class UrlMapping
{
    public string Id { get; private init; } = Guid.NewGuid().ToString("N");
    public string ShortCode { get; private init; } = string.Empty;
    public string OriginalUrl { get; private init; } = string.Empty;
    public LinkMode Mode { get; private init; }
    public Guid? TenantId { get; private init; }
    public Guid? OwnerWorkspaceId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset? ExpiresAtUtc { get; private init; }
    public DateTimeOffset? LastAccessedAtUtc { get; private set; }
    public int HitCount { get; private set; }

    public static UrlMapping CreateGuest(string originalUrl, string shortCode, DateTimeOffset createdAtUtc)
        => Create(originalUrl, shortCode, LinkMode.Guest, null, createdAtUtc);

    public static UrlMapping CreateManaged(string originalUrl, string shortCode, Guid tenantId, DateTimeOffset createdAtUtc)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A valid tenant is required.", nameof(tenantId));
        return Create(originalUrl, shortCode, LinkMode.Managed, tenantId, createdAtUtc);
    }

    public static UrlMapping CreateWorkspaceManaged(string originalUrl, string shortCode, Guid workspaceId, DateTimeOffset createdAtUtc)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("A valid workspace is required.", nameof(workspaceId));
        var mapping = Create(originalUrl, shortCode, LinkMode.Managed, null, createdAtUtc);
        mapping.OwnerWorkspaceId = workspaceId;
        return mapping;
    }

    public void AssignLegacyWorkspace(Guid workspaceId)
    {
        if (Mode != LinkMode.Managed || TenantId is null || workspaceId == Guid.Empty)
            throw new InvalidOperationException("Only a legacy managed link can receive a legacy workspace.");
        OwnerWorkspaceId = workspaceId;
    }

    private static UrlMapping Create(string originalUrl, string shortCode, LinkMode mode, Guid? tenantId, DateTimeOffset createdAtUtc)
    {
        var normalizedUrl = UrlValidation.NormalizeUrl(originalUrl);
        if (!UrlValidation.IsValidAbsoluteUrl(normalizedUrl))
            throw new ArgumentException("Original URL must be an absolute http or https URL.", nameof(originalUrl));
        if (!UrlValidation.IsValidShortCode(shortCode))
            throw new ArgumentException("The short code must be 4 to 12 ASCII letters or digits.", nameof(shortCode));
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Creation time must be UTC.", nameof(createdAtUtc));

        return new UrlMapping
        {
            ShortCode = shortCode.Trim(), OriginalUrl = normalizedUrl,
            Mode = mode, TenantId = tenantId, CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = mode == LinkMode.Guest ? createdAtUtc.AddDays(7) : null
        };
    }

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc is { } expiry && nowUtc >= expiry;

    public void RecordManagedHit(DateTimeOffset timestampUtc)
    {
        if (Mode != LinkMode.Managed) throw new InvalidOperationException("Guest links do not collect analytics.");
        HitCount = checked(HitCount + 1);
        LastAccessedAtUtc = timestampUtc;
    }
}
