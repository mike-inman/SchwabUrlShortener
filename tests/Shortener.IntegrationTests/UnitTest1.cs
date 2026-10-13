using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Shortener.Application.Interfaces;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Shortener.IntegrationTests;

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
    public async Task ManagedOperationsAreTenantScopedAndCodesGloballyUnique()
    {
        using var factory = new TestFactory();
        using var a = ClientFor(factory, TenantA);
        using var b = ClientFor(factory, TenantB);
        using var anon = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com" })).StatusCode);
        var created = await a.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manag123", tenantId = TenantB });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/urls/managed/manag123")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/urls/managed/manag123/analytics")).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<ManagedUrlResponse[]>("/api/urls/managed/analytics"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "other.example", shortCode = "manag123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await anon.GetAsync("/r/manag123")).StatusCode);
        var analytics = await a.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/manag123/analytics");
        Assert.Equal(1, analytics!.HitCount);
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
    public async Task ConcurrentManagedRedirectsAtomicallyUpdateAnalytics()
    {
        using var factory = new TestFactory();
        using var owner = ClientFor(factory, TenantA);
        using var publicClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manyhits" })).StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => publicClient.GetAsync("/r/manyhits")));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Redirect, x.StatusCode));
        var analytics = await owner.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/manyhits/analytics");
        Assert.Equal(20, analytics!.HitCount);
        Assert.NotNull(analytics.LastAccessedAtUtc);
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
        public string ConnectionString { get; }
        public AdjustableClock Clock { get; } = new();
        public TestFactory(bool resetDatabase = true, bool unavailableDatabase = false)
        {
            ConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
                ?? throw new InvalidOperationException("Real PostgreSQL test connection must be provided in ConnectionStrings__ShortenerDb.");
            var databaseName = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
            if (databaseName is null || !databaseName.StartsWith("schwab_slice2_test_", StringComparison.Ordinal))
                throw new InvalidOperationException("Integration tests require a disposable database named schwab_slice2_test_*.");
            _unavailableDatabase = unavailableDatabase;
            if (!resetDatabase) return;
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand("TRUNCATE TABLE url_mappings", connection);
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
                ["ConnectionStrings:ShortenerDb"] = apiConnectionString
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
