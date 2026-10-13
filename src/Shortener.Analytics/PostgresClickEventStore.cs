using Npgsql;
using Shortener.Application;

namespace Shortener.Analytics;

public sealed class InvalidClickEventException(string reason) : Exception(reason);

public sealed class PostgresClickEventStore(NpgsqlDataSource dataSource)
{
    public async Task<bool> ApplyAsync(ManagedClickEventV1 click, CancellationToken cancellationToken = default)
    {
        if (!click.IsValid()) throw new InvalidClickEventException("invalid_event");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var verify = new NpgsqlCommand("""
            SELECT 1 FROM url_mappings
            WHERE id = @mapping_id AND tenant_id = @tenant_id AND mode = 1 AND short_code = @short_code
            """, connection, transaction))
        {
            verify.Parameters.AddWithValue("mapping_id", click.MappingId);
            verify.Parameters.AddWithValue("tenant_id", click.TenantId);
            verify.Parameters.AddWithValue("short_code", click.ShortCode);
            if (await verify.ExecuteScalarAsync(cancellationToken) is null)
                throw new InvalidClickEventException("mapping_tenant_mismatch");
        }
        await using var insert = new NpgsqlCommand("""
            INSERT INTO processed_click_events (event_id, tenant_id, mapping_id)
            VALUES (@event_id, @tenant_id, @mapping_id) ON CONFLICT (event_id) DO NOTHING
            """, connection, transaction);
        insert.Parameters.AddWithValue("event_id", click.EventId);
        insert.Parameters.AddWithValue("tenant_id", click.TenantId);
        insert.Parameters.AddWithValue("mapping_id", click.MappingId);
        var inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        await using var update = new NpgsqlCommand("""
            UPDATE url_mappings SET hit_count = hit_count + 1,
                last_accessed_at_utc = GREATEST(COALESCE(last_accessed_at_utc, @occurred_at), @occurred_at)
            WHERE id = @mapping_id AND tenant_id = @tenant_id AND mode = 1
            """, connection, transaction);
        update.Parameters.AddWithValue("mapping_id", click.MappingId);
        update.Parameters.AddWithValue("tenant_id", click.TenantId);
        update.Parameters.AddWithValue("occurred_at", click.OccurredAtUtc);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidClickEventException("mapping_disappeared");
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RecordDeadLetterAsync(string topic, int partition, long offset,
        Guid? eventId, string reason, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO click_dead_letters (topic, partition_id, offset_id, event_id, reason_code)
            VALUES (@topic, @partition, @offset, @event_id, @reason)
            ON CONFLICT (topic, partition_id, offset_id) DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("topic", topic);
        command.Parameters.AddWithValue("partition", partition);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("event_id", (object?)eventId ?? DBNull.Value);
        command.Parameters.AddWithValue("reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
