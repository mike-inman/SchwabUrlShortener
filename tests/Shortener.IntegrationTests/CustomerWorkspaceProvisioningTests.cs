using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shortener.Domain;
using Shortener.Infrastructure;

namespace Shortener.IntegrationTests;

public sealed class CustomerWorkspaceProvisioningTests
{
    [Fact]
    public async Task Concurrent_first_calls_create_one_personal_workspace_and_never_merge_same_email()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (target.Database != "Schwab_test_local" || target.Port != 63651 ||
            target.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("Customer workspace tests require Schwab_test_local on loopback port 63651.");

        var schema = "schwab_customer_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            target.SearchPath = $"{schema},public";
            var services = new ServiceCollection();
            services.AddDbContextFactory<ShortenerDbContext>(options =>
                options.UseNpgsql(target.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema)));
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<ShortenerDbContext>>();
            await using (var setup = await factory.CreateDbContextAsync())
                await setup.Database.MigrateAsync();

            var repository = new PostgresCustomerWorkspaces(factory);
            const string issuer = "https://issuer.example/tenant/v2.0";
            var firstCalls = await Task.WhenAll(Enumerable.Range(0, 12)
                .Select(_ => repository.GetOrCreateAsync(issuer, "customer-one")));
            var personalId = Assert.Single(firstCalls[0]).Id;
            Assert.All(firstCalls, result => Assert.Equal(personalId, Assert.Single(result).Id));
            Assert.Equal(personalId, Assert.Single(await repository.GetOrCreateAsync(issuer, "customer-one")).Id);

            var second = Assert.Single(await repository.GetOrCreateAsync(issuer, "customer-two"));
            Assert.NotEqual(personalId, second.Id);
            await using var db = await factory.CreateDbContextAsync();
            var identities = await db.ExternalIdentities.OrderBy(x => x.Subject).ToListAsync();
            Assert.Equal(2, identities.Count);
            Assert.NotEqual(identities[0].UserId, identities[1].UserId);
            var users = await db.ApplicationUsers.ToListAsync();
            Assert.Equal(2, users.Count);
            users.ForEach(x => x.ContactEmail = "shared@example.test");
            await db.SaveChangesAsync();
            Assert.Equal(personalId, Assert.Single(await repository.GetOrCreateAsync(issuer, "customer-one")).Id);
            Assert.Equal(second.Id, Assert.Single(await repository.GetOrCreateAsync(issuer, "customer-two")).Id);
            Assert.Equal(2, await db.Workspaces.CountAsync());
            db.UrlMappings.Add(UrlMapping.CreateWorkspaceManaged("https://example.test/private", "one1", personalId,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            var access = new PostgresWorkspaceLinkAccess(factory);
            Assert.True(await access.CanManageLinksAsync(issuer, "customer-one", personalId));
            Assert.False(await access.CanManageLinksAsync(issuer, "customer-two", personalId));
            Assert.False(await access.CanManageLinksAsync(issuer, "customer-one", second.Id));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
