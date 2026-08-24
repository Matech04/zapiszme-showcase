using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeFixedStartTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly[]>(
                name: "fixed_start_times",
                table: "ScheduleOverrides",
                type: "time without time zone[]",
                nullable: false,
                defaultValue: new TimeOnly[0]);

            migrationBuilder.AddColumn<TimeOnly[]>(
                name: "fixed_start_times",
                table: "ScheduleDays",
                type: "time without time zone[]",
                nullable: false,
                defaultValue: new TimeOnly[0]);

            migrationBuilder.AddColumn<int>(
                name: "slot_generation_mode",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fixed_start_times",
                table: "ScheduleOverrides");

            migrationBuilder.DropColumn(
                name: "fixed_start_times",
                table: "ScheduleDays");

            migrationBuilder.DropColumn(
                name: "slot_generation_mode",
                table: "Employees");
        }
    }
}
