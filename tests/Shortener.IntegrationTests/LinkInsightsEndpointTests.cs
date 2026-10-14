using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Shortener.Analytics;
using Shortener.Application;
using Shortener.Domain;
using Shortener.Infrastructure;

namespace Shortener.IntegrationTests;

public sealed class LinkInsightsEndpointTests
{
    private const string Tenant = "6adc36e9-dfe5-469f-8aed-6ef83abcdd19";
    private const string Audience = "8911f6b3-365c-4ce5-a0f7-d12b2e4f230a";
    private const string Issuer = "https://" + Tenant + ".ciamlogin.com/" + Tenant + "/v2.0";

    [Fact]
    public async Task InsightsAndSparklinesEnforceMembershipAndValidateRanges()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Endpoint tests require an isolated guarded test database.");
        var schema = "schwab_insights_api_" + Guid.NewGuid().ToString("N");
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
            var owner = Guid.NewGuid();
            var outsider = Guid.NewGuid();
            var workspace = Guid.NewGuid();
            db.ApplicationUsers.AddRange(new ApplicationUser { Id = owner, CreatedAtUtc = now },
                new ApplicationUser { Id = outsider, CreatedAtUtc = now });
            db.ExternalIdentities.AddRange(
                new ExternalIdentity { Id = Guid.NewGuid(), UserId = owner, Issuer = Issuer, Subject = "owner", CreatedAtUtc = now },
                new ExternalIdentity { Id = Guid.NewGuid(), UserId = outsider, Issuer = Issuer, Subject = "outsider", CreatedAtUtc = now });
            db.Workspaces.Add(new Workspace { Id = workspace, Kind = WorkspaceKind.Personal, PersonalOwnerUserId = owner,
                Name = "Personal", CreatedAtUtc = now });
            db.UrlMappings.AddRange(UrlMapping.CreateWorkspaceManaged("https://example.test/one", "insight123", workspace, now),
                UrlMapping.CreateWorkspaceManaged("https://example.test/two", "insight456", workspace, now));
            await db.SaveChangesAsync();

            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var key = new RsaSecurityKey(rsa) { KeyId = "insights-test-key" };
            using var api = new CustomerFactory(target.ConnectionString, key);
            using var client = api.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            async Task<HttpResponseMessage> SendAsync(string subject, HttpMethod method, string path,
                HttpContent? content = null, string? userAgent = null, string? referrer = null)
            {
                var claims = new[] { new Claim("sub", subject), new Claim("ver", "2.0"), new Claim("scp", "access_as_user") };
                var token = new JwtSecurityToken(Issuer, Audience, claims, DateTime.UtcNow.AddMinutes(-1),
                    DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
                using var request = new HttpRequestMessage(method, path) { Content = content };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
                if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                if (referrer is not null) request.Headers.Referrer = new Uri(referrer);
                return await client.SendAsync(request);
            }
            Task<HttpResponseMessage> GetAsync(string subject, string path) => SendAsync(subject, HttpMethod.Get, path);
            var route = $"/api/workspaces/{workspace}/urls/managed";
            using (var denied = await GetAsync("outsider", route + "/insight123/insights?range=7d"))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using (var denied = await GetAsync("outsider", route + "/sparklines?days=7"))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using (var invalid = await GetAsync("owner", route + "/insight123/insights?range=90d"))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var invalid = await GetAsync("owner", route + "/sparklines?days=31"))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var missing = await GetAsync("owner", route + "/absent123/insights?range=7d"))
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using (var response = await GetAsync("owner", route + "/insight123/insights?range=7d"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
                Assert.Equal(0, json!.RootElement.GetProperty("totalClicks").GetInt64());
                Assert.Equal(7, json.RootElement.GetProperty("timeSeries").GetArrayLength());
            }
            using (var response = await GetAsync("owner", route + "/sparklines?days=7"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
                Assert.Equal(2, json!.RootElement.GetArrayLength());
                Assert.All(json.RootElement.EnumerateArray(), row => Assert.Equal(7, row.GetProperty("days").GetArrayLength()));
            }
            var code = "live" + Guid.NewGuid().ToString("N")[..8];
            using (var created = await SendAsync("owner", HttpMethod.Post, route,
                JsonContent.Create(new { OriginalUrl = "https://example.test/live", ShortCode = code })))
                Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            foreach (var (agent, referrer) in new[]
            {
                ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0", "https://linkedin.com/private/path?token=private"),
                ("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Safari/605.1", "https://example.test/private"),
                ("Googlebot/2.1", "https://example.test/bot-private")
            })
            {
                using var redirect = await SendAsync("owner", HttpMethod.Get, "/r/" + code, null, agent, referrer);
                Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            }
            await using (var connection = new NpgsqlConnection(target.ConnectionString))
            {
                await connection.OpenAsync();
                await using var outbox = new NpgsqlCommand("""
                    SELECT event_id, mapping_id, short_code, occurred_at_utc, visitor_hash, referrer_group,
                        referrer_host, device_class, os_family, browser_family, language, is_bot,
                        row_to_json(o)::text
                    FROM click_outbox o WHERE short_code = @code ORDER BY captured_at_utc
                    """, connection);
                outbox.Parameters.AddWithValue("code", code);
                var events = new List<ManagedClickEventV3>();
                await using (var rows = await outbox.ExecuteReaderAsync())
                    while (await rows.ReadAsync())
                    {
                        var persisted = rows.GetString(12);
                        Assert.DoesNotContain("private/path", persisted);
                        Assert.DoesNotContain("Googlebot/2.1", persisted);
                        events.Add(new ManagedClickEventV3(rows.GetGuid(0), workspace, rows.GetString(1),
                            rows.GetString(2), rows.GetFieldValue<DateTimeOffset>(3), null, rows.GetString(4),
                            rows.GetString(5), rows.IsDBNull(6) ? null : rows.GetString(6), rows.GetString(7),
                            rows.IsDBNull(8) ? null : rows.GetString(8), rows.IsDBNull(9) ? null : rows.GetString(9),
                            rows.IsDBNull(10) ? null : rows.GetString(10), rows.GetBoolean(11)));
                    }
                Assert.Equal(3, events.Count);
                await using var dataSource = new NpgsqlDataSourceBuilder(target.ConnectionString).Build();
                var store = new PostgresClickEventStore(dataSource);
                foreach (var click in events) Assert.True(await store.ApplyAsync(click));
                Assert.Equal(1, events.Count(x => x.IsBot));
            }
            using (var result = await GetAsync("owner", route + "/" + code + "/insights?range=24h"))
            {
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                using var json = await result.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
                Assert.Equal(2, json!.RootElement.GetProperty("totalClicks").GetInt64());
                Assert.Equal(2, json.RootElement.GetProperty("uniqueVisitors").GetInt64());
            }
            Assert.NotEmpty(api.CapturedLogs);
            var capturedLogs = string.Join('\n', api.CapturedLogs);
            Assert.DoesNotContain("private/path", capturedLogs);
            Assert.DoesNotContain("Googlebot/2.1", capturedLogs);
            Assert.DoesNotContain("iPhone OS 17_0", capturedLogs);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class CustomerFactory(string connection, SecurityKey signingKey) : WebApplicationFactory<Program>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> CapturedLogs { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureProvider(CapturedLogs)));
            builder.UseSetting("Identity:Mode", "Customer");
            builder.UseSetting("AzureAd:Instance", "https://schwaburlshortener.ciamlogin.com/");
            builder.UseSetting("AzureAd:TenantId", Tenant);
            builder.UseSetting("AzureAd:ClientId", Audience);
            builder.UseSetting("VisitorHash:DailySecret", "integration-test-secret-not-a-real-value");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Mode"] = "Customer",
                ["AzureAd:Instance"] = "https://schwaburlshortener.ciamlogin.com/",
                ["AzureAd:TenantId"] = Tenant,
                ["AzureAd:ClientId"] = Audience,
                ["DataProtection:Mode"] = "Filesystem",
                ["DataProtection:ApplicationName"] = "Shortener.Api",
                ["ConnectionStrings:ShortenerDb"] = connection,
                ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("ConnectionStrings__Redis"),
                ["VisitorHash:DailySecret"] = "integration-test-secret-not-a-real-value"
            }));
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                    metadata.SigningKeys.Add(signingKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                }));
        }
    }

    private sealed class CaptureProvider(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(messages);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
