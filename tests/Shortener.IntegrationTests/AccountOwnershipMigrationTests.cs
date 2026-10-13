using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shortener.Infrastructure;

namespace Shortener.IntegrationTests;

public sealed class AccountOwnershipMigrationTests
{
    [Fact]
    public async Task PopulatedLegacySchemaBackfillsAndPreservesAnalytics()
    {
        var source = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("A guarded real PostgreSQL test connection is required.");
        var target = new NpgsqlConnectionStringBuilder(source);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Account migration tests require the guarded disposable Schwab_test_local database.");

        var schema = "schwab_accounts_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        await Execute(admin, $"CREATE SCHEMA \"{schema}\"");
        try
        {
            target.SearchPath = $"{schema},public";
            var options = new DbContextOptionsBuilder<ShortenerDbContext>()
                .UseNpgsql(target.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
                .Options;
            await using var context = new ShortenerDbContext(options);
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261013103500_ClickAnalyticsPipeline");

            await using var db = new NpgsqlConnection(target.ConnectionString);
            await db.OpenAsync();
            await Execute(db, """
                INSERT INTO url_mappings
                    (id, short_code, original_url, mode, tenant_id, created_at_utc, expires_at_utc, last_accessed_at_utc, hit_count)
                VALUES
                    ('managed-a', 'acctma', 'https://a.example/path', 1, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', now(), NULL, now(), 9),
                    ('managed-b', 'acctmb', 'https://b.example/path', 1, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', now(), NULL, now(), 4),
                    ('guest', 'acctguest', 'https://guest.example/path', 0, NULL, now(), now() + interval '168 hours', NULL, 0);
                INSERT INTO click_outbox (event_id, tenant_id, mapping_id, short_code, occurred_at_utc, published_at_utc)
                VALUES
                    ('aaaaaaaa-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'managed-a', 'acctma', now(), NULL),
                    ('bbbbbbbb-2222-2222-2222-222222222222', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'managed-b', 'acctmb', now(), now());
                INSERT INTO processed_click_events (event_id, tenant_id, mapping_id)
                VALUES ('bbbbbbbb-2222-2222-2222-222222222222', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'managed-b');
                INSERT INTO click_dead_letters (topic, partition_id, offset_id, event_id, reason_code)
                VALUES ('accounts-test', 0, 1, NULL, 'test-record');
                """);

            var before = await Counts(db);
            Assert.Equal((3L, 2L, 1L, 1L), before);
            var mappingDataBefore = await Scalar<string>(db, """
                SELECT jsonb_agg(jsonb_build_object('id', id, 'short_code', short_code::text,
                    'original_url', original_url, 'mode', mode, 'tenant_id', tenant_id,
                    'created_at_utc', created_at_utc, 'expires_at_utc', expires_at_utc,
                    'last_accessed_at_utc', last_accessed_at_utc, 'hit_count', hit_count)
                    ORDER BY id)::text FROM url_mappings
                """);
            var outboxBefore = await Scalar<string>(db, "SELECT jsonb_agg(to_jsonb(entry) ORDER BY event_id)::text FROM click_outbox AS entry");
            var processedBefore = await Scalar<string>(db, "SELECT jsonb_agg(to_jsonb(entry) ORDER BY event_id)::text FROM processed_click_events AS entry");
            var deadLettersBefore = await Scalar<string>(db, "SELECT jsonb_agg(to_jsonb(entry) ORDER BY offset_id)::text FROM click_dead_letters AS entry");

            await migrator.MigrateAsync("20261013121515_AccountOwnershipFoundation");
            Assert.Equal(before, await Counts(db));
            Assert.Equal(mappingDataBefore, await Scalar<string>(db, """
                SELECT jsonb_agg(jsonb_build_object('id', id, 'short_code', short_code::text,
                    'original_url', original_url, 'mode', mode, 'tenant_id', tenant_id,
                    'created_at_utc', created_at_utc, 'expires_at_utc', expires_at_utc,
                    'last_accessed_at_utc', last_accessed_at_utc, 'hit_count', hit_count)
                    ORDER BY id)::text FROM url_mappings
                """));
            Assert.Equal(outboxBefore, await Scalar<string>(db, "SELECT jsonb_agg(to_jsonb(entry) ORDER BY event_id)::text FROM click_outbox AS entry"));
            Assert.Equal(processedBefore, await Scalar<string>(db, "SELECT jsonb_agg(to_jsonb(entry) ORDER BY event_id)::text FROM processed_click_events AS entry"));
            Assert.Equal(deadLettersBefore, await Scalar<string>(db, "SELECT jsonb_agg(to_jsonb(entry) ORDER BY offset_id)::text FROM click_dead_letters AS entry"));
            Assert.Equal(2L, await Scalar<long>(db, "SELECT count(*) FROM workspaces WHERE kind = 1 AND legacy_tenant_id IS NOT NULL"));
            Assert.Equal(2L, await Scalar<long>(db, """
                SELECT count(*) FROM url_mappings AS mapping
                JOIN workspaces AS workspace ON workspace.id = mapping.owner_workspace_id
                WHERE mapping.mode = 1 AND mapping.tenant_id = workspace.legacy_tenant_id
                """));
            Assert.Equal(1L, await Scalar<long>(db, "SELECT count(*) FROM url_mappings WHERE mode = 0 AND owner_workspace_id IS NULL"));

            await migrator.MigrateAsync("20261013121515_AccountOwnershipFoundation");
            Assert.Equal(before, await Counts(db));
            Assert.Equal(2L, await Scalar<long>(db, "SELECT count(*) FROM workspaces"));

            await CheckConstraints(db);
        }
        finally
        {
            await Execute(admin, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    private static async Task CheckConstraints(NpgsqlConnection db)
    {
        await Execute(db, """
            INSERT INTO application_users (id, created_at_utc) VALUES
                ('11111111-1111-1111-1111-111111111111', now()),
                ('22222222-2222-2222-2222-222222222222', now());
            INSERT INTO workspaces (id, kind, personal_owner_user_id, name, created_at_utc)
            VALUES ('33333333-3333-3333-3333-333333333333', 0, '11111111-1111-1111-1111-111111111111', 'Personal', now());
            INSERT INTO external_identities (id, user_id, issuer, subject, created_at_utc)
            VALUES ('44444444-4444-4444-4444-444444444444', '11111111-1111-1111-1111-111111111111', 'https://issuer.example', 'stable-subject', now());
            """);
        await Reject(db, """
            INSERT INTO external_identities (id, user_id, issuer, subject, created_at_utc)
            VALUES ('55555555-5555-5555-5555-555555555555', '22222222-2222-2222-2222-222222222222', 'https://issuer.example', 'stable-subject', now())
            """, PostgresErrorCodes.UniqueViolation);
        await Reject(db, """
            INSERT INTO external_identities (id, user_id, issuer, subject, created_at_utc)
            VALUES ('55555555-5555-5555-5555-555555555555', '99999999-9999-9999-9999-999999999999', 'https://other.example', 'different-subject', now())
            """, PostgresErrorCodes.ForeignKeyViolation);
        await Reject(db, "UPDATE url_mappings SET owner_workspace_id = (SELECT id FROM workspaces WHERE legacy_tenant_id = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb') WHERE id = 'managed-a'", PostgresErrorCodes.CheckViolation);
        await Reject(db, "UPDATE url_mappings SET owner_workspace_id = '33333333-3333-3333-3333-333333333333' WHERE id = 'guest'", PostgresErrorCodes.CheckViolation);
        await Reject(db, "UPDATE workspaces SET legacy_tenant_id = 'cccccccc-cccc-cccc-cccc-cccccccccccc' WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'", PostgresErrorCodes.CheckViolation);
        await Reject(db, """
            INSERT INTO workspace_memberships (workspace_id, user_id, role, created_at_utc)
            VALUES ('33333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', 0, now())
            """, PostgresErrorCodes.CheckViolation);
        await Reject(db, """
            INSERT INTO workspace_memberships (workspace_id, user_id, role, created_at_utc)
            VALUES ((SELECT id FROM workspaces WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
                    '11111111-1111-1111-1111-111111111111', 9, now())
            """, PostgresErrorCodes.CheckViolation);
        await Execute(db, """
            INSERT INTO workspace_memberships (workspace_id, user_id, role, created_at_utc)
            VALUES ((SELECT id FROM workspaces WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
                    '11111111-1111-1111-1111-111111111111', 0, now())
            """);
        await Reject(db, """
            INSERT INTO workspace_memberships (workspace_id, user_id, role, created_at_utc)
            VALUES ((SELECT id FROM workspaces WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
                    '22222222-2222-2222-2222-222222222222', 0, now())
            """, PostgresErrorCodes.UniqueViolation);
        await Reject(db, """
            INSERT INTO workspace_invitations (id, workspace_id, verifier_hash, created_at_utc, expires_at_utc)
            VALUES ('66666666-6666-6666-6666-666666666666', '33333333-3333-3333-3333-333333333333', repeat('a', 64), now(), now() + interval '1 day')
            """, PostgresErrorCodes.CheckViolation);
        await Reject(db, """
            INSERT INTO workspace_invitations (id, workspace_id, verifier_hash, created_at_utc, expires_at_utc)
            VALUES ('66666666-6666-6666-6666-666666666666',
                    (SELECT id FROM workspaces WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
                    repeat('a', 64), now(), now() - interval '1 second')
            """, PostgresErrorCodes.CheckViolation);
        await Reject(db, """
            INSERT INTO workspace_invitations (id, workspace_id, verifier_hash, created_at_utc, expires_at_utc)
            VALUES ('66666666-6666-6666-6666-666666666666',
                    (SELECT id FROM workspaces WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
                    'raw-token', now(), now() + interval '1 day')
            """, PostgresErrorCodes.CheckViolation);
        await Execute(db, """
            INSERT INTO workspace_invitations (id, workspace_id, verifier_hash, created_at_utc, expires_at_utc)
            VALUES ('66666666-6666-6666-6666-666666666666',
                    (SELECT id FROM workspaces WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
                    repeat('a', 64), now(), now() + interval '1 day')
            """);
        await Reject(db, """
            INSERT INTO workspace_invitations (id, workspace_id, verifier_hash, created_at_utc, expires_at_utc)
            VALUES ('77777777-7777-7777-7777-777777777777',
                    (SELECT id FROM workspaces WHERE legacy_tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
                    repeat('a', 64), now(), now() + interval '1 day')
            """, PostgresErrorCodes.UniqueViolation);
        await Reject(db, """
            UPDATE workspace_invitations SET redeemed_at_utc = expires_at_utc + interval '1 second',
                redeemed_by_user_id = '11111111-1111-1111-1111-111111111111'
            WHERE id = '66666666-6666-6666-6666-666666666666'
            """, PostgresErrorCodes.CheckViolation);
        await Execute(db, """
            UPDATE workspace_invitations SET redeemed_at_utc = created_at_utc + interval '1 hour',
                redeemed_by_user_id = '11111111-1111-1111-1111-111111111111'
            WHERE id = '66666666-6666-6666-6666-666666666666'
            """);
        await Reject(db, """
            UPDATE workspace_invitations SET redeemed_at_utc = NULL, redeemed_by_user_id = NULL
            WHERE id = '66666666-6666-6666-6666-666666666666'
            """, PostgresErrorCodes.CheckViolation);
        await Reject(db, """
            UPDATE workspace_invitations SET expires_at_utc = expires_at_utc + interval '1 day'
            WHERE id = '66666666-6666-6666-6666-666666666666'
            """, PostgresErrorCodes.CheckViolation);
    }

    private static async Task<(long Mappings, long Outbox, long Processed, long DeadLetters)> Counts(NpgsqlConnection db)
    {
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM url_mappings), (SELECT count(*) FROM click_outbox),
                   (SELECT count(*) FROM processed_click_events), (SELECT count(*) FROM click_dead_letters)
            """, db);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected a scalar result."));
    }

    private static async Task Execute(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task Reject(NpgsqlConnection db, string sql, string expectedState)
    {
        await using var command = new NpgsqlCommand(sql, db);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedState, error.SqlState);
    }
}
