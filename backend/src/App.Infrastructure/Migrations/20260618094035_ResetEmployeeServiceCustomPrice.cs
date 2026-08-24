using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResetEmployeeServiceCustomPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Domknięcie luki ceny: analogicznie do custom_duration (migracja MakeEmployeeServiceCustomDurationNullable),
            // do tej pory CustomPrice był ZAWSZE wypełniany snapshotem Service.Price z chwili przypisania, przez co
            // zmiana ceny usługi nie propagowała się do nowych wizyt. Po zmianie modelu null = „dziedzicz z katalogu".
            // Czyścimy wszystkie istniejące ceny per-pracownik (kolumny owned CustomPrice) — katalog = źródło prawdy.
            // Świadome ceny per-pracownik trzeba ustawić ponownie w formularzu pracownika (rzadki przypadek; do tej
            // pory i tak nieodróżnialne od snapshotów po zmianie ceny usługi).
            migrationBuilder.Sql(@"UPDATE ""EmployeeServices"" SET price_amount = NULL, price_currency = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nieodwracalne: oryginalne snapshoty ceny zostały skasowane. Brak backfillu — po Down ceny pozostają
            // NULL (dziedziczą z katalogu), co jest bezpiecznym stanem (CalculateTotalPrice użyje Service.Price).
        }
    }
}
