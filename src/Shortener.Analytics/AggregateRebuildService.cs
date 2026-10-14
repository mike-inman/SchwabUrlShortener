using Npgsql;

namespace Shortener.Analytics;

// Idempotent, documented recovery path: recomputes the three aggregate tables from
// link_click_facts (the source of truth) in one transaction. Invoke with:
//   dotnet run --project src/Shortener.Analytics/Shortener.Analytics.csproj -- --rebuild-aggregates
public sealed class AggregateRebuildService(NpgsqlDataSource dataSource)
{
    public async Task<(long Hourly, long Dimension, long Visitor)> RebuildAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var truncate = new NpgsqlCommand(
            "TRUNCATE TABLE link_click_hourly, link_click_dimension_daily, link_visitor_daily",
            connection, transaction))
            await truncate.ExecuteNonQueryAsync(cancellationToken);

        long hourly, dimension, visitor;
        await using (var command = new NpgsqlCommand("""
            INSERT INTO link_click_hourly (mapping_id, owner_workspace_id, bucket_start_utc, clicks)
            SELECT mapping_id, owner_workspace_id, date_trunc('hour', occurred_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC', count(*)
            FROM link_click_facts WHERE NOT is_bot
            GROUP BY mapping_id, owner_workspace_id, date_trunc('hour', occurred_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            """, connection, transaction))
            hourly = await command.ExecuteNonQueryAsync(cancellationToken);

        await using (var command = new NpgsqlCommand("""
            INSERT INTO link_click_dimension_daily (mapping_id, owner_workspace_id, bucket_date_utc, dimension, dimension_key, clicks)
            SELECT mapping_id, owner_workspace_id, (occurred_at_utc AT TIME ZONE 'UTC')::date, 'referrer', referrer_group, count(*)
            FROM link_click_facts WHERE NOT is_bot AND referrer_group IS NOT NULL
            GROUP BY mapping_id, owner_workspace_id, (occurred_at_utc AT TIME ZONE 'UTC')::date, referrer_group
            UNION ALL
            SELECT mapping_id, owner_workspace_id, (occurred_at_utc AT TIME ZONE 'UTC')::date, 'device', device_class, count(*)
            FROM link_click_facts WHERE NOT is_bot AND device_class IS NOT NULL
            GROUP BY mapping_id, owner_workspace_id, (occurred_at_utc AT TIME ZONE 'UTC')::date, device_class
            """, connection, transaction))
            dimension = await command.ExecuteNonQueryAsync(cancellationToken);

        await using (var command = new NpgsqlCommand("""
            INSERT INTO link_visitor_daily (mapping_id, owner_workspace_id, bucket_date_utc, visitor_hash)
            SELECT DISTINCT mapping_id, owner_workspace_id, (occurred_at_utc AT TIME ZONE 'UTC')::date, visitor_hash
            FROM link_click_facts WHERE NOT is_bot AND visitor_hash IS NOT NULL
            """, connection, transaction))
            visitor = await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (hourly, dimension, visitor);
    }
}
