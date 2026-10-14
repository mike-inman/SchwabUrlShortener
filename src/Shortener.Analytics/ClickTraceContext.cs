using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace Shortener.Analytics;

public static class ClickTraceContext
{
    public const string SourceName = "Shortener.ClickPipeline";
    private static readonly ActivitySource Source = new(SourceName);
    private const string HeaderName = "traceparent";

    public static (Activity? Activity, Headers Headers) BeginPublish(string? persistedTraceparent)
    {
        var headers = new Headers();
        if (!TryParse(persistedTraceparent, out var parent)) return (null, headers);
        var activity = Source.StartActivity("click.outbox.publish", ActivityKind.Producer, parent);
        headers.Add(HeaderName, Encoding.ASCII.GetBytes(activity?.Id ?? persistedTraceparent!));
        return (activity, headers);
    }

    public static Activity? BeginConsume(Headers? headers, string? fallbackTraceparent)
    {
        var fromHeader = headers?.TryGetLastBytes(HeaderName, out var bytes) == true && bytes is { Length: > 0 and <= 128 }
            ? Encoding.ASCII.GetString(bytes) : null;
        if (!TryParse(fromHeader, out var parent) && !TryParse(fallbackTraceparent, out parent)) return null;
        return Source.StartActivity("click.kafka.consume", ActivityKind.Consumer, parent);
    }

    private static bool TryParse(string? traceparent, out ActivityContext context)
    {
        context = default;
        return traceparent is { Length: 55 } &&
            ActivityContext.TryParse(traceparent, null, out context) && context.TraceId != default;
    }
}
