using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Shortener.Infrastructure.Migrations;

[DbContext(typeof(ShortenerDbContext))]
[Migration("20261013133000_WorkspaceManagedEvents")]
public sealed class WorkspaceManagedEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $repair$
            DECLARE expected bigint; assigned bigint;
            BEGIN
                SELECT count(*) INTO expected FROM url_mappings WHERE mode = 1;
                INSERT INTO workspaces (id, kind, personal_owner_user_id, legacy_tenant_id, name, created_at_utc)
                SELECT md5('schwab-url-shortener:legacy-entra-workspace:' || tenant_id::text)::uuid,
                       1, NULL, tenant_id, 'Legacy Entra team', now()
                FROM (SELECT DISTINCT tenant_id FROM url_mappings WHERE mode = 1 AND tenant_id IS NOT NULL) tenants
                ON CONFLICT (legacy_tenant_id) DO NOTHING;
                UPDATE url_mappings AS mapping SET owner_workspace_id = workspace.id
                FROM workspaces AS workspace
                WHERE mapping.mode = 1 AND mapping.owner_workspace_id IS NULL
                  AND workspace.legacy_tenant_id = mapping.tenant_id;
                SELECT count(*) INTO assigned FROM url_mappings AS mapping
                JOIN workspaces AS workspace ON workspace.id = mapping.owner_workspace_id
                WHERE mapping.mode = 1 AND mapping.tenant_id IS NOT NULL
                  AND workspace.legacy_tenant_id = mapping.tenant_id;
                IF assigned <> expected THEN
                    RAISE EXCEPTION 'Legacy workspace repair count mismatch (expected %, assigned %)', expected, assigned;
                END IF;
            END $repair$;

            ALTER TABLE url_mappings DROP CONSTRAINT ck_url_mappings_link_mode;
            ALTER TABLE url_mappings ADD CONSTRAINT ck_url_mappings_link_mode CHECK (
                (mode = 0 AND tenant_id IS NULL AND owner_workspace_id IS NULL
                 AND expires_at_utc IS NOT NULL AND expires_at_utc = created_at_utc + INTERVAL '168 hours'
                 AND last_accessed_at_utc IS NULL AND hit_count = 0)
                OR
                (mode = 1 AND owner_workspace_id IS NOT NULL AND
                 (tenant_id IS NULL OR tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid)
                 AND expires_at_utc IS NULL AND hit_count >= 0));
            CREATE INDEX ix_url_mappings_workspace_created ON url_mappings(owner_workspace_id, created_at_utc)
                WHERE mode = 1;

            ALTER TABLE click_outbox ALTER COLUMN tenant_id DROP NOT NULL;
            ALTER TABLE click_outbox ADD COLUMN owner_workspace_id uuid NULL REFERENCES workspaces(id);
            ALTER TABLE click_outbox ADD COLUMN event_version smallint NOT NULL DEFAULT 1;
            ALTER TABLE click_outbox ADD CONSTRAINT ck_click_outbox_owner_version CHECK (
                (event_version = 1 AND tenant_id IS NOT NULL AND owner_workspace_id IS NULL) OR
                (event_version = 2 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL));
            CREATE INDEX ix_click_outbox_workspace ON click_outbox(owner_workspace_id, captured_at_utc)
                WHERE event_version = 2;

            ALTER TABLE processed_click_events ALTER COLUMN tenant_id DROP NOT NULL;
            ALTER TABLE processed_click_events ADD COLUMN owner_workspace_id uuid NULL REFERENCES workspaces(id);
            ALTER TABLE processed_click_events ADD COLUMN event_version smallint NOT NULL DEFAULT 1;
            ALTER TABLE processed_click_events ADD CONSTRAINT ck_processed_click_owner_version CHECK (
                (event_version = 1 AND tenant_id IS NOT NULL AND owner_workspace_id IS NULL) OR
                (event_version = 2 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL));
            CREATE INDEX ix_processed_click_workspace ON processed_click_events(owner_workspace_id, processed_at_utc)
                WHERE event_version = 2;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Workspace ownership and click events require a reviewed forward repair and verified backup.");
}
