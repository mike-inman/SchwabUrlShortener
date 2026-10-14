using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Shortener.Infrastructure.Migrations;

[DbContext(typeof(ShortenerDbContext))]
[Migration("20261013150000_LinkInsightsCollection")]
public sealed class LinkInsightsCollection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE click_outbox
                ADD COLUMN visitor_hash varchar(32) NULL,
                ADD COLUMN referrer_group varchar(20) NULL,
                ADD COLUMN referrer_host text NULL,
                ADD COLUMN device_class varchar(20) NULL,
                ADD COLUMN os_family varchar(20) NULL,
                ADD COLUMN browser_family varchar(20) NULL,
                ADD COLUMN language varchar(35) NULL,
                ADD COLUMN is_bot boolean NOT NULL DEFAULT false;

            ALTER TABLE click_outbox DROP CONSTRAINT ck_click_outbox_owner_version;
            ALTER TABLE click_outbox ADD CONSTRAINT ck_click_outbox_owner_version CHECK (
                (event_version = 1 AND tenant_id IS NOT NULL AND owner_workspace_id IS NULL
                    AND visitor_hash IS NULL AND referrer_group IS NULL AND device_class IS NULL) OR
                (event_version = 2 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL
                    AND visitor_hash IS NULL AND referrer_group IS NULL AND device_class IS NULL) OR
                (event_version = 3 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL
                    AND visitor_hash IS NOT NULL AND referrer_group IS NOT NULL AND device_class IS NOT NULL));

            ALTER TABLE processed_click_events DROP CONSTRAINT ck_processed_click_owner_version;
            ALTER TABLE processed_click_events ADD CONSTRAINT ck_processed_click_owner_version CHECK (
                (event_version = 1 AND tenant_id IS NOT NULL AND owner_workspace_id IS NULL) OR
                (event_version = 2 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL) OR
                (event_version = 3 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL));

            DROP INDEX ix_click_outbox_workspace;
            CREATE INDEX ix_click_outbox_workspace ON click_outbox(owner_workspace_id, captured_at_utc)
                WHERE event_version IN (2, 3);
            DROP INDEX ix_processed_click_workspace;
            CREATE INDEX ix_processed_click_workspace ON processed_click_events(owner_workspace_id, processed_at_utc)
                WHERE event_version IN (2, 3);

            CREATE TABLE link_click_facts (
                event_id uuid PRIMARY KEY,
                mapping_id text NOT NULL REFERENCES url_mappings(id),
                owner_workspace_id uuid NOT NULL REFERENCES workspaces(id),
                short_code citext NOT NULL,
                occurred_at_utc timestamptz NOT NULL,
                referrer_group varchar(20) NULL,
                referrer_host text NULL,
                device_class varchar(20) NULL,
                os_family varchar(20) NULL,
                browser_family varchar(20) NULL,
                language varchar(35) NULL,
                visitor_hash varchar(32) NULL,
                is_bot boolean NOT NULL,
                country_code varchar(2) NULL,
                event_version smallint NOT NULL CHECK (event_version IN (1, 2, 3))
            );
            CREATE INDEX ix_link_click_facts_mapping_time ON link_click_facts(mapping_id, occurred_at_utc);
            CREATE INDEX ix_link_click_facts_workspace_time ON link_click_facts(owner_workspace_id, occurred_at_utc);

            CREATE TABLE link_click_hourly (
                mapping_id text NOT NULL REFERENCES url_mappings(id),
                owner_workspace_id uuid NOT NULL REFERENCES workspaces(id),
                bucket_start_utc timestamptz NOT NULL,
                clicks bigint NOT NULL DEFAULT 0 CHECK (clicks >= 0),
                PRIMARY KEY (mapping_id, bucket_start_utc)
            );
            CREATE INDEX ix_link_click_hourly_workspace_time ON link_click_hourly(owner_workspace_id, bucket_start_utc);

            CREATE TABLE link_click_dimension_daily (
                mapping_id text NOT NULL REFERENCES url_mappings(id),
                owner_workspace_id uuid NOT NULL REFERENCES workspaces(id),
                bucket_date_utc date NOT NULL,
                dimension varchar(10) NOT NULL CHECK (dimension IN ('referrer', 'device')),
                dimension_key varchar(20) NOT NULL,
                clicks bigint NOT NULL DEFAULT 0 CHECK (clicks >= 0),
                PRIMARY KEY (mapping_id, bucket_date_utc, dimension, dimension_key)
            );
            CREATE INDEX ix_link_click_dimension_daily_workspace_time ON link_click_dimension_daily(owner_workspace_id, bucket_date_utc);

            CREATE TABLE link_visitor_daily (
                mapping_id text NOT NULL REFERENCES url_mappings(id),
                owner_workspace_id uuid NOT NULL REFERENCES workspaces(id),
                bucket_date_utc date NOT NULL,
                visitor_hash varchar(32) NOT NULL,
                PRIMARY KEY (mapping_id, bucket_date_utc, visitor_hash)
            );
            CREATE INDEX ix_link_visitor_daily_retention ON link_visitor_daily(bucket_date_utc);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE link_visitor_daily;
            DROP TABLE link_click_dimension_daily;
            DROP TABLE link_click_hourly;
            DROP TABLE link_click_facts;

            DROP INDEX ix_processed_click_workspace;
            CREATE INDEX ix_processed_click_workspace ON processed_click_events(owner_workspace_id, processed_at_utc)
                WHERE event_version = 2;
            DROP INDEX ix_click_outbox_workspace;
            CREATE INDEX ix_click_outbox_workspace ON click_outbox(owner_workspace_id, captured_at_utc)
                WHERE event_version = 2;

            ALTER TABLE processed_click_events DROP CONSTRAINT ck_processed_click_owner_version;
            ALTER TABLE processed_click_events ADD CONSTRAINT ck_processed_click_owner_version CHECK (
                (event_version = 1 AND tenant_id IS NOT NULL AND owner_workspace_id IS NULL) OR
                (event_version = 2 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL));

            ALTER TABLE click_outbox DROP CONSTRAINT ck_click_outbox_owner_version;
            ALTER TABLE click_outbox ADD CONSTRAINT ck_click_outbox_owner_version CHECK (
                (event_version = 1 AND tenant_id IS NOT NULL AND owner_workspace_id IS NULL) OR
                (event_version = 2 AND tenant_id IS NULL AND owner_workspace_id IS NOT NULL));

            ALTER TABLE click_outbox
                DROP COLUMN visitor_hash,
                DROP COLUMN referrer_group,
                DROP COLUMN referrer_host,
                DROP COLUMN device_class,
                DROP COLUMN os_family,
                DROP COLUMN browser_family,
                DROP COLUMN language,
                DROP COLUMN is_bot;
            """);
    }
}
