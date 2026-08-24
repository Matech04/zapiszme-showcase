using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShiftTemplateFixedStartTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly[]>(
                name: "fixed_start_times",
                table: "ShiftTemplates",
                type: "time without time zone[]",
                nullable: false,
                defaultValue: new TimeOnly[0]);

            migrationBuilder.AddColumn<int>(
                name: "slot_generation_mode",
                table: "ShiftTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fixed_start_times",
                table: "ShiftTemplates");

            migrationBuilder.DropColumn(
                name: "slot_generation_mode",
                table: "ShiftTemplates");
        }
    }
}
