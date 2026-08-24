using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotificationRemindersAndSalonEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "notify_appointment_reminder_2h_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_awaiting_confirmation_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_cancelled_by_salon_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_rescheduled_by_salon_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reminder_24h_sent_at_utc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reminder_2h_sent_at_utc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notify_appointment_reminder_2h_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_awaiting_confirmation_to_salon",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_cancelled_by_salon_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_rescheduled_by_salon_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "reminder_24h_sent_at_utc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "reminder_2h_sent_at_utc",
                table: "Appointments");
        }
    }
}
