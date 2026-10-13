namespace Shortener.Application;

// The outbox and Kafka payload contain no requester identity, address, or browser data.
public sealed record ManagedClickEventV1(
    Guid EventId, Guid TenantId, string MappingId, string ShortCode,
    DateTimeOffset OccurredAtUtc, string? TraceId)
{
    public int Version { get; init; } = 1;

    public bool IsValid() => EventId != Guid.Empty && TenantId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(MappingId) &&
        Domain.UrlValidation.IsValidShortCode(ShortCode) &&
        OccurredAtUtc.Offset == TimeSpan.Zero && Version == 1;
}
