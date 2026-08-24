using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameBookingPauseAndAddGlobalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "service_mode_message",
                table: "Tenants",
                newName: "booking_pause_message");

            migrationBuilder.RenameColumn(
                name: "service_mode_enabled",
                table: "Tenants",
                newName: "booking_pause_enabled");

            migrationBuilder.CreateTable(
                name: "global_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    maintenance_message = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    maintenance_started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "global_settings");

            migrationBuilder.RenameColumn(
                name: "booking_pause_message",
                table: "Tenants",
                newName: "service_mode_message");

            migrationBuilder.RenameColumn(
                name: "booking_pause_enabled",
                table: "Tenants",
                newName: "service_mode_enabled");
        }
    }
}
