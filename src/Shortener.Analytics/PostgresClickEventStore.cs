using Npgsql;
using Shortener.Application;

namespace Shortener.Analytics;

public sealed class InvalidClickEventException(string reason) : Exception(reason);

public sealed class PostgresClickEventStore(NpgsqlDataSource dataSource)
{
    public Task<bool> ApplyAsync(IManagedClickEvent click, CancellationToken cancellationToken = default) => click switch
    {
        ManagedClickEventV1 v1 => ApplyAsync(v1, cancellationToken),
        ManagedClickEventV2 v2 => ApplyAsync(v2, cancellationToken),
        ManagedClickEventV3 v3 => ApplyAsync(v3, cancellationToken),
        _ => throw new InvalidClickEventException("unsupported_version")
    };

    public async Task<bool> ApplyAsync(ManagedClickEventV3 click, CancellationToken cancellationToken = default)
    {
        if (!click.IsValid()) throw new InvalidClickEventException("invalid_event");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var verify = new NpgsqlCommand("""
            SELECT 1 FROM url_mappings
            WHERE id = @mapping_id AND owner_workspace_id = @workspace_id AND tenant_id IS NULL
              AND mode = 1 AND short_code = @short_code
            """, connection, transaction))
        {
            verify.Parameters.AddWithValue("mapping_id", click.MappingId);
            verify.Parameters.AddWithValue("workspace_id", click.WorkspaceId);
            verify.Parameters.AddWithValue("short_code", click.ShortCode);
            if (await verify.ExecuteScalarAsync(cancellationToken) is null)
                throw new InvalidClickEventException("mapping_workspace_mismatch");
        }
        if (!await TryMarkProcessedAsync(connection, transaction, click.EventId, null, click.WorkspaceId, 3, click.MappingId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        await UpdateHitCountAsync(connection, transaction, click.MappingId, null, click.WorkspaceId, click.OccurredAtUtc, cancellationToken);
        await WriteFactAndAggregatesAsync(connection, transaction, click.EventId, click.MappingId, click.WorkspaceId,
            click.ShortCode, click.OccurredAtUtc, 3, click.ReferrerGroup, click.ReferrerHost, click.DeviceClass,
            click.OsFamily, click.BrowserFamily, click.Language, click.VisitorHash, click.IsBot, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ApplyAsync(ManagedClickEventV2 click, CancellationToken cancellationToken = default)
    {
        if (!click.IsValid()) throw new InvalidClickEventException("invalid_event");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var verify = new NpgsqlCommand("""
            SELECT 1 FROM url_mappings
            WHERE id = @mapping_id AND owner_workspace_id = @workspace_id AND tenant_id IS NULL
              AND mode = 1 AND short_code = @short_code
            """, connection, transaction))
        {
            verify.Parameters.AddWithValue("mapping_id", click.MappingId);
            verify.Parameters.AddWithValue("workspace_id", click.WorkspaceId);
            verify.Parameters.AddWithValue("short_code", click.ShortCode);
            if (await verify.ExecuteScalarAsync(cancellationToken) is null)
                throw new InvalidClickEventException("mapping_workspace_mismatch");
        }
        if (!await TryMarkProcessedAsync(connection, transaction, click.EventId, null, click.WorkspaceId, 2, click.MappingId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        await UpdateHitCountAsync(connection, transaction, click.MappingId, null, click.WorkspaceId, click.OccurredAtUtc, cancellationToken);
        await WriteFactAndAggregatesAsync(connection, transaction, click.EventId, click.MappingId, click.WorkspaceId,
            click.ShortCode, click.OccurredAtUtc, 2, null, null, null, null, null, null, null, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ApplyAsync(ManagedClickEventV1 click, CancellationToken cancellationToken = default)
    {
        if (!click.IsValid()) throw new InvalidClickEventException("invalid_event");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid ownerWorkspaceId;
        await using (var verify = new NpgsqlCommand("""
            SELECT owner_workspace_id FROM url_mappings
            WHERE id = @mapping_id AND tenant_id = @tenant_id AND mode = 1 AND short_code = @short_code
            """, connection, transaction))
        {
            verify.Parameters.AddWithValue("mapping_id", click.MappingId);
            verify.Parameters.AddWithValue("tenant_id", click.TenantId);
            verify.Parameters.AddWithValue("short_code", click.ShortCode);
            var result = await verify.ExecuteScalarAsync(cancellationToken);
            if (result is null or DBNull) throw new InvalidClickEventException("mapping_tenant_mismatch");
            ownerWorkspaceId = (Guid)result;
        }
        if (!await TryMarkProcessedAsync(connection, transaction, click.EventId, click.TenantId, null, 1, click.MappingId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        await UpdateHitCountAsync(connection, transaction, click.MappingId, click.TenantId, null, click.OccurredAtUtc, cancellationToken);
        await WriteFactAndAggregatesAsync(connection, transaction, click.EventId, click.MappingId, ownerWorkspaceId,
            click.ShortCode, click.OccurredAtUtc, 1, null, null, null, null, null, null, null, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> TryMarkProcessedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid eventId, Guid? tenantId, Guid? workspaceId, short eventVersion, string mappingId, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO processed_click_events (event_id, tenant_id, owner_workspace_id, event_version, mapping_id)
            VALUES (@event_id, @tenant_id, @workspace_id, @event_version, @mapping_id) ON CONFLICT (event_id) DO NOTHING
            """, connection, transaction);
        insert.Parameters.AddWithValue("event_id", eventId);
        insert.Parameters.AddWithValue("tenant_id", (object?)tenantId ?? DBNull.Value);
        insert.Parameters.AddWithValue("workspace_id", (object?)workspaceId ?? DBNull.Value);
        insert.Parameters.AddWithValue("event_version", eventVersion);
        insert.Parameters.AddWithValue("mapping_id", mappingId);
        return await insert.ExecuteNonQueryAsync(cancellationToken) != 0;
    }

    private static async Task UpdateHitCountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string mappingId, Guid? tenantId, Guid? workspaceId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand(tenantId is not null
            ? """
              UPDATE url_mappings SET hit_count = hit_count + 1,
                  last_accessed_at_utc = GREATEST(COALESCE(last_accessed_at_utc, @occurred_at), @occurred_at)
              WHERE id = @mapping_id AND tenant_id = @tenant_id AND mode = 1
              """
            : """
              UPDATE url_mappings SET hit_count = hit_count + 1,
                  last_accessed_at_utc = GREATEST(COALESCE(last_accessed_at_utc, @occurred_at), @occurred_at)
              WHERE id = @mapping_id AND owner_workspace_id = @workspace_id AND tenant_id IS NULL AND mode = 1
              """, connection, transaction);
        update.Parameters.AddWithValue("mapping_id", mappingId);
        update.Parameters.AddWithValue("occurred_at", occurredAtUtc);
        if (tenantId is not null) update.Parameters.AddWithValue("tenant_id", tenantId.Value);
        else update.Parameters.AddWithValue("workspace_id", workspaceId!.Value);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidClickEventException("mapping_disappeared");
    }

    // The fact table is the source of truth; the three aggregate tables are an idempotent fast path
    // updated in the same transaction (see docs/2026-10-13-1500-link-insights-collection-adr.md,
    // Decision 4). Bot events get a fact row for transparency but update no aggregate.
    private static async Task WriteFactAndAggregatesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid eventId, string mappingId, Guid ownerWorkspaceId, string shortCode, DateTimeOffset occurredAtUtc,
        short eventVersion, string? referrerGroup, string? referrerHost, string? deviceClass, string? osFamily,
        string? browserFamily, string? language, string? visitorHash, bool isBot, CancellationToken cancellationToken)
    {
        await using (var fact = new NpgsqlCommand("""
            INSERT INTO link_click_facts (event_id, mapping_id, owner_workspace_id, short_code, occurred_at_utc,
                referrer_group, referrer_host, device_class, os_family, browser_family, language, visitor_hash,
                is_bot, country_code, event_version)
            VALUES (@event_id, @mapping_id, @owner_workspace_id, @short_code, @occurred_at_utc,
                @referrer_group, @referrer_host, @device_class, @os_family, @browser_family, @language, @visitor_hash,
                @is_bot, NULL, @event_version)
            ON CONFLICT (event_id) DO NOTHING
            """, connection, transaction))
        {
            fact.Parameters.AddWithValue("event_id", eventId);
            fact.Parameters.AddWithValue("mapping_id", mappingId);
            fact.Parameters.AddWithValue("owner_workspace_id", ownerWorkspaceId);
            fact.Parameters.AddWithValue("short_code", shortCode);
            fact.Parameters.AddWithValue("occurred_at_utc", occurredAtUtc);
            fact.Parameters.AddWithValue("referrer_group", (object?)referrerGroup ?? DBNull.Value);
            fact.Parameters.AddWithValue("referrer_host", (object?)referrerHost ?? DBNull.Value);
            fact.Parameters.AddWithValue("device_class", (object?)deviceClass ?? DBNull.Value);
            fact.Parameters.AddWithValue("os_family", (object?)osFamily ?? DBNull.Value);
            fact.Parameters.AddWithValue("browser_family", (object?)browserFamily ?? DBNull.Value);
            fact.Parameters.AddWithValue("language", (object?)language ?? DBNull.Value);
            fact.Parameters.AddWithValue("visitor_hash", (object?)visitorHash ?? DBNull.Value);
            fact.Parameters.AddWithValue("is_bot", isBot);
            fact.Parameters.AddWithValue("event_version", eventVersion);
            await fact.ExecuteNonQueryAsync(cancellationToken);
        }

        if (isBot) return;

        await using (var hourly = new NpgsqlCommand("""
            INSERT INTO link_click_hourly (mapping_id, owner_workspace_id, bucket_start_utc, clicks)
            VALUES (@mapping_id, @owner_workspace_id, date_trunc('hour', @occurred_at_utc), 1)
            ON CONFLICT (mapping_id, bucket_start_utc) DO UPDATE SET clicks = link_click_hourly.clicks + 1
            """, connection, transaction))
        {
            hourly.Parameters.AddWithValue("mapping_id", mappingId);
            hourly.Parameters.AddWithValue("owner_workspace_id", ownerWorkspaceId);
            hourly.Parameters.AddWithValue("occurred_at_utc", occurredAtUtc);
            await hourly.ExecuteNonQueryAsync(cancellationToken);
        }

        // Dimension and visitor aggregates are V3-only; V1/V2 carry no referrer/device signal.
        if (referrerGroup is null || deviceClass is null) return;

        foreach (var (dimension, key) in new[] { ("referrer", referrerGroup), ("device", deviceClass) })
        {
            await using var dim = new NpgsqlCommand("""
                INSERT INTO link_click_dimension_daily (mapping_id, owner_workspace_id, bucket_date_utc, dimension, dimension_key, clicks)
                VALUES (@mapping_id, @owner_workspace_id, date_trunc('day', @occurred_at_utc)::date, @dimension, @key, 1)
                ON CONFLICT (mapping_id, bucket_date_utc, dimension, dimension_key) DO UPDATE SET clicks = link_click_dimension_daily.clicks + 1
                """, connection, transaction);
            dim.Parameters.AddWithValue("mapping_id", mappingId);
            dim.Parameters.AddWithValue("owner_workspace_id", ownerWorkspaceId);
            dim.Parameters.AddWithValue("occurred_at_utc", occurredAtUtc);
            dim.Parameters.AddWithValue("dimension", dimension);
            dim.Parameters.AddWithValue("key", key);
            await dim.ExecuteNonQueryAsync(cancellationToken);
        }

        if (visitorHash is null) return;
        await using (var visitor = new NpgsqlCommand("""
            INSERT INTO link_visitor_daily (mapping_id, owner_workspace_id, bucket_date_utc, visitor_hash)
            VALUES (@mapping_id, @owner_workspace_id, date_trunc('day', @occurred_at_utc)::date, @visitor_hash)
            ON CONFLICT (mapping_id, bucket_date_utc, visitor_hash) DO NOTHING
            """, connection, transaction))
        {
            visitor.Parameters.AddWithValue("mapping_id", mappingId);
            visitor.Parameters.AddWithValue("owner_workspace_id", ownerWorkspaceId);
            visitor.Parameters.AddWithValue("occurred_at_utc", occurredAtUtc);
            visitor.Parameters.AddWithValue("visitor_hash", visitorHash);
            await visitor.ExecuteNonQueryAsync(cancellationToken);
        }
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
