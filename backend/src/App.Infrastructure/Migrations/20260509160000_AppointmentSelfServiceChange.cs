using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentSelfServiceChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "self_service_changed_at",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "self_service_change_kind",
                table: "Appointments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_self_service_changed_at",
                table: "Appointments",
                column: "self_service_changed_at",
                filter: "self_service_changed_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_self_service_changed_at",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "self_service_changed_at",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "self_service_change_kind",
                table: "Appointments");
        }
    }
}
