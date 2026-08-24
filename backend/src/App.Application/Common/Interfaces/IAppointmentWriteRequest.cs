namespace App.Application.Common.Interfaces;

/// <summary>
/// Marker dla komend zapisu wizyt po stronie personelu (tworzenie/przełożenie/zmiana usług/status/
/// swap/usunięcie/notatki/cena). Gdy salon wstrzymał rezerwacje (<c>Tenant.BookingPaused</c>),
/// <c>BookingPauseBehavior</c> blokuje takie komendy (<c>BookingPausedException</c>) — enforcement
/// po stronie serwera, spójny z ukryciem kalendarza w panelu. Globalny tryb serwisowy platformy
/// blokuje wszystkie zapisy osobno (middleware).
/// </summary>
public interface IAppointmentWriteRequest;
