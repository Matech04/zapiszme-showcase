using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthPublicationAndBookingHorizon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "booking_horizon_days",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.CreateTable(
                name: "EmployeeMonthPublications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    opens_on = table.Column<DateOnly>(type: "date", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeMonthPublications", x => x.id);
                    table.ForeignKey(
                        name: "FK_EmployeeMonthPublications_Employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeMonthPublications_employee_id_year_month",
                table: "EmployeeMonthPublications",
                columns: new[] { "employee_id", "year", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeMonthPublications");

            migrationBuilder.DropColumn(
                name: "booking_horizon_days",
                table: "Tenants");
        }
    }
}
