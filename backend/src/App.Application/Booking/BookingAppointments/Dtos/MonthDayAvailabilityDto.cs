namespace App.Application.Booking.BookingAppointments.Dtos;

/// <summary>
/// Liczba wolnych slotów dla pojedynczego dnia — używane przez kalendarz klienta
/// do kolorowania kafelków dat (dużo / mało / bardzo mało / brak miejsc).
/// </summary>
/// <param name="date">Data dnia.</param>
/// <param name="availableCount">Liczba wolnych slotów (0 = brak, np. zajęte albo brak grafiku).</param>
/// <param name="isWorkingDay">
/// Czy pracownik ma jakikolwiek grafik tego dnia (niezależnie od rezerwacji/przeszłości).
/// Pozwala frontowi odróżnić „miesiąc zajęty" (są dni robocze, ale wszystko wzięte) od
/// „pracownik bez grafiku" (0 dni roboczych) — w tym drugim przypadku kalendarz NIE skacze
/// automatycznie w przód szukając wolnego terminu, tylko od razu pokazuje „brak terminów".
/// </param>
public record MonthDayAvailabilityDto(
  DateOnly date,
  int availableCount,
  bool isWorkingDay
  );
