using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "deposit_enabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "deposit_instrument",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "deposit_mode",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_value",
                table: "Tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "merchant_account_id",
                table: "Tenants",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "merchant_charges_enabled",
                table: "Tenants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "merchant_onboarding_status",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "merchant_provider",
                table: "Tenants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_override_value",
                table: "Services",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_amount",
                table: "Appointments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deposit_currency",
                table: "Appointments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "link_expires_at_utc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at_utc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_link_url",
                table: "Appointments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_session_id",
                table: "Appointments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_status",
                table: "Appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_payment_status_link_expires_at_utc",
                table: "Appointments",
                columns: new[] { "payment_status", "link_expires_at_utc" },
                filter: "payment_status = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_payment_status_link_expires_at_utc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "deposit_enabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "deposit_instrument",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "deposit_mode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "deposit_value",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "merchant_account_id",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "merchant_charges_enabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "merchant_onboarding_status",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "merchant_provider",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "deposit_override_value",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "deposit_amount",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "deposit_currency",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "link_expires_at_utc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "paid_at_utc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "payment_link_url",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "payment_session_id",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "payment_status",
                table: "Appointments");
        }
    }
}
