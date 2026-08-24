using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultiWorkRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_time",
                table: "ShiftTemplates");

            migrationBuilder.DropColumn(
                name: "start_time",
                table: "ShiftTemplates");

            migrationBuilder.DropColumn(
                name: "end_time",
                table: "ScheduleOverrides");

            migrationBuilder.DropColumn(
                name: "start_time",
                table: "ScheduleOverrides");

            migrationBuilder.DropColumn(
                name: "end_time",
                table: "ScheduleDays");

            migrationBuilder.DropColumn(
                name: "start_time",
                table: "ScheduleDays");

            migrationBuilder.CreateTable(
                name: "ScheduleDayWorkRanges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    schedule_day_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleDayWorkRanges", x => x.id);
                    table.ForeignKey(
                        name: "FK_ScheduleDayWorkRanges_ScheduleDays_schedule_day_id",
                        column: x => x.schedule_day_id,
                        principalTable: "ScheduleDays",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleOverrideWorkRanges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    schedule_override_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleOverrideWorkRanges", x => x.id);
                    table.ForeignKey(
                        name: "FK_ScheduleOverrideWorkRanges_ScheduleOverrides_schedule_overr~",
                        column: x => x.schedule_override_id,
                        principalTable: "ScheduleOverrides",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTemplateWorkRanges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    shift_template_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTemplateWorkRanges", x => x.id);
                    table.ForeignKey(
                        name: "FK_ShiftTemplateWorkRanges_ShiftTemplates_shift_template_id",
                        column: x => x.shift_template_id,
                        principalTable: "ShiftTemplates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleDayWorkRanges_schedule_day_id",
                table: "ScheduleDayWorkRanges",
                column: "schedule_day_id");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOverrideWorkRanges_schedule_override_id",
                table: "ScheduleOverrideWorkRanges",
                column: "schedule_override_id");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplateWorkRanges_shift_template_id",
                table: "ShiftTemplateWorkRanges",
                column: "shift_template_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleDayWorkRanges");

            migrationBuilder.DropTable(
                name: "ScheduleOverrideWorkRanges");

            migrationBuilder.DropTable(
                name: "ShiftTemplateWorkRanges");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                table: "ShiftTemplates",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                table: "ShiftTemplates",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                table: "ScheduleOverrides",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                table: "ScheduleOverrides",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                table: "ScheduleDays",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                table: "ScheduleDays",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));
        }
    }
}
