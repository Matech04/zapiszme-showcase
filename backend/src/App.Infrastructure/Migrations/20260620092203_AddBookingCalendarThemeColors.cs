using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCalendarThemeColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "booking_calendar_background_hex",
                table: "Tenants",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "booking_calendar_price_hex",
                table: "Tenants",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "booking_calendar_surface_hex",
                table: "Tenants",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "booking_calendar_background_hex",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "booking_calendar_price_hex",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "booking_calendar_surface_hex",
                table: "Tenants");
        }
    }
}
