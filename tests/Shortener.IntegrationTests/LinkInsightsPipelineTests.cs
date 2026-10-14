using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using Shortener.Analytics;
using Shortener.Application;
using Shortener.Domain;
using Shortener.Infrastructure;
using Xunit.Abstractions;

namespace Shortener.IntegrationTests;

public sealed class LinkInsightsPipelineTests(ITestOutputHelper output)
{
    [Fact]
    public async Task IndexedReadsOnOneThousandLinksAndOneHundredThousandClicks()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Insights performance tests require an isolated guarded test database.");
        var schema = "schwab_insights_scale_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            target.SearchPath = $"{schema},public";
            var services = new ServiceCollection();
            services.AddDbContextFactory<ShortenerDbContext>(options => options.UseNpgsql(target.ConnectionString,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)));
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<ShortenerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = Guid.NewGuid();
            var workspace = Guid.NewGuid();
            db.ApplicationUsers.Add(new ApplicationUser { Id = user, CreatedAtUtc = now });
            db.Workspaces.Add(new Workspace { Id = workspace, Kind = WorkspaceKind.Personal, PersonalOwnerUserId = user,
                Name = "Scale", CreatedAtUtc = now });
            var mappings = Enumerable.Range(0, 1000).Select(i => UrlMapping.CreateWorkspaceManaged(
                "https://example.test/destination", $"scale{i:0000}", workspace, now)).ToArray();
            db.UrlMappings.AddRange(mappings);
            await db.SaveChangesAsync();
            await using var dataSource = new NpgsqlDataSourceBuilder(target.ConnectionString).Build();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO link_click_facts (event_id, mapping_id, owner_workspace_id, short_code,
                    occurred_at_utc, referrer_group, device_class, visitor_hash, is_bot, event_version)
                SELECT md5(m.id || ':' || n::text)::uuid, m.id, @workspace, m.short_code,
                    @time - (n % 24) * interval '1 hour', 'Direct', 'Desktop',
                    substr(md5(m.id || ':' || n::text), 1, 32), false, 3
                FROM url_mappings m CROSS JOIN generate_series(1, 100) n
                WHERE m.owner_workspace_id = @workspace
                """, connection))
            {
                seed.Parameters.AddWithValue("workspace", workspace);
                seed.Parameters.AddWithValue("time", now);
                Assert.Equal(100000, await seed.ExecuteNonQueryAsync());
            }
            await new AggregateRebuildService(dataSource).RebuildAsync();
            await using (var analyze = new NpgsqlCommand("ANALYZE url_mappings; ANALYZE link_click_facts; ANALYZE link_click_hourly; ANALYZE link_click_dimension_daily; ANALYZE link_visitor_daily", connection))
                await analyze.ExecuteNonQueryAsync();
            var reader = new LinkInsightsReader(dataSource, TimeProvider.System);
            var watch = Stopwatch.StartNew();
            var insights = await reader.GetAsync(workspace, mappings[0].Id, "7d");
            watch.Stop();
            output.WriteLine($"1,000 links / 100,000 clicks: link insights {watch.ElapsedMilliseconds} ms");
            Assert.Equal(100, insights.TotalClicks);
            watch.Restart();
            var sparklines = await reader.GetSparklinesAsync(workspace, 7);
            watch.Stop();
            output.WriteLine($"1,000 links / 100,000 clicks: batched sparklines {watch.ElapsedMilliseconds} ms");
            Assert.Equal(1000, sparklines.Count);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task CollectionMigrationUpgradesCurrentMainSchema()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Insights tests require an isolated guarded test database.");
        var schema = "schwab_insights_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            target.SearchPath = $"{schema},public";
            var services = new ServiceCollection();
            services.AddDbContextFactory<ShortenerDbContext>(options => options.UseNpgsql(target.ConnectionString,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)));
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<ShortenerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.GetService<IMigrator>().MigrateAsync("20261013133000_WorkspaceManagedEvents");
            Assert.DoesNotContain(await db.Database.GetAppliedMigrationsAsync(), x => x.Contains("LinkInsightsCollection"));
            await db.Database.MigrateAsync();
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), x => x.Contains("LinkInsightsCollection"));
            await using var connection = new NpgsqlConnection(target.ConnectionString);
            await connection.OpenAsync();
            await using var check = new NpgsqlCommand("SELECT to_regclass('link_click_facts') IS NOT NULL", connection);
            Assert.True((bool)(await check.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task FreshMigrationV3ReplayBotV2ReaderRebuildAndRetention()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Insights tests require an isolated guarded test database.");
        var schema = "schwab_insights_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            target.SearchPath = $"{schema},public";
            var services = new ServiceCollection();
            services.AddDbContextFactory<ShortenerDbContext>(options => options.UseNpgsql(target.ConnectionString,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)));
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<ShortenerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            var user = Guid.NewGuid();
            var workspace = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var mapping = UrlMapping.CreateWorkspaceManaged("https://example.test/destination", "insight123", workspace, now);
            db.ApplicationUsers.Add(new ApplicationUser { Id = user, CreatedAtUtc = now });
            db.Workspaces.Add(new Workspace { Id = workspace, Kind = WorkspaceKind.Personal, PersonalOwnerUserId = user,
                Name = "Personal", CreatedAtUtc = now });
            db.UrlMappings.Add(mapping);
            await db.SaveChangesAsync();
            await using var dataSource = new NpgsqlDataSourceBuilder(target.ConnectionString).Build();
            var store = new PostgresClickEventStore(dataSource);
            var rawIp = "192.0.2.191";
            var rawAgent = "ExampleBrowser/12.3";
            var rawReferrer = "https://linkedin.com/private/path?token=example";
            var hashed = VisitorHashService.FromConfiguredSecret("integration-test-visitor-secret-32-bytes")
                .Compute(rawIp, rawAgent, mapping.Id, DateOnly.FromDateTime(now.UtcDateTime));
            var referrer = ReferrerClassifier.Classify(rawReferrer);
            var signal = new Shortener.Application.Interfaces.ManagedClickSignal(hashed, referrer.ReferrerGroup,
                referrer.ReferrerHost, "Mobile", "iOS", "Safari", "en-US", false);
            var repository = new PostgresUrlShortenerRepository(factory, NullLogger<PostgresUrlShortenerRepository>.Instance);
            await repository.CaptureWorkspaceClickAsync(mapping.Id, workspace, now, null, signal);
            await using (var outboxConnection = await dataSource.OpenConnectionAsync())
            await using (var outbox = new NpgsqlCommand("SELECT row_to_json(o)::text FROM click_outbox o WHERE mapping_id = @mapping", outboxConnection))
            {
                outbox.Parameters.AddWithValue("mapping", mapping.Id);
                var persistedOutbox = (string)(await outbox.ExecuteScalarAsync())!;
                Assert.DoesNotContain(rawIp, persistedOutbox);
                Assert.DoesNotContain(rawAgent, persistedOutbox);
                Assert.DoesNotContain(rawReferrer, persistedOutbox);
            }
            var click = new ManagedClickEventV3(Guid.NewGuid(), workspace, mapping.Id, mapping.ShortCode,
                now.AddMinutes(-5), null, hashed, "LinkedIn", "linkedin.com", "Mobile", "iOS", "Safari", "en-US", false);
            var serializedEvent = JsonSerializer.Serialize(click);
            Assert.DoesNotContain(rawIp, serializedEvent);
            Assert.DoesNotContain(rawAgent, serializedEvent);
            Assert.DoesNotContain(rawReferrer, serializedEvent);
            Assert.True(await store.ApplyAsync(click));
            Assert.False(await store.ApplyAsync(click));
            var bot = click with { EventId = Guid.NewGuid(), IsBot = true, DeviceClass = "Bot", OccurredAtUtc = now.AddMinutes(-4) };
            Assert.True(await store.ApplyAsync(bot));
            Assert.True(await store.ApplyAsync(new ManagedClickEventV2(Guid.NewGuid(), workspace, mapping.Id,
                mapping.ShortCode, now.AddMinutes(-3), null)));
            var read = new LinkInsightsReader(dataSource, TimeProvider.System);
            var insights = await read.GetAsync(workspace, mapping.Id, "24h");
            Assert.Equal(2, insights.TotalClicks);
            Assert.Equal(0, insights.PreviousPeriodTotal);
            Assert.Equal(1, insights.UniqueVisitors);
            Assert.Equal(24, insights.TimeSeries.Count);
            Assert.Equal(2, insights.TimeSeries.Sum(x => x.Clicks));
            Assert.Contains(insights.TopReferrers, x => x.Name == "LinkedIn" && x.Clicks == 1);
            Assert.Contains(insights.DeviceShares, x => x.Name == "Mobile" && x.Clicks == 1);
            Assert.Equal(2, (await read.GetSparklinesAsync(workspace, 7)).Single().Days.Sum(x => x.Clicks));
            var (hourly, dimension, visitor) = await new AggregateRebuildService(dataSource).RebuildAsync();
            Assert.True(hourly > 0 && dimension > 0 && visitor > 0);
            Assert.Equal(2, (await read.GetAsync(workspace, mapping.Id, "7d")).TotalClicks);
            await using var connection = await dataSource.OpenConnectionAsync();
            await using (var inspect = new NpgsqlCommand("""
                SELECT count(*), string_agg(row_to_json(f)::text, ' ') FROM link_click_facts f
                WHERE mapping_id = @mapping
                """, connection))
            {
                inspect.Parameters.AddWithValue("mapping", mapping.Id);
                await using var rows = await inspect.ExecuteReaderAsync();
                Assert.True(await rows.ReadAsync());
                Assert.Equal(3, rows.GetInt64(0));
                var persisted = rows.GetString(1);
                Assert.DoesNotContain(rawIp, persisted);
                Assert.DoesNotContain(rawAgent, persisted);
                Assert.DoesNotContain(rawReferrer, persisted);
            }
            var old = click with { EventId = Guid.NewGuid(), OccurredAtUtc = now.AddDays(-40),
                VisitorHash = VisitorHashService.FromConfiguredSecret("integration-test-visitor-secret-32-bytes")
                    .Compute("192.0.2.191", "ExampleBrowser/12.3", mapping.Id, DateOnly.FromDateTime(now.AddDays(-40).UtcDateTime)) };
            Assert.True(await store.ApplyAsync(old));
            await new VisitorRetentionWorker(dataSource, NullLogger<VisitorRetentionWorker>.Instance).SweepOnceAsync(default);
            await using var check = new NpgsqlCommand("SELECT visitor_hash FROM link_click_facts WHERE event_id = @event", connection);
            check.Parameters.AddWithValue("event", old.EventId);
            Assert.Equal(DBNull.Value, await check.ExecuteScalarAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("24h", 24)]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    public void RangeValidationAndZeroFilledWindow(string range, int expectedBuckets)
    {
        Assert.True(LinkInsightsReader.TryRange(range, new DateTimeOffset(2026, 10, 14, 9, 37, 0, TimeSpan.Zero),
            out var start, out var end, out var step));
        Assert.Equal(expectedBuckets, (end - start) / step);
        Assert.False(LinkInsightsReader.TryRange("90d", DateTimeOffset.UtcNow, out _, out _, out _));
    }
}
