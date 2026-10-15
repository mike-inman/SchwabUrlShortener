using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Shortener.Infrastructure;
using Shortener.Api;
using Shortener.Application;

namespace Shortener.IntegrationTests;

public sealed class ContactInboxTests
{
    private const string Issuer = "https://example.test/customer/v2.0";
    private const string Subject = "michael-test-subject";

    [Fact]
    public void Admin_configuration_is_empty_by_default_and_matches_both_claims_exactly()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", Issuer), new Claim("sub", Subject)
        ], "Test"));
        Assert.False(new ContactInboxAdmin(new ConfigurationBuilder().Build()).IsAdmin(principal));
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Admin:Subjects:0"] = Issuer + "|" + Subject }).Build();
        var admin = new ContactInboxAdmin(configured);
        Assert.True(admin.IsAdmin(principal));
        Assert.False(admin.IsAdmin(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", "https://another.test/customer/v2.0"), new Claim("sub", Subject)
        ], "Test"))));
        Assert.True(LinkChange.TryDeserialize(ContactInboxHint.Change.Serialize(), out var hint));
        Assert.Equal(LinkChangeScope.Admin, hint!.Scope);
        Assert.Equal(ContactInboxHint.ScopeId, hint.ScopeId);
    }

    [Fact]
    public async Task Admin_inbox_enforces_access_paginates_and_marks_handled_on_a_reversible_migration()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Contact inbox tests require guarded disposable Schwab_test_local.");
        var schema = "schwab_inbox_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            target.SearchPath = $"{schema},public";
            var options = new DbContextOptionsBuilder<ShortenerDbContext>()
                .UseNpgsql(target.ConnectionString, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using var db = new ShortenerDbContext(options);
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261014181953_ContactMessages");
            var when = new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);
            var first = Message(1, when);
            var second = Message(2, when);
            var third = Message(3, when.AddMinutes(1));
            foreach (var message in new[] { first, second, third })
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO contact_messages (id, created_at_utc, name, email, subject, message,
                        client_key_hash, next_attempt_at_utc, notification_attempts)
                    VALUES ({message.Id}, {message.CreatedAtUtc}, {message.Name}, {message.Email},
                        {message.Subject}, {message.Message}, {message.ClientKeyHash},
                        {message.NextAttemptAtUtc}, {message.NotificationAttempts})
                    """);
            await migrator.MigrateAsync();
            Assert.Equal(3, await db.ContactMessages.CountAsync());

            using var factory = new InboxFactory(target.ConnectionString);
            using var anonymous = Client(factory);
            using var nonAdmin = Client(factory, "someone-else");
            using var michael = Client(factory, Subject);
            foreach (var client in new[] { anonymous, nonAdmin })
            {
                using var list = await client.GetAsync("/api/admin/contact-messages");
                using var handled = await client.PostAsync($"/api/admin/contact-messages/{first.Id}/handled", null);
                Assert.Equal(client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, list.StatusCode);
                Assert.Equal(client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, handled.StatusCode);
            }
            var caller = await michael.GetFromJsonAsync<AdminInfo>("/api/me");
            Assert.True(caller!.IsContactAdmin);
            Assert.Equal(3, caller.UnhandledContactCount);
            var other = await nonAdmin.GetFromJsonAsync<AdminInfo>("/api/me");
            Assert.False(other!.IsContactAdmin);
            Assert.Equal(0, other.UnhandledContactCount);

            using var firstResponse = await michael.GetAsync("/api/admin/contact-messages?take=2");
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var json = await firstResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("clientKeyHash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(first.ClientKeyHash, json, StringComparison.Ordinal);
            var page = JsonSerializer.Deserialize<Page>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal([third.Id, second.Id], page.Items.Select(x => x.Id));
            Assert.NotNull(page.NextCursor);

            db.ContactMessages.Add(Message(4, when.AddMinutes(2)));
            await db.SaveChangesAsync();
            var older = await michael.GetFromJsonAsync<Page>("/api/admin/contact-messages?take=2&cursor=" +
                Uri.EscapeDataString(page.NextCursor));
            Assert.Equal(first.Id, Assert.Single(older!.Items).Id);
            Assert.Null(older.NextCursor);
            using var invalid = await michael.GetAsync("/api/admin/contact-messages?cursor=invalid!");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

            using var mark = await michael.PostAsync($"/api/admin/contact-messages/{first.Id}/handled", null);
            using var again = await michael.PostAsync($"/api/admin/contact-messages/{first.Id}/handled", null);
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal(await mark.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
            Assert.NotNull((await db.ContactMessages.AsNoTracking().SingleAsync(x => x.Id == first.Id)).HandledAtUtc);
            Assert.Equal(3, (await michael.GetFromJsonAsync<AdminInfo>("/api/me"))!.UnhandledContactCount);

            await migrator.MigrateAsync("20261014181953_ContactMessages");
            await using (var column = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = @schema AND table_name = 'contact_messages' AND column_name = 'handled_at_utc'", admin))
            {
                column.Parameters.AddWithValue("schema", schema);
                Assert.Equal(0L, await column.ExecuteScalarAsync());
            }
            await migrator.MigrateAsync();
            Assert.Equal(4, await db.ContactMessages.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static ContactMessage Message(int number, DateTimeOffset created) => new()
    {
        Id = Guid.Parse($"00000000-0000-0000-0000-{number:000000000000}"),
        CreatedAtUtc = created, Name = "Visitor", Email = "visitor@example.test",
        Subject = "Question", Message = "<script>alert('x')</script>",
        ClientKeyHash = new string('A', 64), NextAttemptAtUtc = created
    };

    private static HttpClient Client(InboxFactory factory, string? subject = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Issuer", Issuer);
            client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            client.DefaultRequestHeaders.Add("X-Test-Scope", "access_as_user");
        }
        return client;
    }

    private sealed record AdminInfo(bool IsContactAdmin, int UnhandledContactCount);
    private sealed record Item(Guid Id);
    private sealed record Page(IReadOnlyList<Item> Items, string? NextCursor, int UnhandledContactCount);

    private sealed class InboxFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AzureAd:ClientId", "11111111-1111-1111-1111-111111111111");
            builder.UseSetting("ConnectionStrings:ShortenerDb", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", Environment.GetEnvironmentVariable("ConnectionStrings__Redis")
                ?? throw new InvalidOperationException("Real Redis test connection required."));
            builder.UseSetting("VisitorHash:DailySecret", "integration-test-secret-not-a-real-value");
            builder.UseSetting("Admin:Subjects:0", Issuer + "|" + Subject);
            builder.ConfigureTestServices(services =>
            {
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

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Subject", out var subject))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("sub", subject.ToString()),
                new Claim("iss", Request.Headers["X-Test-Issuer"].ToString()),
                new Claim("scp", Request.Headers["X-Test-Scope"].ToString())
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
