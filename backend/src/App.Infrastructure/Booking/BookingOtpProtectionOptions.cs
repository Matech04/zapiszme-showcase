namespace App.Infrastructure.Booking;

/// <summary>
/// Konfigurowalne progi anty-abuse dla publicznego bookingu (OTP + holdy). Sekcja
/// <c>Booking:OtpProtection</c> w appsettings. Wartości domyślne dobrane tak, by przejść legalny
/// flow (1 user = 1 hold + 1–2 resendy OTP), a uciąć rotację numeru / AnonSessionId z jednego IP.
/// Liczniki żyją w IMemoryCache (single-instance MVP) — przy skalowaniu poziomym przenieść do
/// wspólnego store (Redis), inaczej realny sufit = próg × liczba instancji.
/// </summary>
public sealed class BookingOtpProtectionOptions
{
  public const string Section = "Booking:OtpProtection";

  /// <summary>
  /// [M1] Twardy cap liczby WYSŁANYCH OTP-SMS z jednego IP w ciągu godziny — niezależny od numeru
  /// telefonu. Domyka lukę: per-numer capy resetują się z każdym nowym numerem (rotacja numeru z tego
  /// samego IP), więc bez tego sufit kosztu zaczynał się dopiero na per-IP/global cap SmsApiClienta.
  /// Legalnie: 1 user = 1 numer = 1 SMS (+ ew. 1–2 resendy) → 15/h to hojny bufor (np. rodzina / biuro
  /// za jednym IP), a wciąż ścina masową rotację numerów. 0 = wyłączony.
  /// </summary>
  public int MaxOtpSmsPerIpPerHour { get; set; } = 15;

  /// <summary>
  /// [M1e] Odpowiednik <see cref="MaxOtpSmsPerIpPerHour"/> dla kanału e-mail. Bez niego salony na
  /// <c>CustomerVerificationChannel.Email</c> były odsłonięte: cap per-adres (5/h) omija rotacja
  /// adresu (catch-all <c>atak+N@moja.domena</c>), a per-IP capu dla maili w ogóle nie było — jedyną
  /// barierą zostawał limiter 30/min, co daje rząd setek potwierdzonych rezerwacji na godzinę.
  /// Luźniejszy niż SMS (mail jest tańszy, a jeden adres IP bywa współdzielony), ale skończony.
  /// 0 = wyłączony.
  /// </summary>
  public int MaxOtpEmailsPerIpPerHour { get; set; } = 30;

  /// <summary>
  /// [M4] Cap POTWIERDZONYCH rezerwacji z jednego IP w oknie godziny — ostatnia bramka, niezależna
  /// od kanału weryfikacji. Capy wysyłkowe ograniczają koszt kodów, ale nie liczbę wizyt, które
  /// realnie lądują w kalendarzu: po udanej weryfikacji <c>ReleaseHoldForIp</c> zwalnia miejsce
  /// w liczniku holdów, więc <see cref="MaxConcurrentHoldsPerIp"/> też tego nie łapie. Legalnie
  /// jedno IP to 1–2 rezerwacje (rodzina/biuro za NAT-em: kilka), więc 10/h jest hojne, a ucina
  /// zapychanie kalendarza fałszywymi wizytami. 0 = wyłączony.
  /// </summary>
  public int MaxConfirmedBookingsPerIpPerHour { get; set; } = 10;

  /// <summary>
  /// [M3] Twardy cap liczby JEDNOCZEŚNIE aktywnych holdów (AwaitingOtp) z jednego IP. Domyka slot
  /// hoarding przez rotację AnonSessionId / brak cookie (auto-anulowanie poprzedniego holdu działa
  /// tylko per-AnonSessionId). Legalny user trzyma 1 hold naraz (nowy /hold anuluje poprzedni), więc
  /// nie dobija do progu; atakujący rotujący sesję dobija. 0 = wyłączony.
  /// </summary>
  public int MaxConcurrentHoldsPerIp { get; set; } = 5;
}
