namespace Shortener.Application;

public interface IManagedClickEvent
{
    Guid EventId { get; }
    string MappingId { get; }
    string ShortCode { get; }
    DateTimeOffset OccurredAtUtc { get; }
    string? TraceId { get; }
    int Version { get; }
    bool IsValid();
}

// The outbox and Kafka payload contain no requester identity, address, or browser data.
public sealed record ManagedClickEventV1(
    Guid EventId, Guid TenantId, string MappingId, string ShortCode,
    DateTimeOffset OccurredAtUtc, string? TraceId) : IManagedClickEvent
{
    public int Version { get; init; } = 1;

    public bool IsValid() => EventId != Guid.Empty && TenantId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(MappingId) &&
        Domain.UrlValidation.IsValidShortCode(ShortCode) &&
        OccurredAtUtc.Offset == TimeSpan.Zero && Version == 1;
}
