namespace App.Application.Notifications;

/// <summary>
/// Strukturalne dane powiadomienia. Handler zdarzenia wypełnia pola istotne dla danego typu,
/// a kanały renderują z nich treść (e-mail → HTML, SMS/in-app → krótki tekst). Wszystkie pola
/// poza <see cref="SalonName"/> są opcjonalne — jeden rekord obsługuje wszystkie typy powiadomień.
/// </summary>
public record NotificationPayload(
  string SalonName,
  string? CustomerName = null,
  string? StaffName = null,
  string? ServiceName = null,
  DateOnly? Date = null,
  TimeOnly? StartTime = null,
  DateOnly? OldDate = null,
  TimeOnly? OldStartTime = null,
  // Pola kontaktowe klienta — wypełniane dla powiadomień DO salonu, by pracownik widział, kto
  // zarezerwował. CustomerFullName jest puste, gdy klient nie podał imienia (inaczej niż
  // CustomerName, które ma fallback „Klient" dla powitań kierowanych DO klienta).
  string? CustomerFullName = null,
  string? CustomerPhone = null,
  string? CustomerEmail = null,
  // Link akcji (np. do zapłaty zadatku) + jego tekstowy opis kwoty — używane przez typy
  // powiadomień, które niosą wezwanie do działania (DepositLinkToCustomer).
  string? ActionUrl = null,
  string? AmountText = null);
