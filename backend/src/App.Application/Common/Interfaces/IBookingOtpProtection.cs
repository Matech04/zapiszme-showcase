namespace App.Application.Common.Interfaces;

/// <summary>
/// Ochrona publicznych endpointów OTP: cooldown między żądaniami kodu, limit prób weryfikacji, limity per IP.
/// </summary>
public interface IBookingOtpProtection
{
  void AssertCanRequestOtp(Guid appointmentId, string? clientIp);

  /// <summary>Wywołaj po udanym zapisie nowego OTP (cooldown, reset błędów, licznik IP).</summary>
  void RegisterOtpRequestSucceeded(Guid appointmentId, string? clientIp);

  /// <summary>
  /// Per-target-email throttle (anti email-bomb): max N maili z OTP na jeden adres docelowy/godzinę,
  /// niezależnie od liczby appointmentów / tenantów / IP. Chroni ofiarę przed kosztownym spamem
  /// (ACS Email płaci się za wysłany mail).
  /// </summary>
  void AssertCanSendOtpToEmail(string email);

  /// <summary>Wywołaj PO udanej wysyłce — bump licznika "ile do tego maila wysłaliśmy w tej godzinie".</summary>
  void RegisterOtpSentToEmail(string email);

  /// <summary>
  /// Per-target-phone throttle (anti SMS-bomb): max N SMS-ów z OTP na jeden numer docelowy/godzinę,
  /// niezależnie od liczby appointmentów / tenantów / IP. SMS kosztuje (smsapi.pl płatne), więc
  /// chroni ofiarę i budżet przed kosztownym spamem.
  /// </summary>
  void AssertCanSendOtpToPhone(string phoneE164);

  /// <summary>Wywołaj PO udanej wysyłce SMS — bump licznika "ile na ten numer wysłaliśmy w tej godzinie".</summary>
  void RegisterOtpSentToPhone(string phoneE164);

  /// <summary>
  /// [M1] Anti-rotacja-numeru: cap WYSŁANYCH OTP-SMS z jednego IP/godzinę, NIEZALEŻNY od numeru.
  /// Domyka lukę, w której per-numer capy resetowały się z każdym nowym numerem (rotacja numeru z
  /// jednego IP), a twardy sufit zaczynał się dopiero na per-IP/global cap SmsApiClienta.
  /// Wywołaj PRZED wysyłką SMS-a. Brak/pusty IP → no-op (tło bez HttpContext).
  /// </summary>
  void AssertCanSendOtpSmsFromIp(string? clientIp);

  /// <summary>Wywołaj PO udanej wysyłce OTP-SMS — bump per-IP/godzinę licznika wysłanych SMS.</summary>
  void RegisterOtpSmsSentFromIp(string? clientIp);

  /// <summary>
  /// [M1e] Anti-rotacja-adresu: cap WYSŁANYCH OTP-maili z jednego IP/godzinę, NIEZALEŻNY od adresu.
  /// Lustrzane odbicie <see cref="AssertCanSendOtpSmsFromIp"/> — bez niego salony na kanale e-mail
  /// miały wyłącznie cap per-adres, który omija catch-all (<c>atak+N@moja.domena</c>).
  /// Wywołaj PRZED wysyłką maila. Brak/pusty IP → no-op (tło bez HttpContext).
  /// </summary>
  void AssertCanSendOtpEmailFromIp(string? clientIp);

  /// <summary>Wywołaj PO udanej wysyłce OTP-maila — bump per-IP/godzinę licznika wysłanych maili.</summary>
  void RegisterOtpEmailSentFromIp(string? clientIp);

  /// <summary>
  /// [M4] Atomowo rezerwuje „slot" POTWIERDZONEJ rezerwacji dla danego IP (cap per IP/godzinę).
  /// Ostatnia bramka przed wpisaniem wizyty do kalendarza: capy wysyłkowe ograniczają koszt kodów,
  /// ale nie liczbę realnie powstałych wizyt, a <c>ReleaseHoldForIp</c> zwalnia miejsce w liczniku
  /// holdów zaraz po weryfikacji. Sprawdzenie i inkrement są atomowe (brak TOCTOU przy równoległych
  /// żądaniach). Rzuca <c>RateLimitExceededException</c> po przekroczeniu progu; przy przekroczeniu
  /// slot NIE jest rezerwowany. Brak/pusty IP → no-op. Wołaj PO udanej weryfikacji OTP, a PRZED
  /// zapisem potwierdzenia.
  /// </summary>
  void AssertCanConfirmBookingFromIp(string? clientIp);

