using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantGapFillingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "gap_filling_buffer_minutes",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gap_filling_lookahead_slots",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gap_filling_mode",
                table: "Tenants",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gap_filling_buffer_minutes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "gap_filling_lookahead_slots",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "gap_filling_mode",
                table: "Tenants");
        }
    }
}
