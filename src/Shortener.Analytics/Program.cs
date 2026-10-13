using Shortener.Analytics;
using Npgsql;

namespace Shortener.Analytics;

public static class AnalyticsHost
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        var connectionString = builder.Configuration.GetConnectionString("ShortenerDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:ShortenerDb must be configured for click analytics.");
        var options = AnalyticsOptions.FromConfiguration(builder.Configuration);
        builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => new AnalyticsReadiness(connectionString, options, sp.GetRequiredService<ILogger<AnalyticsReadiness>>()));

        if (args.Contains("--healthcheck", StringComparer.Ordinal))
        {
            await using var healthHost = builder.Build();
            Environment.ExitCode = await healthHost.Services.GetRequiredService<AnalyticsReadiness>().CheckAsync() ? 0 : 1;
            return;
        }

        builder.Services.AddSingleton<PostgresClickEventStore>();
        builder.Services.AddSingleton<ClickOutboxPublisher>();
        builder.Services.AddSingleton<ClickKafkaConsumer>();
        builder.Services.AddHostedService<OutboxWorker>();
        builder.Services.AddHostedService<ConsumerWorker>();
        builder.Services.AddHostedService<AnalyticsDependencyMonitor>();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")))
            builder.WebHost.UseUrls("http://0.0.0.0:8080");
        var app = builder.Build();
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
