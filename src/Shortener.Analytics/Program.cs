using Shortener.Analytics;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
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
host.Run();
