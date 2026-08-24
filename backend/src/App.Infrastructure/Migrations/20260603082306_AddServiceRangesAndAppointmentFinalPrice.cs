using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRangesAndAppointmentFinalPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duration_max_minutes",
                table: "Services",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_min_minutes",
                table: "Services",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_max_amount",
                table: "Services",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "final_price_amount",
                table: "Appointments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "final_price_currency",
                table: "Appointments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "duration_max_minutes",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "duration_min_minutes",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "price_max_amount",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "final_price_amount",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "final_price_currency",
                table: "Appointments");
        }
    }
}
