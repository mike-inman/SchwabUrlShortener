using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shortener.Analytics;
using Shortener.Application;
using Shortener.Application.Interfaces;
using Shortener.Infrastructure;
using StackExchange.Redis;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Shortener.IntegrationTests;

public class RedisConnectionLifecycleTests
{
    [Fact]
    public async Task TimedOutConnectionAttemptIsSharedAndFailedAttemptCanRetry()
    {
        var pending = new TaskCompletionSource<ConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var cache = new RedisRedirectCache("127.0.0.1:1", null, _ =>
        {
            attempts++;
            return attempts == 1 ? pending.Task : Task.FromException<ConnectionMultiplexer>(new InvalidOperationException("Connect failed"));
        });

        Assert.False(await cache.IsAvailableAsync());
        Assert.False(await cache.IsAvailableAsync());
        Assert.Equal(1, attempts);

        pending.SetException(new InvalidOperationException("Connect failed"));
        Assert.False(await cache.IsAvailableAsync());
        Assert.False(await cache.IsAvailableAsync());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ConnectionCompletingAfterCacheShutdownIsDisposed()
    {
        var redisConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis")
            ?? throw new InvalidOperationException("Real Redis test connection must be provided in ConnectionStrings__Redis.");
        await using var connection = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
        await connection.GetDatabase().PingAsync();
        var pending = new TaskCompletionSource<ConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new RedisRedirectCache(redisConnectionString, null, _ => pending.Task);
        var disposed = false;
        try
        {
            Assert.False(await cache.IsAvailableAsync());
            await cache.DisposeAsync();
            disposed = true;
            pending.SetResult(connection);
            Assert.True(SpinWait.SpinUntil(() => !connection.IsConnected, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            if (!disposed) await cache.DisposeAsync();
        }
    }
}

public class ShortenerApiTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GuestIsAnonymousRedirectOnlyAndExpiresAtSevenDays()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var create = await client.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.com", shortCode = "guest123" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await create.Content.ReadFromJsonAsync<CreateUrlResponse>();
        Assert.NotNull(body);
        Assert.Equal(factory.Clock.GetUtcNow().AddDays(7), body.ExpiresAtUtc);
        Assert.Equal("/r/guest123", body.ShortUrl);
        var redirect = await client.GetAsync(body.ShortUrl);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com", redirect.Headers.Location?.ToString().TrimEnd('/'));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/urls/managed/guest123/analytics")).StatusCode);
        using var managedClient = ClientFor(factory, TenantA);
        Assert.Equal(HttpStatusCode.NotFound, (await managedClient.GetAsync("/api/urls/managed/guest123")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromTicks(-1)));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(body.ShortUrl)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(body.ShortUrl)).StatusCode);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT mode, tenant_id IS NULL, hit_count, last_accessed_at_utc IS NULL, expires_at_utc = created_at_utc + INTERVAL '168 hours' FROM url_mappings WHERE short_code = 'guest123'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt16(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
    }

    [Fact]
    public async Task RealRedisHitMissExpiryAndPostgresFallback()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.RedisConnectionString);
        var db = redis.GetDatabase();
        Assert.Equal(RedisRedirectCache.KeyFor("MiXeD123", factory.RedisNamespace),
            RedisRedirectCache.KeyFor("mixed123", factory.RedisNamespace));
        var key = RedisRedirectCache.KeyFor("mixed123", factory.RedisNamespace);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.com", shortCode = "MiXeD123" })).StatusCode);
        Assert.True(await db.KeyExistsAsync(key));
        var ttl = await db.KeyTimeToLiveAsync(key);
        Assert.True(ttl > TimeSpan.Zero && ttl <= TimeSpan.FromHours(1));

        // With the mapping database unavailable, a cached guest redirect proves the hot read path.
        using (var cacheOnly = new TestFactory(resetDatabase: false, unavailableDatabase: true, redisNamespace: factory.RedisNamespace))
        using (var cacheOnlyClient = cacheOnly.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }))
            Assert.Equal(HttpStatusCode.Redirect, (await cacheOnlyClient.GetAsync("/r/mIxEd123")).StatusCode);

        Assert.True(await db.KeyDeleteAsync(key));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/r/mixed123")).StatusCode);
        Assert.True(await db.KeyExistsAsync(key)); // PostgreSQL miss fallback repopulated Redis.

        using (var owner = ClientFor(factory, TenantA))
            Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.org", shortCode = "cacheman" })).StatusCode);
        Assert.True(await db.KeyExistsAsync(RedisRedirectCache.KeyFor("cacheman", factory.RedisNamespace)));
        using (var cacheOnly = new TestFactory(resetDatabase: false, unavailableDatabase: true, redisNamespace: factory.RedisNamespace))
        using (var cacheOnlyClient = cacheOnly.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }))
            Assert.Equal(HttpStatusCode.InternalServerError, (await cacheOnlyClient.GetAsync("/r/cacheman")).StatusCode);

        factory.Clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/r/MIXED123")).StatusCode);
    }

    [Fact]
    public async Task RedisOutageMakesReadinessUnhealthyButPostgresRedirectWorks()
    {
        using var factory = new TestFactory(unavailableRedis: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.com", shortCode = "noRedis1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/r/noRedis1")).StatusCode);
    }

    [Fact]
    public async Task ManagedOperationsAreTenantScopedAndCodesGloballyUnique()
    {
        using var factory = new TestFactory();
        using var a = ClientFor(factory, TenantA);
        using var b = ClientFor(factory, TenantB);
        using var anon = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com" })).StatusCode);
        var created = await a.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manag123", tenantId = TenantB });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var repeatedCode = await a.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manag123" });
        Assert.Equal(HttpStatusCode.OK, repeatedCode.StatusCode);
        Assert.Equal("manag123", (await repeatedCode.Content.ReadFromJsonAsync<CreateUrlResponse>())!.ShortCode);
        var generatedOne = await a.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com" });
        var generatedTwo = await a.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com" });
        Assert.Equal(HttpStatusCode.Created, generatedOne.StatusCode);
        Assert.Equal(HttpStatusCode.Created, generatedTwo.StatusCode);
        Assert.NotEqual((await generatedOne.Content.ReadFromJsonAsync<CreateUrlResponse>())!.ShortCode,
            (await generatedTwo.Content.ReadFromJsonAsync<CreateUrlResponse>())!.ShortCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/urls/managed/manag123")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/urls/managed/manag123/analytics")).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<ManagedUrlResponse[]>("/api/urls/managed/analytics"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "other.example", shortCode = "manag123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await anon.GetAsync("/r/manag123")).StatusCode);
        var analytics = await a.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/manag123/analytics");
        Assert.Equal(0, analytics!.HitCount);
        await using (var captured = new NpgsqlConnection(factory.ConnectionString))
        {
            await captured.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM click_outbox WHERE short_code = 'manag123' AND tenant_id = @tenant", captured);
            command.Parameters.AddWithValue("tenant", TenantA);
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT tenant_id FROM url_mappings WHERE short_code = 'manag123'", connection);
            Assert.Equal(TenantA, (Guid)(await command.ExecuteScalarAsync())!);
        }

        var otherTenantCreate = await b.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manag456" });
        Assert.Equal(HttpStatusCode.Created, otherTenantCreate.StatusCode);
        Assert.Equal("manag456", (await otherTenantCreate.Content.ReadFromJsonAsync<CreateUrlResponse>())!.ShortCode);
    }

    [Fact]
    public async Task InvalidTenantContextIsForbidden()
    {
        using var factory = new TestFactory();
        using var client = ClientFor(factory, Guid.Empty);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/urls/managed/analytics")).StatusCode);
    }

    [Fact]
    public async Task ManagedEndpointsRequireDelegatedScope()
    {
        using var factory = new TestFactory(resetDatabase: false);
        using var missingScope = ClientFor(factory, TenantA, scope: null);
        using var wrongScope = ClientFor(factory, TenantA, scope: "other_scope");
        Assert.Equal(HttpStatusCode.Forbidden, (await missingScope.GetAsync("/api/urls/managed/analytics")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongScope.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com" })).StatusCode);
    }

    [Fact]
    public async Task LinksSurviveApiHostRestart()
    {
        using (var first = new TestFactory())
        using (var guest = first.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }))
        using (var managed = ClientFor(first, TenantA))
        {
            Assert.Equal(HttpStatusCode.Created, (await guest.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.com", shortCode = "keepguest" })).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await managed.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "other.example", shortCode = "keepmanage" })).StatusCode);
        }
        using var second = new TestFactory(resetDatabase: false);
        using var publicClient = second.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var owner = ClientFor(second, TenantA);
        Assert.Equal(HttpStatusCode.Redirect, (await publicClient.GetAsync("/r/keepguest")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/urls/managed/keepmanage")).StatusCode);
    }

    [Fact]
    public async Task CrossModeCodeRaceHasExactlyOneWinner()
    {
        using var factory = new TestFactory();
        using var guest = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var managed = ClientFor(factory, TenantA);
        var attempts = await Task.WhenAll(
            guest.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.com", shortCode = "race123" }),
            managed.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "other.example", shortCode = "RACE123" }));
        Assert.Contains(attempts, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Contains(attempts, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM url_mappings WHERE short_code = 'race123'", connection);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ConcurrentManagedRedirectsCaptureDistinctDurableEvents()
    {
        using var factory = new TestFactory();
        using var owner = ClientFor(factory, TenantA);
        using var publicClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manyhits" })).StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => publicClient.GetAsync("/r/manyhits")));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Redirect, x.StatusCode));
        var analytics = await owner.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/manyhits/analytics");
        Assert.Equal(0, analytics!.HitCount);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*), count(DISTINCT event_id) FROM click_outbox WHERE short_code = 'manyhits' AND tenant_id = @tenant", connection);
        command.Parameters.AddWithValue("tenant", TenantA);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(20L, reader.GetInt64(0));
        Assert.Equal(20L, reader.GetInt64(1));
    }

    [Fact]
    public async Task RealKafkaPipelineReplaysWithoutDoubleCountAndRejectsWrongTenant()
    {
        var broker = Environment.GetEnvironmentVariable("Kafka__BootstrapServers")
            ?? throw new InvalidOperationException("Real Kafka broker must be provided in Kafka__BootstrapServers.");
        using var factory = new TestFactory();
        using var owner = ClientFor(factory, TenantA);
        using var publicClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "pipeline1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await publicClient.GetAsync("/r/pipeline1")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await publicClient.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.org", shortCode = "pipeline2" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await publicClient.GetAsync("/r/pipeline2")).StatusCode);

        var topic = "schwab-click-test-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = broker }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        var options = new AnalyticsOptions(broker, topic, "schwab-test-" + Guid.NewGuid().ToString("N"));
        await using var dataSource = new NpgsqlDataSourceBuilder(factory.ConnectionString).Build();
        using (var unavailablePublisher = new ClickOutboxPublisher(dataSource,
            options with { BootstrapServers = "127.0.0.1:1" }, NullLogger<ClickOutboxPublisher>.Instance))
            Assert.Equal(PublishOutcome.Retried, await unavailablePublisher.PublishOneAsync());
        using var publisher = new ClickOutboxPublisher(dataSource, options, NullLogger<ClickOutboxPublisher>.Instance);
        await Task.Delay(TimeSpan.FromSeconds(2));
        var store = new PostgresClickEventStore(dataSource);
        var publish = await publisher.PublishOneAsync();
        for (var retry = 0; publish == PublishOutcome.Retried && retry < 4; retry++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1 << retry));
            publish = await publisher.PublishOneAsync();
        }
        if (publish != PublishOutcome.Published)
        {
            await using var diagnostic = new NpgsqlConnection(factory.ConnectionString);
            await diagnostic.OpenAsync();
            await using var failure = new NpgsqlCommand("SELECT last_error_code FROM click_outbox LIMIT 1", diagnostic);
            Assert.Fail($"Publish outcome {publish}; error type {await failure.ExecuteScalarAsync()}");
        }
        Assert.Equal(PublishOutcome.NoWork, await publisher.PublishOneAsync());
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var row = new NpgsqlCommand("SELECT event_id, tenant_id, mapping_id, short_code, occurred_at_utc, published_at_utc IS NOT NULL FROM click_outbox", connection);
        await using var reader = await row.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var click = new ManagedClickEventV1(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), null);
        Assert.True(reader.GetBoolean(5));
        Assert.Equal(TenantA, click.TenantId);
        await reader.DisposeAsync();
        Assert.Equal(0, (await owner.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/pipeline1/analytics"))!.HitCount);

        using (var consumer = new ClickKafkaConsumer(options, store, NullLogger<ClickKafkaConsumer>.Instance))
            Assert.Equal(ConsumeOutcome.Applied, await ConsumeUntilAsync(consumer, ConsumeOutcome.Applied));
        Assert.Equal(1, (await owner.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/pipeline1/analytics"))!.HitCount);

        // Replaying the committed event with a new group and publishing it again both exercise event-ID deduplication.
        using (var replay = new ClickKafkaConsumer(options with { ConsumerGroup = "schwab-replay-" + Guid.NewGuid().ToString("N") }, store, NullLogger<ClickKafkaConsumer>.Instance))
            Assert.Equal(ConsumeOutcome.Duplicate, await ConsumeUntilAsync(replay, ConsumeOutcome.Duplicate));
        using (var producer = new ProducerBuilder<string, string>(options.ProducerConfig()).Build())
        {
            await producer.ProduceAsync(topic, new Message<string, string> { Key = click.MappingId, Value = JsonSerializer.Serialize(click with { TenantId = TenantB, EventId = Guid.NewGuid() }) });
        }
        using (var poison = new ClickKafkaConsumer(options with { ConsumerGroup = "schwab-poison-" + Guid.NewGuid().ToString("N") }, store, NullLogger<ClickKafkaConsumer>.Instance))
        {
            Assert.Equal(ConsumeOutcome.Duplicate, await ConsumeUntilAsync(poison, ConsumeOutcome.Duplicate));
            Assert.Equal(ConsumeOutcome.DeadLettered, await ConsumeUntilAsync(poison, ConsumeOutcome.DeadLettered));
        }
        Assert.Equal(1, (await owner.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/pipeline1/analytics"))!.HitCount);
        await using var count = new NpgsqlCommand("SELECT (SELECT count(*) FROM processed_click_events), (SELECT count(*) FROM click_dead_letters), (SELECT count(*) FROM click_outbox WHERE short_code = 'pipeline2')", connection);
        await using var counts = await count.ExecuteReaderAsync();
        Assert.True(await counts.ReadAsync());
        Assert.Equal(1L, counts.GetInt64(0));
        Assert.Equal(1L, counts.GetInt64(1));
        Assert.Equal(0L, counts.GetInt64(2));
    }

    [Fact]
    public async Task FailedPoisonDeadLetterWriteCannotSkipToLaterKafkaMessage()
    {
        var broker = Environment.GetEnvironmentVariable("Kafka__BootstrapServers")
            ?? throw new InvalidOperationException("Real Kafka broker must be provided in Kafka__BootstrapServers.");
        using var factory = new TestFactory();
        using var owner = ClientFor(factory, TenantA);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/urls/managed",
            new { originalUrl = "example.com", shortCode = "poisonnext" })).StatusCode);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var mapping = new NpgsqlCommand("SELECT id FROM url_mappings WHERE short_code = 'poisonnext'", connection);
        var mappingId = Assert.IsType<string>(await mapping.ExecuteScalarAsync());
        var validClick = new ManagedClickEventV1(Guid.NewGuid(), TenantA, mappingId, "poisonnext", DateTimeOffset.UtcNow, null);
        var topic = "schwab-poison-retry-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = broker }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        var options = new AnalyticsOptions(broker, topic, "schwab-poison-retry-" + Guid.NewGuid().ToString("N"));
        using (var producer = new ProducerBuilder<string, string>(options.ProducerConfig()).Build())
        {
            await producer.ProduceAsync(topic, new Message<string, string> { Key = mappingId, Value = "{invalid-json" });
            await producer.ProduceAsync(topic, new Message<string, string> { Key = mappingId, Value = JsonSerializer.Serialize(validClick) });
        }

        var timedConnection = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { CommandTimeout = 1 };
        await using var dataSource = new NpgsqlDataSourceBuilder(timedConnection.ConnectionString).Build();
        using var consumer = new ClickKafkaConsumer(options, new PostgresClickEventStore(dataSource), NullLogger<ClickKafkaConsumer>.Instance);
        await using var lockConnection = new NpgsqlConnection(factory.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var holdDeadLetters = new NpgsqlCommand("LOCK TABLE click_dead_letters IN ACCESS EXCLUSIVE MODE", lockConnection, lockTransaction))
            await holdDeadLetters.ExecuteNonQueryAsync();
        try
        {
            Assert.Equal(ConsumeOutcome.Retry, await ConsumeUntilAsync(consumer, ConsumeOutcome.Retry));
            Assert.Equal(ConsumeOutcome.Retry, await ConsumeUntilAsync(consumer, ConsumeOutcome.Retry));
            await using var before = new NpgsqlCommand("SELECT hit_count FROM url_mappings WHERE id = @id", connection);
            before.Parameters.AddWithValue("id", mappingId);
            Assert.Equal(0, await before.ExecuteScalarAsync());
        }
        finally
        {
            await lockTransaction.RollbackAsync();
        }

        Assert.Equal(ConsumeOutcome.DeadLettered, await ConsumeUntilAsync(consumer, ConsumeOutcome.DeadLettered));
        Assert.Equal(ConsumeOutcome.Applied, await ConsumeUntilAsync(consumer, ConsumeOutcome.Applied));
        await using var verify = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM click_dead_letters WHERE topic = @topic AND offset_id = 0 AND reason_code = 'invalid_json'),
                   (SELECT hit_count FROM url_mappings WHERE id = @id),
                   (SELECT count(*) FROM processed_click_events WHERE event_id = @event_id)
            """, connection);
        verify.Parameters.AddWithValue("topic", topic);
        verify.Parameters.AddWithValue("id", mappingId);
        verify.Parameters.AddWithValue("event_id", validClick.EventId);
        await using var result = await verify.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(1L, result.GetInt64(0));
        Assert.Equal(1, result.GetInt32(1));
        Assert.Equal(1L, result.GetInt64(2));
    }

    private static async Task<ConsumeOutcome> ConsumeUntilAsync(ClickKafkaConsumer consumer, ConsumeOutcome expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            var outcome = await consumer.ConsumeOneAsync(TimeSpan.FromMilliseconds(500), timeout.Token);
            if (outcome != ConsumeOutcome.NoWork) return outcome;
        }
        throw new TimeoutException($"Expected {expected} from Kafka consumer.");
    }

    [Fact]
    public async Task RepositoryHonorsCancellationAndReadinessReportsDatabaseFailure()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var repository = factory.Services.GetRequiredService<IUrlShortenerRepository>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetByShortCodeAsync("missing", canceled.Token));
        using var unavailable = new TestFactory(unavailableDatabase: true);
        using var unavailableClient = unavailable.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await unavailableClient.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await unavailableClient.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await unavailableClient.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.com", shortCode = "nodb123" })).StatusCode);
    }

    [Fact]
    public async Task DatabaseRejectsInvalidGuestExpiry()
    {
        using var factory = new TestFactory();
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO url_mappings (id, short_code, original_url, mode, tenant_id, created_at_utc, expires_at_utc, hit_count) VALUES (@id, @code, @url, 0, NULL, @created, @expires, 0)", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("code", "invalidguest");
        command.Parameters.AddWithValue("url", "https://example.com");
        var created = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        command.Parameters.AddWithValue("created", created);
        command.Parameters.AddWithValue("expires", created.AddDays(1));
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_url_mappings_link_mode", error.ConstraintName);
        command.Parameters["expires"].Value = DBNull.Value;
        var missingExpiry = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, missingExpiry.SqlState);
        Assert.Equal("ck_url_mappings_link_mode", missingExpiry.ConstraintName);
    }

    private static HttpClient ClientFor(TestFactory factory, Guid tenant, string? scope = "access_as_user")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant.ToString());
        if (scope is not null) client.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        return client;
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly bool _unavailableDatabase;
        private readonly bool _unavailableRedis;
        public string ConnectionString { get; }
        public string RedisConnectionString { get; }
        public string RedisNamespace { get; }
        public AdjustableClock Clock { get; } = new();
        public TestFactory(bool resetDatabase = true, bool unavailableDatabase = false,
            bool unavailableRedis = false, string? redisNamespace = null)
        {
            ConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
                ?? throw new InvalidOperationException("Real PostgreSQL test connection must be provided in ConnectionStrings__ShortenerDb.");
            var databaseName = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
            if (databaseName != "Schwab_test_local")
                throw new InvalidOperationException("Integration tests require the disposable database Schwab_test_local.");
            RedisConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis")
                ?? throw new InvalidOperationException("Real Redis test connection must be provided in ConnectionStrings__Redis.");
            RedisNamespace = redisNamespace ?? "shortener:redirect:test:" + Guid.NewGuid().ToString("N");
            _unavailableDatabase = unavailableDatabase;
            _unavailableRedis = unavailableRedis;
            if (!resetDatabase) return;
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand("TRUNCATE TABLE click_dead_letters, processed_click_events, click_outbox, url_mappings", connection);
            command.ExecuteNonQuery();
        }
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            var apiConnectionString = ConnectionString;
            if (_unavailableDatabase)
            {
                var failed = new NpgsqlConnectionStringBuilder(ConnectionString) { Port = 1, Timeout = 1, CommandTimeout = 1 };
                apiConnectionString = failed.ConnectionString;
            }
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                ["ConnectionStrings:ShortenerDb"] = apiConnectionString,
                ["ConnectionStrings:Redis"] = _unavailableRedis ? "127.0.0.1:1,abortConnect=false" : RedisConnectionString,
                ["RedirectCache:Namespace"] = RedisNamespace
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                });
            });
        }
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    // Test-only authentication replaces bearer validation inside WebApplicationFactory.
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Tenant", out var value))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new("tid", value.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Scope", out var scope)) claims.Add(new Claim("scp", scope.ToString()));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
