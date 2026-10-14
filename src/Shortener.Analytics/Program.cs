using Shortener.Analytics;
using Npgsql;
using OpenTelemetry.Trace;
using Shortener.Infrastructure.LiveUpdates;
using Shortener.Shared;

namespace Shortener.Analytics;

public static class AnalyticsHost
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddLiveUpdates(builder.Configuration);
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        var telemetry = TelemetryHostConfiguration.AddConfiguredTelemetry(builder, "shortener-analytics");
        builder.Services.ConfigureOpenTelemetryTracerProvider((_, traces) => traces.AddNpgsql());
        var connectionString = builder.Configuration.GetConnectionString("ShortenerDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:ShortenerDb must be configured for click analytics.");
        var options = AnalyticsOptions.FromConfiguration(builder.Configuration);
        builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
        builder.Services.AddSingleton<AggregateRebuildService>();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => new AnalyticsReadiness(connectionString, options, sp.GetRequiredService<ILogger<AnalyticsReadiness>>()));

        if (args.Contains("--rebuild-aggregates", StringComparer.Ordinal))
        {
            await using var rebuildHost = builder.Build();
            var (hourly, dimension, visitor) = await rebuildHost.Services.GetRequiredService<AggregateRebuildService>().RebuildAsync();
            Console.WriteLine($"Aggregate rebuild complete. Hourly={hourly} Dimension={dimension} Visitor={visitor}");
            return;
        }

        if (args.Contains("--healthcheck", StringComparer.Ordinal))
        {
            await using var healthHost = builder.Build();
            Environment.ExitCode = await healthHost.Services.GetRequiredService<AnalyticsReadiness>().CheckAsync() ? 0 : 1;
            return;
        }

        builder.Services.AddSingleton<PostgresClickEventStore>();
        builder.Services.AddSingleton<ClickOutboxPublisher>();
        var contactNotificationMode = builder.Configuration["ContactNotifications:Mode"] ?? "None";
        builder.Services.AddSingleton<IContactNotifier>(sp => contactNotificationMode switch
        {
            "None" => ActivatorUtilities.CreateInstance<NoContactNotifier>(sp),
            "AzureCommunicationEmail" => ActivatorUtilities.CreateInstance<AzureCommunicationContactNotifier>(sp),
            _ => throw new InvalidOperationException("ContactNotifications:Mode must be None or AzureCommunicationEmail.")
        });
        builder.Services.AddSingleton<ContactNotificationDispatcher>();
        builder.Services.AddHostedService<ContactNotificationWorker>();
        builder.Services.AddSingleton<ClickKafkaConsumer>();
        builder.Services.AddHostedService<OutboxWorker>();
        builder.Services.AddHostedService<ConsumerWorker>();
        builder.Services.AddHostedService<AnalyticsDependencyMonitor>();
        builder.Services.AddHostedService<VisitorRetentionWorker>();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")))
            builder.WebHost.UseUrls("http://0.0.0.0:8080");
        var app = builder.Build();
        app.Logger.LogInformation("Telemetry configuration. Exporter={Exporter} SamplingRatio={SamplingRatio}", telemetry.Exporter, telemetry.SamplingRatio);
        app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("Service started. Service={Service}", "analytics"));
        app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("Service stopping. Service={Service}", "analytics"));
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", async (AnalyticsReadiness readiness, CancellationToken cancellationToken) =>
            await readiness.CheckAsync(cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.Problem(title: "Analytics dependencies are unavailable.", statusCode: 503));
        app.Run();
    }
}
