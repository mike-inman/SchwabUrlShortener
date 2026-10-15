using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shortener.Analytics;
using Shortener.Application;
using Shortener.Infrastructure;

namespace Shortener.IntegrationTests;

public sealed class ContactFlowTests
{
    [Fact]
    public async Task Contact_migration_api_and_worker_use_guarded_postgres()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Contact tests require guarded disposable Schwab_test_local.");
        var schema = "schwab_contact_" + Guid.NewGuid().ToString("N");
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
            await migrator.MigrateAsync("20261013150000_LinkInsightsCollection");
            await migrator.MigrateAsync();
            Assert.Empty(await db.ContactMessages.ToListAsync());

            using var factory = new ContactApiFactory(target.ConnectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
            });
            var token = (await client.GetFromJsonAsync<TokenResponse>("/api/contact/form-token"))!.Token;
            var valid = new ContactSubmission("Visitor", "visitor@example.test", "Question", "A useful question for Ziplio.", null, token);
            using var fast = await client.PostAsJsonAsync("/api/contact", valid);
            Assert.Equal(HttpStatusCode.Created, fast.StatusCode);
            using var trap = await client.PostAsJsonAsync("/api/contact", valid with { Website = "filled" });
            Assert.Equal(HttpStatusCode.Created, trap.StatusCode);
            Assert.Empty(await db.ContactMessages.ToListAsync());
            await Task.Delay(TimeSpan.FromSeconds(3.1));
            using var invalid = await client.PostAsJsonAsync("/api/contact", valid with { Email = "invalid" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var accepted = await client.PostAsJsonAsync("/api/contact", valid);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            var stored = Assert.Single(await db.ContactMessages.AsNoTracking().ToListAsync());
            Assert.Equal("Visitor", stored.Name);
            Assert.Equal(64, stored.ClientKeyHash.Length);
            using var fifth = await client.PostAsJsonAsync("/api/contact", valid with { Email = "invalid" });
            Assert.Equal(HttpStatusCode.BadRequest, fifth.StatusCode);
            using var limited = await client.PostAsJsonAsync("/api/contact", valid);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.True(limited.Headers.TryGetValues("Retry-After", out _));

            await using var dataSource = new NpgsqlDataSourceBuilder(target.ConnectionString).Build();
            var none = new ContactNotificationDispatcher(dataSource,
                new NoContactNotifier(NullLogger<NoContactNotifier>.Instance),
                NullLogger<ContactNotificationDispatcher>.Instance);
            Assert.Equal(ContactNotificationOutcome.Handled, await none.ProcessOneAsync());
            Assert.NotNull((await db.ContactMessages.AsNoTracking().SingleAsync()).NotifiedAtUtc);

            var successMessage = NewMessage();
            var failingMessage = NewMessage();
            db.ContactMessages.AddRange(successMessage, failingMessage);
            await db.SaveChangesAsync();
            var sender = new TestNotifier();
            var dispatcher = new ContactNotificationDispatcher(dataSource, sender,
                NullLogger<ContactNotificationDispatcher>.Instance);
            Assert.Equal(ContactNotificationOutcome.Handled, await dispatcher.ProcessOneAsync());
            Assert.Contains(successMessage.Id, sender.Sent);
            sender.Fail = true;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                Assert.Equal(attempt == 5 ? ContactNotificationOutcome.Failed : ContactNotificationOutcome.Retried,
                    await dispatcher.ProcessOneAsync());
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE contact_messages SET next_attempt_at_utc = now() WHERE id = {failingMessage.Id}");
            }
            var failed = await db.ContactMessages.AsNoTracking().SingleAsync(x => x.Id == failingMessage.Id);
            Assert.Equal(5, failed.NotificationAttempts);
            Assert.NotNull(failed.FailedAtUtc);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static ContactMessage NewMessage() => new()
    {
        Id = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, Name = "Visitor", Email = "visitor@example.test",
        Message = "A useful question for Ziplio.", ClientKeyHash = new string('A', 64),
        NextAttemptAtUtc = DateTimeOffset.UtcNow
    };

    private sealed record TokenResponse(string Token);

    private sealed class TestNotifier : IContactNotifier
    {
        public bool Fail { get; set; }
        public List<Guid> Sent { get; } = [];
        public Task NotifyAsync(ContactMessage message, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("test notification failed");
            Sent.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class ContactApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AzureAd:ClientId", "11111111-1111-1111-1111-111111111111");
            builder.UseSetting("ConnectionStrings:ShortenerDb", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", Environment.GetEnvironmentVariable("ConnectionStrings__Redis")
                ?? throw new InvalidOperationException("Real Redis test connection required."));
            builder.UseSetting("PublicLinks:BaseUrl", "https://localhost/");
            builder.UseSetting("VisitorHash:DailySecret", "integration-test-secret-not-a-real-value");
        }
    }
}
