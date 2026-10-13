using Confluent.Kafka;
using Npgsql;

namespace Shortener.Analytics;

public sealed class AnalyticsDependencyMonitor(
    NpgsqlDataSource dataSource, AnalyticsOptions options, ILogger<AnalyticsDependencyMonitor> logger) : BackgroundService
{
    private const string ReadyFile = "/tmp/schwab-analytics-ready";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        File.Delete(ReadyFile);
        var wasReady = false;
        using var broker = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            var postgresReady = false;
            var kafkaReady = false;
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                postgresReady = (int)(await command.ExecuteScalarAsync(stoppingToken) ?? 0) == 1;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Analytics PostgreSQL unavailable. ExceptionType={ExceptionType}", ex.GetType().Name);
            }
            try
            {
                var metadata = broker.GetMetadata(options.Topic, TimeSpan.FromSeconds(3));
                kafkaReady = metadata.Brokers.Count > 0 &&
                    metadata.Topics.Any(topic => topic.Topic == options.Topic && !topic.Error.IsError);
            }
            catch (Exception ex) when (ex is KafkaException or TimeoutException)
            {
                logger.LogWarning("Analytics Kafka unavailable. ExceptionType={ExceptionType}", ex.GetType().Name);
            }
            var ready = postgresReady && kafkaReady;
            if (ready) File.WriteAllText(ReadyFile, "ready");
            else File.Delete(ReadyFile);
            if (ready != wasReady)
            {
                logger.LogInformation("Analytics dependency readiness changed. Ready={Ready} PostgreSQL={PostgreSQL} Kafka={Kafka}",
                    ready, postgresReady, kafkaReady);
                wasReady = ready;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
        File.Delete(ReadyFile);
    }
}
