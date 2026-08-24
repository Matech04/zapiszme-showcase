using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeEmployeeServiceCustomDurationNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "custom_duration",
                table: "EmployeeServices",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // Reset istniejących snapshotów: do tej pory custom_duration był ZAWSZE wypełniany
            // (kopia Service.DurationInMinutes z chwili przypisania), przez co zmiana czasu usługi
            // nie propagowała się do nowych wizyt. Po zmianie modelu null = "dziedzicz z katalogu".
            // Czyścimy wszystkie wartości — katalog staje się źródłem prawdy. Świadome override'y
            // per-pracownik trzeba ustawić ponownie w formularzu pracownika (rzadki przypadek;
            // do tej pory i tak były nieodróżnialne od snapshotów po zmianie czasu usługi).
            migrationBuilder.Sql(@"UPDATE ""EmployeeServices"" SET custom_duration = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill NULL-i z katalogu, inaczej SET NOT NULL padnie na wierszach dziedziczących.
            migrationBuilder.Sql(@"
                UPDATE ""EmployeeServices"" es
                SET custom_duration = s.duration_in_minutes
                FROM ""Services"" s
                WHERE es.service_id = s.""Id"" AND es.custom_duration IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "custom_duration",
                table: "EmployeeServices",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
