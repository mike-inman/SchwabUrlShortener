using Npgsql;

namespace Shortener.Analytics;

// 35-day retention for visitor-identifying data (ADR Decision 7). Deletes link_visitor_daily
// rows and nulls link_click_facts.visitor_hash past the retention window. Logs row counts only
// - never hashes, IPs, or user agents.
public sealed class VisitorRetentionWorker(NpgsqlDataSource dataSource, ILogger<VisitorRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(35);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Visitor retention sweep failed; retrying next interval. ExceptionType={ExceptionType}", ex.GetType().Name);
            }
            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - RetentionWindow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using var deleteVisitorDaily = new NpgsqlCommand(
            "DELETE FROM link_visitor_daily WHERE bucket_date_utc < @cutoff", connection);
        deleteVisitorDaily.Parameters.AddWithValue("cutoff", DateOnly.FromDateTime(cutoff.UtcDateTime));
        var deletedVisitorRows = await deleteVisitorDaily.ExecuteNonQueryAsync(cancellationToken);

        await using var nullFactHashes = new NpgsqlCommand(
            "UPDATE link_click_facts SET visitor_hash = NULL WHERE visitor_hash IS NOT NULL AND occurred_at_utc < @cutoff", connection);
        nullFactHashes.Parameters.AddWithValue("cutoff", cutoff);
        var nulledFactRows = await nullFactHashes.ExecuteNonQueryAsync(cancellationToken);

        logger.LogInformation("Visitor retention sweep complete. DeletedVisitorDailyRows={DeletedVisitorDailyRows} NulledFactHashes={NulledFactHashes}",
            deletedVisitorRows, nulledFactRows);
    }
}
