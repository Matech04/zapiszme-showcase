using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceComboGroupAndAppointmentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "combo_group",
                table: "Services",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentServiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentServiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentServiceItems_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppointmentServiceItems_Services_service_id",
                        column: x => x.service_id,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentServiceItems_AppointmentId_position",
                table: "AppointmentServiceItems",
                columns: new[] { "AppointmentId", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentServiceItems_service_id",
                table: "AppointmentServiceItems",
                column: "service_id");

            // Backfill: każda istniejąca wizyta dostaje JEDNĄ pozycję (position 0) zbudowaną z jej
            // dotychczasowego ServiceId + total_price + (EndTime-StartTime). Idempotentne (NOT EXISTS),
            // czysto addytywne — żadnych zmian/usunięć istniejących danych.
            migrationBuilder.Sql(@"
                INSERT INTO ""AppointmentServiceItems""
                    (""Id"", tenant_id, ""AppointmentId"", service_id, duration_minutes, price_amount, price_currency, position)
                SELECT gen_random_uuid(),
                       a.""TenantId"",
                       a.""Id"",
                       a.""ServiceId"",
                       (EXTRACT(EPOCH FROM (a.""EndTime"" - a.""StartTime"")) / 60)::int,
                       a.total_price_amount,
                       a.total_price_currency,
                       0
                FROM ""Appointments"" a
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""AppointmentServiceItems"" i WHERE i.""AppointmentId"" = a.""Id""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentServiceItems");

            migrationBuilder.DropColumn(
                name: "combo_group",
                table: "Services");
        }
    }
}
