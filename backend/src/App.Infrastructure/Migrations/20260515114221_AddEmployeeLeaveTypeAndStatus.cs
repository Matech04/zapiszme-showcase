using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeLeaveTypeAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Domyślne wartości to nominały enumów (Approved=1, Vacation=1) — historyczne wiersze
            // dotąd nie miały tych pól w DB i w domenie powstawały z tymi defaultami.
            migrationBuilder.AddColumn<int>(
                name: "AbsenceStatus",
                table: "EmployeeLeaves",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "AbsenceType",
                table: "EmployeeLeaves",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbsenceStatus",
                table: "EmployeeLeaves");

            migrationBuilder.DropColumn(
                name: "AbsenceType",
                table: "EmployeeLeaves");
        }
    }
}
