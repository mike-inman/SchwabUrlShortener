using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Shortener.Infrastructure.Migrations;

[DbContext(typeof(ShortenerDbContext))]
[Migration("20261013103500_ClickAnalyticsPipeline")]
public sealed class ClickAnalyticsPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE click_outbox (
                event_id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
                mapping_id text NOT NULL REFERENCES url_mappings(id),
                short_code citext NOT NULL,
                occurred_at_utc timestamptz NOT NULL,
                trace_id text NULL,
                captured_at_utc timestamptz NOT NULL DEFAULT now(),
                published_at_utc timestamptz NULL,
                failed_at_utc timestamptz NULL,
                attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
                next_attempt_at_utc timestamptz NOT NULL DEFAULT now(),
                last_error_code varchar(80) NULL
            );
            CREATE INDEX ix_click_outbox_pending ON click_outbox(next_attempt_at_utc, captured_at_utc)
                WHERE published_at_utc IS NULL AND failed_at_utc IS NULL;
            CREATE INDEX ix_click_outbox_tenant ON click_outbox(tenant_id, captured_at_utc);

            CREATE TABLE processed_click_events (
                event_id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL,
                mapping_id text NOT NULL,
                processed_at_utc timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_processed_click_events_tenant ON processed_click_events(tenant_id, processed_at_utc);

            CREATE TABLE click_dead_letters (
                topic text NOT NULL,
                partition_id integer NOT NULL,
                offset_id bigint NOT NULL,
                event_id uuid NULL,
                reason_code varchar(80) NOT NULL,
                recorded_at_utc timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (topic, partition_id, offset_id)
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE click_dead_letters; DROP TABLE processed_click_events; DROP TABLE click_outbox;");
    }
}
