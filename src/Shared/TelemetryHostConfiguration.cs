using System.Globalization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shortener.Shared;

public static class TelemetryHostConfiguration
{
    public static (string Exporter, float SamplingRatio) AddConfiguredTelemetry(
        WebApplicationBuilder builder, string serviceName)
    {
        var rawRatio = builder.Configuration["OpenTelemetry:SamplingRatio"];
        if (!float.TryParse(rawRatio, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ||
            ratio < 0 || ratio > 1)
            throw new InvalidOperationException("OpenTelemetry:SamplingRatio must be between 0 and 1.");
        if (!bool.TryParse(builder.Configuration["OpenTelemetry:ConsoleExporter"], out var console))
            throw new InvalidOperationException("OpenTelemetry:ConsoleExporter must be true or false.");
        var connection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        var azure = !string.IsNullOrWhiteSpace(connection);
        if (azure && console)
            throw new InvalidOperationException("Configure either Azure Monitor or the local console exporter, not both.");

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(traces =>
            {
                traces.AddSource("Shortener.ClickPipeline", "Shortener.Redis");
                if (console) traces.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("Shortener.ClickAnalytics", "Shortener.LiveUpdates", "Shortener.RateLimiting");
                if (console) metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddConsoleExporter();
            });
        if (azure)
            telemetry.UseAzureMonitor(options =>
            {
                options.ConnectionString = connection;
                options.SamplingRatio = ratio;
                options.TracesPerSecond = null;
            });
        return (azure ? "AzureMonitor" : console ? "Console" : "None", ratio);
    }
}
