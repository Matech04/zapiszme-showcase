namespace App.Application.Booking.BookingAppointments.Dtos;

/// <summary>
/// Dostępność całego miesiąca w publicznym bookingu wraz z informacją, czy miesiąc jest w ogóle
/// otwarty na zapisy.
///
/// Rozdzielenie „zamknięte" od „brak wolnych terminów" jest tu celowe: pusta siatka bez wyjaśnienia
/// czyta się dla klienta jak „salon nie pracuje", a nie „zapisy jeszcze nie ruszyły" — i kosztuje
/// rezerwację, która by się odbyła.
/// </summary>
/// <param name="isClosed">
/// Czy salon świadomie zamknął ten miesiąc dla klientów (jawna publikacja miesiąca).
/// <c>false</c> nie oznacza, że są wolne terminy — tylko że miesiąc nie jest zablokowany.
/// </param>
/// <param name="opensOn">
/// Dzień, w którym miesiąc otworzy się sam. <c>null</c> przy <paramref name="isClosed"/> = zamknięty
/// bezterminowo (salon otworzy ręcznie) → front nie powinien obiecywać konkretnej daty.
/// </param>
/// <param name="days">Dostępność per dzień. Przy zamkniętym miesiącu wszystkie dni mają 0 slotów.</param>
public record BookingMonthAvailabilityDto(
  bool isClosed,
  DateOnly? opensOn,
  IReadOnlyList<MonthDayAvailabilityDto> days
  );