  /// <summary>
  /// [M3] Anti slot-hoarding: rejestruje utworzenie nowego holdu (AwaitingOtp) dla danego IP i
  /// asercję twardego capu liczby JEDNOCZEŚNIE aktywnych holdów per IP. Rzuca po przekroczeniu progu.
  /// Brak/pusty IP → no-op. Wywołaj PRZY tworzeniu holdu (po anulowaniu poprzednich tej sesji).
  /// </summary>
  void RegisterHoldCreatedForIp(string? clientIp);

  /// <summary>
  /// [M3] Zwalnia jeden aktywny hold z licznika per-IP (np. gdy poprzedni hold tej sesji jest
  /// anulowany). Dzięki temu legalny user (1 hold naraz) nie akumuluje się do progu. No-op gdy 0/brak IP.
  /// </summary>
  void ReleaseHoldForIp(string? clientIp);

  /// <summary>Rejestruje próbę weryfikacji OTP (per IP); przy przekroczeniu limitu — wyjątek 429.</summary>
  void RecordVerifyOtpAttempt(string? clientIp);

  bool IsVerificationBlocked(Guid appointmentId);

  /// <summary>Zwraca liczbę kolejnych nieudanych prób po tej próbie (1–3).</summary>
  int RegisterFailedVerificationAttempt(Guid appointmentId);

  void ClearVerificationAttempts(Guid appointmentId);

  /// <summary>
  /// Per-target (email/telefon) blokada nieudanych weryfikacji — niezależna od appointmentId.
  /// Domyka amortyzację brute-force: rotacja holdu (nowy appointmentId) resetuje per-appointment
  /// budżet 3 prób, ale per-target licznik trzyma się dla kontaktu ofiary.
  /// </summary>
  bool IsTargetVerificationBlocked(string target);

  /// <summary>Rejestruje nieudaną weryfikację dla celu (email/telefon); zwraca łączną liczbę w oknie.</summary>
  int RegisterFailedVerificationForTarget(string target);

  /// <summary>Czyści per-target licznik po udanej weryfikacji.</summary>
  void ClearTargetVerificationAttempts(string target);

  /// <summary>
  /// [Skip-OTP] Atomowo rezerwuje „slot" potwierdzenia <c>confirm-with-session</c> i egzekwuje limit
  /// per sesja zweryfikowanego kontaktu ORAZ per IP/godzinę. Każde potwierdzenie wyzwala płatny SMS
  /// potwierdzający (poza capami OTP), więc bez tego jedna ważna sesja (koszt: 1 OTP) mogłaby w pętli
  /// hold→confirm drenować budżet SMS salonu. Sprawdzenie i inkrement są atomowe (brak TOCTOU przy
  /// równoległych żądaniach). Rzuca <c>RateLimitExceededException</c> po przekroczeniu progu; przy
  /// przekroczeniu slot NIE jest rezerwowany. Pusty IP → tylko cap per-sesja. Wołaj tuż przed
  /// potwierdzeniem (po pozostałych bramkach), bo rezerwacja jest skuteczna nawet gdy dalszy zapis padnie.
  /// </summary>
  void AssertCanConfirmWithSession(Guid sessionToken, string? clientIp);

  /// <summary>
  /// Atomowo rezerwuje „slot" przełożenia self-service i egzekwuje limit per sesja/godzinę, per
  /// wizyta/dobę ORAZ per IP/godzinę. Każde przełożenie publikuje płatny SMS „zmieniono termin", więc
  /// bez tego jedna ważna sesja (koszt: 1 OTP) mogłaby w pętli reschedule (A→B→A→…) drenować budżet SMS
  /// salonu — endpoint jest anonimowy i tylko rate-limitowany per-IP. Sprawdzenie i inkrement są atomowe
  /// (brak TOCTOU). Rzuca <c>RateLimitExceededException</c> po przekroczeniu progu; wołaj PRZED samym
  /// przełożeniem (po walidacji sesji i dostępu). Pusty IP → tylko capy per-sesja i per-wizyta.
  /// </summary>
  void AssertCanReschedule(Guid sessionToken, Guid appointmentId, string? clientIp);
}
