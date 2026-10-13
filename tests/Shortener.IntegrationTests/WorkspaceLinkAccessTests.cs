using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shortener.Application.Interfaces;
using Shortener.Infrastructure;

namespace Shortener.IntegrationTests;

public sealed class WorkspaceLinkAccessTests
{
    [Fact]
    public async Task ExplicitIdentityAndMembershipControlPersonalAndTeamLinkAccess()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Workspace access tests require the guarded disposable Schwab_test_local database.");

        var schema = "schwab_access_" + Guid.NewGuid().ToString("N");
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
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();

            var owner = Guid.NewGuid();
            var adminUser = Guid.NewGuid();
            var member = Guid.NewGuid();
            var outsider = Guid.NewGuid();
            var personal = Guid.NewGuid();
            var team = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            db.ApplicationUsers.AddRange(
                new ApplicationUser { Id = owner, CreatedAtUtc = now, ContactEmail = "shared@example.test" },
                new ApplicationUser { Id = adminUser, CreatedAtUtc = now },
                new ApplicationUser { Id = member, CreatedAtUtc = now },
                new ApplicationUser { Id = outsider, CreatedAtUtc = now, ContactEmail = "shared@example.test" });
            db.Workspaces.AddRange(
                new Workspace { Id = personal, Kind = WorkspaceKind.Personal, PersonalOwnerUserId = owner, Name = "Personal", CreatedAtUtc = now },
                new Workspace { Id = team, Kind = WorkspaceKind.Team, LegacyTenantId = Guid.NewGuid(), Name = "Team", CreatedAtUtc = now });
            db.ExternalIdentities.AddRange(
                new ExternalIdentity { Id = Guid.NewGuid(), UserId = owner, Issuer = "https://issuer.example/a", Subject = "owner", CreatedAtUtc = now },
                new ExternalIdentity { Id = Guid.NewGuid(), UserId = adminUser, Issuer = "https://issuer.example/a", Subject = "admin", CreatedAtUtc = now },
                new ExternalIdentity { Id = Guid.NewGuid(), UserId = member, Issuer = "https://issuer.example/a", Subject = "member", CreatedAtUtc = now },
                new ExternalIdentity { Id = Guid.NewGuid(), UserId = outsider, Issuer = "https://issuer.example/b", Subject = "owner", CreatedAtUtc = now });
            db.WorkspaceMemberships.AddRange(
                new WorkspaceMembership { WorkspaceId = team, UserId = owner, Role = TeamRole.Owner, CreatedAtUtc = now },
                new WorkspaceMembership { WorkspaceId = team, UserId = adminUser, Role = TeamRole.Admin, CreatedAtUtc = now },
                new WorkspaceMembership { WorkspaceId = team, UserId = member, Role = TeamRole.Member, CreatedAtUtc = now });
            await db.SaveChangesAsync();

            IWorkspaceLinkAccess access = new PostgresWorkspaceLinkAccess(factory);
            Assert.True(await access.CanManageLinksAsync("https://issuer.example/a", "owner", personal));
            Assert.True(await access.CanManageLinksAsync("https://issuer.example/a", "owner", team));
            Assert.True(await access.CanManageLinksAsync("https://issuer.example/a", "admin", team));
            Assert.True(await access.CanManageLinksAsync("https://issuer.example/a", "member", team));
            Assert.False(await access.CanManageLinksAsync("https://issuer.example/a", "admin", personal));
            Assert.False(await access.CanManageLinksAsync("https://issuer.example/b", "owner", personal));
            Assert.False(await access.CanManageLinksAsync("https://issuer.example/b", "owner", team));
            Assert.False(await access.CanManageLinksAsync("https://issuer.example/a", "unknown", team));
            Assert.False(await access.CanManageLinksAsync("", "owner", team));
            Assert.False(await access.CanManageLinksAsync("https://issuer.example/a", "owner", Guid.Empty));

            db.WorkspaceMemberships.Remove(db.WorkspaceMemberships.Single(row => row.WorkspaceId == team && row.UserId == member));
            await db.SaveChangesAsync();
            Assert.False(await access.CanManageLinksAsync("https://issuer.example/a", "member", team));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
