using Npgsql;

namespace Shortener.Infrastructure;

public sealed record InsightPoint(DateTimeOffset BucketStartUtc, long Clicks);
public sealed record InsightGroup(string Name, long Clicks);
public sealed record InsightLastClick(DateTimeOffset OccurredAtUtc, string? ReferrerGroup, string? DeviceClass);
public sealed record LinkInsights(long TotalClicks, long PreviousPeriodTotal, long UniqueVisitors,
    IReadOnlyList<InsightPoint> TimeSeries, IReadOnlyList<InsightGroup> TopReferrers,
    IReadOnlyList<InsightGroup> DeviceShares, InsightLastClick? LastClick,
    IReadOnlyList<InsightPoint> HeatmapHours);
public sealed record LinkSparkline(string ShortCode, IReadOnlyList<InsightPoint> Days);

public sealed class LinkInsightsReader(NpgsqlDataSource dataSource, TimeProvider clock)
{
    public static bool TryRange(string? range, DateTimeOffset nowUtc, out DateTimeOffset startUtc,
        out DateTimeOffset endUtc, out TimeSpan step)
    {
        var today = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
        var hour = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, TimeSpan.Zero);
        switch (range)
        {
            case "24h": endUtc = hour.AddHours(1); step = TimeSpan.FromHours(1); startUtc = endUtc.AddHours(-24); return true;
            case "7d": endUtc = today.AddDays(1); step = TimeSpan.FromDays(1); startUtc = endUtc.AddDays(-7); return true;
            case "30d": endUtc = today.AddDays(1); step = TimeSpan.FromDays(1); startUtc = endUtc.AddDays(-30); return true;
            default: startUtc = endUtc = default; step = default; return false;
        }
    }

    public async Task<LinkInsights> GetAsync(Guid workspaceId, string mappingId, string range, CancellationToken cancellationToken = default)
    {
        _ = TryRange(range, clock.GetUtcNow(), out var start, out var end, out var step);
        var previous = start - (end - start);
        var totals = new Dictionary<DateTimeOffset, long>();
        var heat = new Dictionary<DateTimeOffset, long>();
        var refs = new Dictionary<string, long>(StringComparer.Ordinal);
        var devices = new Dictionary<string, long>(StringComparer.Ordinal);
        long unique = 0;
        InsightLastClick? last = null;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(step == TimeSpan.FromHours(1) ? """
            SELECT bucket_start_utc, clicks FROM link_click_hourly
            WHERE owner_workspace_id = @workspace AND mapping_id = @mapping
              AND bucket_start_utc >= @start AND bucket_start_utc < @end
            ORDER BY bucket_start_utc
            """ : """
            SELECT date_trunc('day', bucket_start_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC', sum(clicks)::bigint
            FROM link_click_hourly
            WHERE owner_workspace_id = @workspace AND mapping_id = @mapping
              AND bucket_start_utc >= @start AND bucket_start_utc < @end
            GROUP BY 1 ORDER BY 1
            """, connection))
        {
            AddScope(command, workspaceId, mappingId);
            command.Parameters.AddWithValue("start", previous);
            command.Parameters.AddWithValue("end", end);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                totals[reader.GetFieldValue<DateTimeOffset>(0)] = reader.GetInt64(1);
        }
        await using (var command = new NpgsqlCommand(step == TimeSpan.FromHours(1) ? """
            SELECT count(DISTINCT ((occurred_at_utc AT TIME ZONE 'UTC')::date, visitor_hash))
            FROM link_click_facts
            WHERE owner_workspace_id = @workspace AND mapping_id = @mapping AND NOT is_bot
              AND occurred_at_utc >= @start AND occurred_at_utc < @end AND visitor_hash IS NOT NULL
            """ : """
            SELECT count(*) FROM link_visitor_daily
            WHERE owner_workspace_id = @workspace AND mapping_id = @mapping
              AND bucket_date_utc >= @start AND bucket_date_utc < @end
            """, connection))
        {
            AddScope(command, workspaceId, mappingId);
            AddRange(command, start, end, step == TimeSpan.FromDays(1));
            unique = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }
        await using (var command = new NpgsqlCommand(step == TimeSpan.FromHours(1) ? """
            SELECT dimension, dimension_key, count(*) FROM (
                SELECT 'referrer' AS dimension, referrer_group AS dimension_key
                FROM link_click_facts WHERE owner_workspace_id = @workspace AND mapping_id = @mapping
                  AND NOT is_bot AND occurred_at_utc >= @start AND occurred_at_utc < @end AND referrer_group IS NOT NULL
                UNION ALL
                SELECT 'device', device_class FROM link_click_facts
                WHERE owner_workspace_id = @workspace AND mapping_id = @mapping
                  AND NOT is_bot AND occurred_at_utc >= @start AND occurred_at_utc < @end AND device_class IS NOT NULL
            ) dimensions GROUP BY dimension, dimension_key
            """ : """
            SELECT dimension, dimension_key, sum(clicks)::bigint FROM link_click_dimension_daily
            WHERE owner_workspace_id = @workspace AND mapping_id = @mapping
              AND bucket_date_utc >= @start AND bucket_date_utc < @end
            GROUP BY dimension, dimension_key
            """, connection))
        {
            AddScope(command, workspaceId, mappingId);
            AddRange(command, start, end, step == TimeSpan.FromDays(1));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var target = reader.GetString(0) == "referrer" ? refs : devices;
                target[reader.GetString(1)] = reader.GetInt64(2);
            }
        }
        await using (var command = new NpgsqlCommand("""
            SELECT occurred_at_utc, referrer_group, device_class FROM link_click_facts
            WHERE owner_workspace_id = @workspace AND mapping_id = @mapping AND NOT is_bot
            ORDER BY occurred_at_utc DESC LIMIT 1
            """, connection))
        {
            AddScope(command, workspaceId, mappingId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                last = new(reader.GetFieldValue<DateTimeOffset>(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2));
        }
        var heatStart = end.AddDays(-28);
        await using (var command = new NpgsqlCommand("""
            SELECT bucket_start_utc, clicks FROM link_click_hourly
            WHERE owner_workspace_id = @workspace AND mapping_id = @mapping
              AND bucket_start_utc >= @start AND bucket_start_utc < @end
            ORDER BY bucket_start_utc
            """, connection))
        {
            AddScope(command, workspaceId, mappingId);
            command.Parameters.AddWithValue("start", heatStart);
            command.Parameters.AddWithValue("end", end);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                heat[reader.GetFieldValue<DateTimeOffset>(0)] = reader.GetInt64(1);
        }
        var series = Fill(totals, start, end, step);
        var total = series.Sum(x => x.Clicks);
        if (total > refs.Values.Sum()) refs["Unknown"] = total - refs.Values.Sum();
        if (total > devices.Values.Sum()) devices["Unknown"] = total - devices.Values.Sum();
        return new(total, totals.Where(x => x.Key >= previous && x.Key < start).Sum(x => x.Value),
            unique, series, TopReferrers(refs), devices.OrderByDescending(x => x.Value).Select(x => new InsightGroup(x.Key, x.Value)).ToArray(),
            last, Fill(heat, heatStart, end, TimeSpan.FromHours(1)));
    }

    public async Task<IReadOnlyList<LinkSparkline>> GetSparklinesAsync(Guid workspaceId, int days, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var end = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        var start = end.AddDays(-days);
        var byCode = new Dictionary<string, Dictionary<DateTimeOffset, long>>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT m.short_code, date_trunc('day', h.bucket_start_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS day, sum(h.clicks)::bigint
            FROM url_mappings m
            LEFT JOIN link_click_hourly h ON h.mapping_id = m.id AND h.owner_workspace_id = @workspace
                AND h.bucket_start_utc >= @start AND h.bucket_start_utc < @end
            WHERE m.owner_workspace_id = @workspace AND m.tenant_id IS NULL AND m.mode = 1
            GROUP BY m.short_code, day ORDER BY m.short_code, day
            """, connection);
        command.Parameters.AddWithValue("workspace", workspaceId);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var code = reader.GetString(0);
            if (!byCode.TryGetValue(code, out var points)) byCode[code] = points = new();
            if (!reader.IsDBNull(1)) points[reader.GetFieldValue<DateTimeOffset>(1)] = reader.GetInt64(2);
        }
        return byCode.Select(x => new LinkSparkline(x.Key, Fill(x.Value, start, end, TimeSpan.FromDays(1)))).ToArray();
    }

    private static void AddScope(NpgsqlCommand command, Guid workspaceId, string mappingId)
    {
        command.Parameters.AddWithValue("workspace", workspaceId);
        command.Parameters.AddWithValue("mapping", mappingId);
    }

    private static void AddRange(NpgsqlCommand command, DateTimeOffset start, DateTimeOffset end, bool dates)
    {
        if (dates)
        {
            command.Parameters.AddWithValue("start", DateOnly.FromDateTime(start.UtcDateTime));
            command.Parameters.AddWithValue("end", DateOnly.FromDateTime(end.UtcDateTime));
        }
        else
        {
            command.Parameters.AddWithValue("start", start);
            command.Parameters.AddWithValue("end", end);
        }
    }

    private static IReadOnlyList<InsightPoint> Fill(Dictionary<DateTimeOffset, long> values, DateTimeOffset start,
        DateTimeOffset end, TimeSpan step)
    {
        var result = new List<InsightPoint>();
        for (var bucket = start; bucket < end; bucket += step)
            result.Add(new(bucket, values.GetValueOrDefault(bucket)));
        return result;
    }

    private static IReadOnlyList<InsightGroup> TopReferrers(Dictionary<string, long> values)
    {
        var sorted = values.Where(x => x.Key != "Other").OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();
        var top = sorted.Take(5).Select(x => new InsightGroup(x.Key, x.Value)).ToList();
        var other = sorted.Skip(5).Sum(x => x.Value) + values.GetValueOrDefault("Other");
        if (other > 0) top.Add(new("Other", other));
        return top;
    }
}
