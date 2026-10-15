using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortener.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContactInboxHandled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "handled_at_utc",
                table: "contact_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contact_messages_inbox_order",
                table: "contact_messages",
                columns: new[] { "created_at_utc", "id" },
                descending: new[] { true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_contact_messages_inbox_order",
                table: "contact_messages");

            migrationBuilder.DropColumn(
                name: "handled_at_utc",
                table: "contact_messages");
        }
    }
}
