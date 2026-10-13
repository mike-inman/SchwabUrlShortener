namespace Shortener.Domain;

public sealed class UrlMapping
{
    public string Id { get; private init; } = Guid.NewGuid().ToString("N");
    public string ShortCode { get; private init; } = string.Empty;
    public string OriginalUrl { get; private init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAccessedAtUtc { get; private set; }
    public int HitCount { get; private set; }

    public static UrlMapping Create(string originalUrl, string shortCode)
    {
        var normalizedUrl = UrlValidation.NormalizeUrl(originalUrl);

        if (!UrlValidation.IsValidAbsoluteUrl(normalizedUrl))
        {
            throw new ArgumentException("Original URL must be an absolute http or https URL.", nameof(originalUrl));
        }

        if (!UrlValidation.IsValidShortCode(shortCode))
        {
            throw new ArgumentException("The short code must be 4 to 12 characters using letters or numbers only.", nameof(shortCode));
        }

        return new UrlMapping
        {
            ShortCode = shortCode.Trim(),
            OriginalUrl = normalizedUrl,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastAccessedAtUtc = null,
            HitCount = 0
        };
    }

    public void RecordHit(DateTimeOffset? timestamp = null)
    {
        HitCount += 1;
        LastAccessedAtUtc = timestamp ?? DateTimeOffset.UtcNow;
    }
}
