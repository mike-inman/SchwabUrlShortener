namespace Shortener.Application;

// Workspace ID is application ownership. It is never inferred from provider tenant ID.
public sealed record ManagedClickEventV2(
    Guid EventId, Guid WorkspaceId, string MappingId, string ShortCode,
    DateTimeOffset OccurredAtUtc, string? TraceId) : IManagedClickEvent
{
    public int Version { get; init; } = 2;

    public bool IsValid() => EventId != Guid.Empty && WorkspaceId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(MappingId) &&
        Domain.UrlValidation.IsValidShortCode(ShortCode) &&
        OccurredAtUtc.Offset == TimeSpan.Zero && Version == 2;
}
