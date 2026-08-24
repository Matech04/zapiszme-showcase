using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PurgeNeverConfirmedCanceledHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Jednorazowe czyszczenie backlogu: wizyty Canceled z niepustą dzierżawą to NIGDY
            // niepotwierdzone holdy publicznej rezerwacji, które stara ścieżka anti-abuse zamieniała
            // na Canceled zamiast usuwać (zob. RemovePreviousHoldsForAnonSessionAsync). Potwierdzona
            // wizyta ZAWSZE ma lease=null (czyszczone w VerifyOtp), więc realne anulowania nie są
            // ruszane. Bezpieczny dyskryminator: Status='Canceled' AND lease_reservation_token IS NOT NULL.
            migrationBuilder.Sql(
                "DELETE FROM \"Appointments\" WHERE \"Status\" = 'Canceled' AND lease_reservation_token IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Brak rollbacku — usuniętych śmieciowych holdów nie odtwarzamy (nigdy nie były realnymi wizytami).
        }
    }
}
