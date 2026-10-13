using Shortener.Analytics;
using Npgsql;
using Confluent.Kafka;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    try
    {
        var healthConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb");
        var broker = Environment.GetEnvironmentVariable("Kafka__BootstrapServers");
        var topic = Environment.GetEnvironmentVariable("Kafka__Topic");
        if (string.IsNullOrWhiteSpace(healthConnectionString) || string.IsNullOrWhiteSpace(broker) || string.IsNullOrWhiteSpace(topic))
            throw new InvalidOperationException("Analytics dependencies are not configured.");
        var boundedConnection = new NpgsqlConnectionStringBuilder(healthConnectionString) { Timeout = 3, CommandTimeout = 3 };
        await using var db = new NpgsqlConnection(boundedConnection.ConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1", db);
        await command.ExecuteScalarAsync();
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = broker }).Build();
        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(3));
        Environment.ExitCode = metadata.Topics.Any(x => x.Topic == topic && x.Error.Code == ErrorCode.NoError) ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
var connectionString = builder.Configuration.GetConnectionString("ShortenerDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:ShortenerDb must be configured for click analytics.");
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
builder.Services.AddSingleton(AnalyticsOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<PostgresClickEventStore>();
builder.Services.AddSingleton<ClickOutboxPublisher>();
builder.Services.AddSingleton<ClickKafkaConsumer>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<ConsumerWorker>();
builder.Services.AddHostedService<AnalyticsDependencyMonitor>();

var host = builder.Build();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AnalyticsHost");
lifetime.ApplicationStarted.Register(() => logger.LogInformation("Service started. Service={Service}", "analytics"));
lifetime.ApplicationStopping.Register(() => logger.LogInformation("Service stopping. Service={Service}", "analytics"));
host.Run();
