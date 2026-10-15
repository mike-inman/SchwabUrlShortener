using System.Diagnostics.Metrics;

namespace Shortener.Application;

public static class ContactMetrics
{
    private static readonly Meter Meter = new("Shortener.Contact");
    public static readonly Counter<long> Received = Meter.CreateCounter<long>("shortener.contact.received");
    public static readonly Counter<long> Rejected = Meter.CreateCounter<long>("shortener.contact.rejected");
    public static readonly Counter<long> Notified = Meter.CreateCounter<long>("shortener.contact.notified");
    public static readonly Counter<long> NotifyFailed = Meter.CreateCounter<long>("shortener.contact.notify_failed");
}
