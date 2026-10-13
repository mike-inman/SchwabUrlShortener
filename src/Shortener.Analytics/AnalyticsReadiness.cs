using Confluent.Kafka;
using Npgsql;

namespace Shortener.Analytics;

public sealed class AnalyticsReadiness(string connectionString, AnalyticsOptions options, ILogger<AnalyticsReadiness> logger)
{
    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        var postgresReady = false;
        var kafkaReady = false;
        try
        {
            var bounded = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 3, CommandTimeout = 3 };
            await using var db = new NpgsqlConnection(bounded.ConnectionString);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await db.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT 1", db);
            postgresReady = (int)(await command.ExecuteScalarAsync(timeout.Token) ?? 0) == 1;
            if (!postgresReady)
                logger.LogWarning("Analytics readiness failed. Dependency={Dependency} ExceptionType={ExceptionType}", "PostgreSQL", "CheckFailed");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Analytics readiness failed. Dependency={Dependency} ExceptionType={ExceptionType}", "PostgreSQL", ex.GetType().Name);
        }
        try
        {
            using var admin = new AdminClientBuilder(options.AdminConfig()).Build();
            var metadata = admin.GetMetadata(options.Topic, TimeSpan.FromSeconds(3));
            kafkaReady = metadata.Brokers.Count > 0 &&
                metadata.Topics.Any(topic => topic.Topic == options.Topic && !topic.Error.IsError);
            if (!kafkaReady)
                logger.LogWarning("Analytics readiness failed. Dependency={Dependency} ExceptionType={ExceptionType}", "Kafka", "TopicUnavailable");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Analytics readiness failed. Dependency={Dependency} ExceptionType={ExceptionType}", "Kafka", ex.GetType().Name);
        }
        return postgresReady && kafkaReady;
    }
}
