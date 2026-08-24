using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Application.Appointments.Dtos;

public record AppointmentDto(
  Guid Id,
  Guid EmployeeId,
  Guid ServiceId,
  Guid? CustomerId,
  bool IsGuest,
  string EmployeeFirstName,
  string EmployeeLastName,
  string CustomerFirstName,
  string CustomerLastName,
  string CustomerPhoneNumber,
  string? CustomerInstagramNick,
  string ServiceName,
  DateOnly Date,
  TimeOnly StartTime,
  TimeOnly EndTime,
  AppointmentStatus Status,
  Money TotalPrice,
  string AppointmentNotes,
  string PaymentStatus,
  Money? DepositAmount,
  DateTime? PaidAtUtc,
  // Utrwalony link do zadatku (krótki URL) + jego wygaśnięcie. Wystawiamy je, aby panel mógł
  // pokazać raz wygenerowany link po odświeżeniu strony / powrocie do wizyty (a nie tylko
  // w pamięci komponentu). Null gdy linku jeszcze nie wygenerowano. PaymentLinkExpired liczone
  // serwerowo, bo zależy od „teraz" — frontend nie ma autorytatywnego zegara.
  string? PaymentLinkUrl,
  DateTime? PaymentLinkExpiresAtUtc,
  bool PaymentLinkExpired,
  // Kiedy i czym link POTWIERDZONO jako wysłany do klienta. Null = nie wysłano (lub wysyłka padła);
  // panel pokazuje wtedy „Wyślij", a nie „Wysłano ponownie". Zerowane przy generowaniu nowego linku.
  DateTime? DepositLinkSentAtUtc,
  string? DepositLinkSentChannel,
  // Ile razy wygenerowano link (aktualny włącznie) i ile POPRZEDNICH wygasło bez opłaty. Panel
  // pokazuje z tego „3. link" + ostrzeżenie o nieudanych próbach. Nadpisanie wciąż ważnego linku
  // nie jest wygaśnięciem, więc te dwie liczby nie muszą się różnić o 1.
  int DepositLinkAttempts,
  int ExpiredDepositLinkCount,
  // Czy salon może w ogóle pobierać zadatki (zadatki włączone + konto Stripe gotowe). Steruje
  // widocznością akcji „Generuj zadatek" w panelu — bez tego przycisk pokazywał się zawsze.
  bool DepositsAvailable,
  Money? FinalPrice = null,
  // Pozycje combo (wszystkie usługi wizyty, w kolejności). Dla wizyt single-service = 1 element,
  // którego ServiceId == ServiceId (primary). Panel pokazuje pełną listę usług.
  IReadOnlyList<AppointmentServiceItemDto>? Services = null,
  // Zdjęcia inspiracji dołączone przez klientkę przy publicznej rezerwacji — pracownik widzi
  // miniatury w panelu szczegółów wizyty (klik → podgląd). Pusta lista gdy brak.
  IReadOnlyList<AppointmentInspirationImageDto>? InspirationImages = null,
  // Niestandardowy czas trwania wizyty (minuty) ustawiony przez personel — null = czas standardowy.
  // Panel pokazuje badge „czas własny" i prefill kontrolki. Efektywny czas = EndTime − StartTime.
  int? CustomDurationMinutes = null
  );

/// <summary>Pozycja usługi w wizycie-combo (nazwa dołączona na żywo, czas/cena ze snapshotu).</summary>
public record AppointmentServiceItemDto(
  Guid ServiceId,
  string ServiceName,
  int DurationMinutes,
  Money Price,
  int Position);

/// <summary>Zdjęcie inspiracji wizyty — publiczne URL-e (miniatura do siatki, główny do podglądu).</summary>
public record AppointmentInspirationImageDto(
  string Url,
  string ThumbnailUrl);
