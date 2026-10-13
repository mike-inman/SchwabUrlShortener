using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortener.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialUrlMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "url_mappings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    short_code = table.Column<string>(type: "citext", maxLength: 12, nullable: false),
                    original_url = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_accessed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hit_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_url_mappings", x => x.id);
                    table.CheckConstraint("ck_url_mappings_link_mode", "(mode = 0 AND tenant_id IS NULL AND expires_at_utc IS NOT NULL AND expires_at_utc = created_at_utc + INTERVAL '168 hours' AND last_accessed_at_utc IS NULL AND hit_count = 0) OR (mode = 1 AND tenant_id IS NOT NULL AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND expires_at_utc IS NULL AND hit_count >= 0)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_url_mappings_tenant_created",
                table: "url_mappings",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_url_mappings_tenant_original",
                table: "url_mappings",
                columns: new[] { "tenant_id", "original_url" });

            migrationBuilder.CreateIndex(
                name: "ux_url_mappings_short_code",
                table: "url_mappings",
                column: "short_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "url_mappings");
        }
    }
}
