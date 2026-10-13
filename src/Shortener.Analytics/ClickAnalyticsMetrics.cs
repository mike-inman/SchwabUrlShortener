using System.Diagnostics.Metrics;

namespace Shortener.Analytics;

public static class ClickAnalyticsMetrics
{
    public static readonly Meter Meter = new("Shortener.ClickAnalytics", "1.0");
    public static readonly Counter<long> Published = Meter.CreateCounter<long>("shortener.clicks.published");
    public static readonly Counter<long> Consumed = Meter.CreateCounter<long>("shortener.clicks.consumed");
    public static readonly Counter<long> Retried = Meter.CreateCounter<long>("shortener.clicks.retried");
    public static readonly Counter<long> Deduplicated = Meter.CreateCounter<long>("shortener.clicks.deduplicated");
    public static readonly Counter<long> Failed = Meter.CreateCounter<long>("shortener.clicks.failed");
    public static readonly Histogram<long> OutboxPending = Meter.CreateHistogram<long>("shortener.clicks.outbox.pending");
    public static readonly Histogram<double> OutboxOldestSeconds = Meter.CreateHistogram<double>("shortener.clicks.outbox.oldest_seconds");
    public static readonly Histogram<long> ConsumerLag = Meter.CreateHistogram<long>("shortener.clicks.consumer.lag_records");
}
