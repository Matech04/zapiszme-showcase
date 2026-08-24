using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsChannelToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sms_channel_enabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_appointment_reminder_2h_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_appointment_reminder_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_awaiting_confirmation_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_booking_confirmation_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_cancellation_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_cancellation_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_cancelled_by_salon_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_new_booking_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_reschedule_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_reschedule_to_salon",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sms_notify_rescheduled_by_salon_to_customer",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sms_channel_enabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_appointment_reminder_2h_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_appointment_reminder_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_awaiting_confirmation_to_salon",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_booking_confirmation_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_cancellation_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_cancellation_to_salon",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_cancelled_by_salon_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_new_booking_to_salon",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_reschedule_to_customer",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_reschedule_to_salon",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "sms_notify_rescheduled_by_salon_to_customer",
                table: "Tenants");
        }
    }
}
