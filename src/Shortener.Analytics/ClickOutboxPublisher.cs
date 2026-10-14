using System.Text.Json;
using Confluent.Kafka;
using Npgsql;
using Shortener.Application;

namespace Shortener.Analytics;

public enum PublishOutcome { NoWork, Published, Retried, Failed }

public sealed class ClickOutboxPublisher : IDisposable
{
    private const int MaximumAttempts = 5;
    private readonly NpgsqlDataSource _dataSource;
    private readonly AnalyticsOptions _options;
    private readonly ILogger<ClickOutboxPublisher> _logger;
    private readonly IProducer<string, string> _producer;

    public ClickOutboxPublisher(NpgsqlDataSource dataSource, AnalyticsOptions options, ILogger<ClickOutboxPublisher> logger)
    {
        _dataSource = dataSource;
        _options = options;
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(options.ProducerConfig()).Build();
    }

    public async Task<PublishOutcome> PublishOneAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand("""
            SELECT event_id, tenant_id, mapping_id, short_code, occurred_at_utc, trace_id, attempts,
                   event_version, owner_workspace_id, visitor_hash, referrer_group, referrer_host,
                   device_class, os_family, browser_family, language, is_bot
            FROM click_outbox
            WHERE published_at_utc IS NULL AND failed_at_utc IS NULL AND next_attempt_at_utc <= now()
            ORDER BY captured_at_utc, event_id LIMIT 1 FOR UPDATE SKIP LOCKED
            """, connection, transaction);
        IManagedClickEvent? click = null;
        int attempts = 0;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var traceId = reader.IsDBNull(5) ? null : reader.GetString(5);
                click = reader.GetInt16(7) switch
                {
                    1 when !reader.IsDBNull(1) => new ManagedClickEventV1(reader.GetGuid(0), reader.GetGuid(1),
                        reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), traceId),
                    2 when !reader.IsDBNull(8) => new ManagedClickEventV2(reader.GetGuid(0), reader.GetGuid(8),
                        reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), traceId),
                    3 when !reader.IsDBNull(8) => new ManagedClickEventV3(reader.GetGuid(0), reader.GetGuid(8),
                        reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), traceId,
                        reader.GetString(9), reader.GetString(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetString(12),
                        reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14),
                        reader.IsDBNull(15) ? null : reader.GetString(15), reader.GetBoolean(16)),
                    _ => throw new InvalidOperationException("Unsupported click outbox event version.")
                };
                attempts = reader.GetInt32(6);
            }
        }
        if (click is null) return PublishOutcome.NoWork;
        try
        {
            var (activity, headers) = ClickTraceContext.BeginPublish(click.TraceId);
            using var publishActivity = activity;
            var payload = JsonSerializer.Serialize(click, click.GetType());
            // Mapping ID keeps all events for one link on a stable Kafka partition.
            await _producer.ProduceAsync(_options.Topic,
                new Message<string, string> { Key = click.MappingId, Value = payload, Headers = headers }, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(16), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var nextAttempt = attempts + 1;
            var terminal = nextAttempt >= MaximumAttempts;
            var backoff = TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempts, 4)));
            await using var failure = new NpgsqlCommand("""
                UPDATE click_outbox SET attempts = attempts + 1,
                    next_attempt_at_utc = now() + @backoff,
                    failed_at_utc = CASE WHEN @terminal THEN now() ELSE NULL END,
                    last_error_code = @reason
                WHERE event_id = @event_id
                """, connection, transaction);
            failure.Parameters.AddWithValue("backoff", backoff);
            failure.Parameters.AddWithValue("terminal", terminal);
            failure.Parameters.AddWithValue("reason", ex.GetType().Name);
            failure.Parameters.AddWithValue("event_id", click.EventId);
            await failure.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (terminal) ClickAnalyticsMetrics.Failed.Add(1); else ClickAnalyticsMetrics.Retried.Add(1);
            _logger.LogWarning("Click publish {Outcome}. EventId={EventId} Version={Version} MappingId={MappingId} Attempt={Attempt} ExceptionType={ExceptionType} TraceId={TraceId}",
                terminal ? "failed" : "retry", click.EventId, click.Version, click.MappingId, nextAttempt, ex.GetType().Name, click.TraceId);
            return terminal ? PublishOutcome.Failed : PublishOutcome.Retried;
        }
        await using var published = new NpgsqlCommand(
            "UPDATE click_outbox SET published_at_utc = now(), attempts = attempts + 1, last_error_code = NULL WHERE event_id = @event_id",
            connection, transaction);
        published.Parameters.AddWithValue("event_id", click.EventId);
        await published.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        ClickAnalyticsMetrics.Published.Add(1);
        _logger.LogInformation("Click published. EventId={EventId} Version={Version} MappingId={MappingId} TraceId={TraceId}",
            click.EventId, click.Version, click.MappingId, click.TraceId);
        return PublishOutcome.Published;
    }

    public async Task RecordBacklogAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT count(*), COALESCE(EXTRACT(EPOCH FROM now() - min(captured_at_utc)), 0)
            FROM click_outbox WHERE published_at_utc IS NULL AND failed_at_utc IS NULL
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var count = reader.GetInt64(0);
        var oldest = reader.GetDecimal(1);
        ClickAnalyticsMetrics.OutboxPending.Record(count);
        ClickAnalyticsMetrics.OutboxOldestSeconds.Record((double)oldest);
        if (count > 0) _logger.LogInformation("Click outbox pending. Count={Count} OldestSeconds={OldestSeconds}", count, oldest);
    }

    public void Dispose() => _producer.Dispose();
}
