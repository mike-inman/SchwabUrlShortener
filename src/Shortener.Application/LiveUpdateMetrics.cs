using System.Diagnostics.Metrics;

namespace Shortener.Application;

public static class LiveUpdateMetrics
{
    public static readonly Meter Meter = new("Shortener.LiveUpdates");
    public static readonly Counter<long> Published = Meter.CreateCounter<long>("shortener.live.published");
    public static readonly Counter<long> PublishFailed = Meter.CreateCounter<long>("shortener.live.publish_failed");
    public static readonly Counter<long> Received = Meter.CreateCounter<long>("shortener.live.received");
    public static readonly Counter<long> Dispatched = Meter.CreateCounter<long>("shortener.live.dispatched");
}
