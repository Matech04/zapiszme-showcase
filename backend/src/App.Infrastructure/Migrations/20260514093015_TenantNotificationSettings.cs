using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "notify_appointment_reminder_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_booking_confirmation_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_cancellation_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_cancellation_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_new_booking_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_reschedule_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_reschedule_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notify_appointment_reminder_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_booking_confirmation_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_cancellation_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_cancellation_to_salon",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_new_booking_to_salon",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_reschedule_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notify_reschedule_to_salon",
                table: "Tenants");
        }
    }
}
