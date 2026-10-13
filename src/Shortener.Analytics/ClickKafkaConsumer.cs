using System.Text.Json;
using Confluent.Kafka;
using Shortener.Application;

namespace Shortener.Analytics;

public enum ConsumeOutcome { NoWork, Applied, Duplicate, DeadLettered, Retry }

public sealed class ClickKafkaConsumer : IDisposable
{
    private const int MaximumProcessingAttempts = 5;
    private readonly IConsumer<string, string> _consumer;
    private readonly PostgresClickEventStore _store;
    private readonly ILogger<ClickKafkaConsumer> _logger;
    private TopicPartitionOffset? _lastFailure;
    private TopicPartitionOffset? _pendingOffset;
    private int _failureCount;

    public ClickKafkaConsumer(AnalyticsOptions options, PostgresClickEventStore store, ILogger<ClickKafkaConsumer> logger)
    {
        _store = store;
        _logger = logger;
        _consumer = new ConsumerBuilder<string, string>(options.ConsumerConfig()).Build();
        _consumer.Subscribe(options.Topic);
    }

    public async Task<ConsumeOutcome> ConsumeOneAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // A failed store or commit must be revisited even if a previous Seek failed.
        if (_pendingOffset is not null) _consumer.Seek(_pendingOffset);
        var result = _consumer.Consume(timeout);
        if (result is null) return ConsumeOutcome.NoWork;
        if (_pendingOffset is not null && _pendingOffset != result.TopicPartitionOffset)
        {
            _consumer.Seek(_pendingOffset);
            return ConsumeOutcome.Retry;
        }
        _pendingOffset = result.TopicPartitionOffset;
        ManagedClickEventV1? click = null;
        try
        {
            click = JsonSerializer.Deserialize<ManagedClickEventV1>(result.Message.Value);
            if (click is null || !click.IsValid() || result.Message.Key != click.MappingId)
                throw new InvalidClickEventException("invalid_event");
            var applied = await _store.ApplyAsync(click, cancellationToken);
            _consumer.Commit(result);
            _pendingOffset = null;
            _lastFailure = null;
            _failureCount = 0;
            if (applied) ClickAnalyticsMetrics.Consumed.Add(1); else ClickAnalyticsMetrics.Deduplicated.Add(1);
            RecordLag(result);
            _logger.LogInformation("Click {Outcome}. EventId={EventId} TenantId={TenantId} MappingId={MappingId} TraceId={TraceId}",
                applied ? "consumed" : "deduplicated", click.EventId, click.TenantId, click.MappingId, click.TraceId);
            return applied ? ConsumeOutcome.Applied : ConsumeOutcome.Duplicate;
        }
        catch (Exception ex) when (ex is JsonException or InvalidClickEventException)
        {
            var reason = ex is InvalidClickEventException invalid ? invalid.Message : "invalid_json";
            try
            {
                await _store.RecordDeadLetterAsync(result.Topic, result.Partition.Value, result.Offset.Value,
                    click?.EventId, reason, cancellationToken);
                _consumer.Commit(result);
            }
            catch (Exception deadLetterError) when (deadLetterError is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                CountFailure(result);
                _logger.LogError("Click dead-letter persistence or offset commit failed; retrying same Kafka offset. ExceptionType={ExceptionType}",
                    deadLetterError.GetType().Name);
                return SeekForRetry(result, click, deadLetterError);
            }
            _pendingOffset = null;
            _lastFailure = null;
            _failureCount = 0;
            ClickAnalyticsMetrics.Failed.Add(1);
            _logger.LogWarning("Click dead-lettered. EventId={EventId} TenantId={TenantId} MappingId={MappingId} Reason={Reason}",
                click?.EventId, click?.TenantId, click?.MappingId, reason);
            return ConsumeOutcome.DeadLettered;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            CountFailure(result);
            if (_failureCount >= MaximumProcessingAttempts)
            {
                try
                {
                    await _store.RecordDeadLetterAsync(result.Topic, result.Partition.Value, result.Offset.Value,
                        click?.EventId, "processing_exhausted", cancellationToken);
                    _consumer.Commit(result);
                    _pendingOffset = null;
                    ClickAnalyticsMetrics.Failed.Add(1);
                    _logger.LogError("Click processing exhausted and dead-lettered. EventId={EventId} TenantId={TenantId} MappingId={MappingId} ExceptionType={ExceptionType}",
                        click?.EventId, click?.TenantId, click?.MappingId, ex.GetType().Name);
                    _lastFailure = null;
                    _failureCount = 0;
                    return ConsumeOutcome.DeadLettered;
                }
                catch (Exception deadLetterError) when (deadLetterError is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError("Click dead-letter storage unavailable; retaining Kafka offset. ExceptionType={ExceptionType}",
                        deadLetterError.GetType().Name);
                }
            }
            return SeekForRetry(result, click, ex);
        }
    }

    private void CountFailure(ConsumeResult<string, string> result)
    {
        if (_lastFailure == result.TopicPartitionOffset) _failureCount++; else
        {
            _lastFailure = result.TopicPartitionOffset;
            _failureCount = 1;
        }
    }

    private ConsumeOutcome SeekForRetry(ConsumeResult<string, string> result, ManagedClickEventV1? click, Exception error)
    {
        // Keep the marker until a successful commit. If Seek throws, the next call
        // retries it before Consume can move forward.
        _pendingOffset = result.TopicPartitionOffset;
        _consumer.Seek(result.TopicPartitionOffset);
        ClickAnalyticsMetrics.Retried.Add(1);
        _logger.LogWarning("Click processing retry. EventId={EventId} TenantId={TenantId} MappingId={MappingId} Attempt={Attempt} ExceptionType={ExceptionType}",
            click?.EventId, click?.TenantId, click?.MappingId, _failureCount, error.GetType().Name);
        return ConsumeOutcome.Retry;
    }

    private void RecordLag(ConsumeResult<string, string> result)
    {
        try
        {
            var watermark = _consumer.QueryWatermarkOffsets(result.TopicPartition, TimeSpan.FromMilliseconds(500));
            ClickAnalyticsMetrics.ConsumerLag.Record(Math.Max(0, watermark.High.Value - result.Offset.Value - 1));
        }
        catch (KafkaException) { /* Lag telemetry must not change offset handling. */ }
    }

    public void Dispose() => _consumer.Dispose();
}
