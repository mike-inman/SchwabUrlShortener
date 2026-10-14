namespace Shortener.Application;

// Extends the V2 (workspace) lineage only; the legacy V1 (tenant) capture path is unchanged.
// The outbox and Kafka payload still contain no raw IP, full user agent, or full referrer URL.
public sealed record ManagedClickEventV3(
    Guid EventId, Guid WorkspaceId, string MappingId, string ShortCode,
    DateTimeOffset OccurredAtUtc, string? TraceId,
    string VisitorHash, string ReferrerGroup, string? ReferrerHost,
    string DeviceClass, string? OsFamily, string? BrowserFamily, string? Language, bool IsBot) : IManagedClickEvent
{
    public static readonly IReadOnlySet<string> ReferrerGroups =
        new HashSet<string> { "LinkedIn", "X", "Facebook", "Instagram", "Email", "Google", "Other", "Direct" };
    public static readonly IReadOnlySet<string> DeviceClasses =
        new HashSet<string> { "Mobile", "Desktop", "Tablet", "Bot", "Unknown" };

    public int Version { get; init; } = 3;

    public bool IsValid() => EventId != Guid.Empty && WorkspaceId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(MappingId) &&
        Domain.UrlValidation.IsValidShortCode(ShortCode) &&
        OccurredAtUtc.Offset == TimeSpan.Zero && Version == 3 &&
        VisitorHash.Length == 32 && VisitorHash.All(Uri.IsHexDigit) &&
        ReferrerGroups.Contains(ReferrerGroup) && DeviceClasses.Contains(DeviceClass);
}
