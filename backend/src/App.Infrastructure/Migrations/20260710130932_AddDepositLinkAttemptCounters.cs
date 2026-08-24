using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositLinkAttemptCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "deposit_link_attempts",
                table: "Appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "expired_deposit_link_count",
                table: "Appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: wizyty z już wygenerowanym linkiem miały dokładnie jedną (znaną nam) próbę.
            // Bez tego panel pokazałby dla nich „0. link". Historii sprzed tej migracji nie znamy,
            // więc expired_deposit_link_count zostaje 0 — świadomie zaniżone, nie zmyślamy danych.
            migrationBuilder.Sql(
                """
                UPDATE "Appointments"
                SET deposit_link_attempts = 1
                WHERE payment_link_url IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deposit_link_attempts",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "expired_deposit_link_count",
                table: "Appointments");
        }
    }
}
