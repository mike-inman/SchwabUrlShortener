namespace Shortener.Analytics;

public sealed class OutboxWorker(ClickOutboxPublisher publisher, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastBacklog = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await publisher.PublishOneAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastBacklog >= TimeSpan.FromSeconds(15))
                {
                    await publisher.RecordBacklogAsync(stoppingToken);
                    lastBacklog = DateTimeOffset.UtcNow;
                }
                if (outcome != PublishOutcome.Published) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Outbox worker failed; retrying. ExceptionType={ExceptionType}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}

public sealed class ConsumerWorker(ClickKafkaConsumer consumer, ILogger<ConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await consumer.ConsumeOneAsync(TimeSpan.FromSeconds(1), stoppingToken);
                if (outcome == ConsumeOutcome.Retry) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Click consumer failed; retrying without committing offset. ExceptionType={ExceptionType}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
