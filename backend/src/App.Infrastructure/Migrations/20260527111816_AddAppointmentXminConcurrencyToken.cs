using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin jest systemową kolumną PostgreSQL — nie wymaga CREATE COLUMN.
            // Migracja istnieje tylko po to, żeby zaktualizować snapshot modelu EF Core
            // (dodanie IsConcurrencyToken do właściwości xmin w konfiguracji Appointment).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
