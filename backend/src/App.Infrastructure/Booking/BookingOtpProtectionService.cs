using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Booking;

public sealed class BookingOtpProtectionService : IBookingOtpProtection
{
  public const int CooldownSeconds = 60;
  public const int MaxFailedVerifications = 3;
  /// <summary>
  /// Per-target (email/telefon) próg nieudanych weryfikacji w oknie. Niezależny od appointmentId,
  /// więc rotacja holdu (nowy appointmentId = świeże 3 próby per-appointment) nie resetuje budżetu
  /// brute-force na konkretny kontakt. 10 to bufor na legitne pomyłki przy kilku rezerwacjach.
  /// </summary>
  public const int MaxFailedVerificationsPerTarget = 10;
  private const int MaxRequestOtpPerIpPerMinute = 30;
  // 6-cyfrowy kod = 10^6 możliwości. 60/min było za hojne (brute-force w oknie ważności 10 min).
  // 10/min nadal mieści legitne pomyłki, a drastycznie ścina przestrzeń zgadywania per IP.
  private const int MaxVerifyOtpPerIpPerMinute = 10;
  /// <summary>
  /// Anti email-bomb cap: ile maili z OTP wolno wysłać do JEDNEGO adresu w ciągu godziny.
  /// Wartość trzyma się legitnym przypadkom (klient może 2-3x poprosić o resend) i odcina
  /// kosztowy atak (ACS płaci się od maila). 5 to kompromis: jeden klient zwykle nie potrzebuje
  /// więcej niż 1-2 OTP, więc 5 to bufor na zgubione maile + retries.
  /// </summary>
  private const int MaxOtpPerEmailPerHour = 5;
  /// <summary>
  /// Anti SMS-bomb cap: ile SMS-ów z OTP wolno wysłać na JEDEN numer w ciągu godziny.
  /// Legalny klient potrzebuje 1 kodu (+ ew. 1-2 resendy przy zgubionym SMS) → 3 w pełni pokrywa
  /// realny przypadek; powyżej tego brak uzasadnienia. SMS jest droższy od maila, więc ciaśniej.
  /// </summary>
  private const int MaxOtpPerPhonePerHour = 3;
  /// <summary>
  /// Anti SMS-bomb cap dobowy na JEDEN numer. Realny dzień klienta (rezerwacja + ew. zmiana terminu
  /// + anulowanie przez self-service) to ~2-3 SMS. 5/dobę to hojny sufit; >5 = niemal pewny abuse.
  /// Domyka lukę: bez tego per-godzinowy cap pozwalał 3×24=72 SMS/dobę na jeden numer.
  /// </summary>
  private const int MaxOtpPerPhonePerDay = 5;
  /// <summary>
  /// Maks. liczba wysyłek OTP (dowolnym kanałem) na JEDEN hold/rezerwację przez całe jej życie.
  /// Legalnie: 1 + ew. resendy. Sprawia, że jeden hold nie jest narzędziem do rozsiewania kodów
  /// na wiele numerów (cooldown jest per-hold, więc bez tego capa jeden hold = wielokrotny send).
  /// </summary>
  private const int MaxOtpSendsPerAppointment = 3;
  /// <summary>
  /// [Skip-OTP] Maks. liczba potwierdzeń confirm-with-session na JEDNĄ sesję w oknie godziny. Każde
  /// potwierdzenie wyzwala płatny SMS potwierdzający (poza capami OTP). Sesja służy wygodzie jednego
  /// klienta (1–2 rezerwacje), więc 5/h pokrywa realny przypadek, a odcina pętlę drenażu budżetu SMS.
  /// </summary>
  private const int MaxConfirmWithSessionPerSessionPerHour = 5;
  /// <summary>[Skip-OTP] Twardszy sufit per-IP/godzinę (wiele sesji z jednego adresu).</summary>
  private const int MaxConfirmWithSessionPerIpPerHour = 10;

  /// <summary>
  /// Maks. liczba przełożeń self-service na JEDNĄ sesję w oknie godziny. Każde przełożenie publikuje
  /// płatny SMS „zmieniono termin"; legalny klient przekłada 1–2 razy, więc 5/h pokrywa realny przypadek,
  /// a ucina pętlę reschedule drenującą budżet SMS salonu.
  /// </summary>
  private const int MaxRescheduleSelfServicePerSessionPerHour = 5;
  /// <summary>Twardy cap przełożeń tej samej wizyty w ciągu doby — domyka odbijanie A→B→A→… na jednej wizycie.</summary>
  private const int MaxRescheduleSelfServicePerAppointmentPerDay = 5;
  /// <summary>Twardszy sufit per-IP/godzinę (wiele sesji z jednego adresu).</summary>
  private const int MaxRescheduleSelfServicePerIpPerHour = 10;

  private readonly IMemoryCache _cache;
  private readonly TimeProvider _timeProvider;
  private readonly BookingOtpProtectionOptions _options;

  public BookingOtpProtectionService(
      IMemoryCache cache,
      TimeProvider timeProvider,
      IOptions<BookingOtpProtectionOptions> options)
  {
    _cache = cache;
    _timeProvider = timeProvider;
    _options = options.Value;
  }

  public void AssertCanRequestOtp(Guid appointmentId, string? clientIp)
  {
    // Twardy limit liczby wysyłek na jeden hold (lifetime) — jeden hold nie może rozsiewać kodów.
    if (_cache.TryGetValue(ApptSendsKey(appointmentId), out int sends) && sends >= MaxOtpSendsPerAppointment)
    {
      throw new RateLimitExceededException(
          "Przekroczono limit wysyłek kodu dla tej rezerwacji. Rozpocznij rezerwację od nowa.",
          retryAfterSeconds: 0);
    }

    var cdKey = CooldownKey(appointmentId);
    if (_cache.TryGetValue(cdKey, out DateTimeOffset until))
    {
      var secs = (int)Math.Ceiling((until - _timeProvider.GetUtcNow()).TotalSeconds);
      if (secs > 0)
      {
        throw new RateLimitExceededException(
            $"Możesz poprosić o nowy kod za {secs} s.",
            secs);
      }
    }

    AssertIpWindow("otp:rq", clientIp, MaxRequestOtpPerIpPerMinute);
  }

  public void RegisterOtpRequestSucceeded(Guid appointmentId, string? clientIp)
  {
    _cache.Set(
        CooldownKey(appointmentId),
        _timeProvider.GetUtcNow().AddSeconds(CooldownSeconds),
        new MemoryCacheEntryOptions
        {
          AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CooldownSeconds + 10)
        });

    BumpIpWindow("otp:rq", clientIp);

    // Inkrementujemy licznik wysyłek per-hold (lifetime). TTL z zapasem ponad życie holdu.
    var sends = _cache.TryGetValue(ApptSendsKey(appointmentId), out int current) ? current : 0;
    _cache.Set(
        ApptSendsKey(appointmentId),
        sends + 1,
        new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });

    // ŚWIADOMIE NIE czyścimy licznika nieudanych weryfikacji przy ponownym wysłaniu kodu.
    // Wcześniej reset pozwalał obejść lockout: 3 błędne próby → poproś o nowy kod (reset) →
    // kolejne 3 → itd. Teraz lockout (MaxFailedVerifications) trzyma się dla całego życia
    // appointmentu; legitny user po 3 pomyłkach zaczyna nową rezerwację (nowy appointmentId).
  }

  public void RecordVerifyOtpAttempt(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp))
    {
      return;
    }

    var ipKey = IpMinuteKey("otp:vf", clientIp);
    var n = 0;
    if (_cache.TryGetValue(ipKey, out int current))
    {
      n = current;
    }

    n++;
    if (n > MaxVerifyOtpPerIpPerMinute)
    {
      throw new RateLimitExceededException(
          "Zbyt wiele prób weryfikacji kodu z tego adresu. Odczekaj minutę i spróbuj ponownie.",
          60);
    }

    _cache.Set(
        ipKey,
        n,
        new MemoryCacheEntryOptions { AbsoluteExpiration = _timeProvider.GetUtcNow().AddMinutes(2) });
  }

  public bool IsVerificationBlocked(Guid appointmentId)
  {
    return _cache.TryGetValue(FailKey(appointmentId), out int fails) && fails >= MaxFailedVerifications;
  }

  public int RegisterFailedVerificationAttempt(Guid appointmentId)
  {
    var key = FailKey(appointmentId);
    var fails = 0;
    if (_cache.TryGetValue(key, out int current))
    {
      fails = current;
    }

    fails++;
    _cache.Set(
        key,
        fails,
        new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) });
    return fails;
  }

  public void ClearVerificationAttempts(Guid appointmentId)
  {
    _cache.Remove(FailKey(appointmentId));
  }

  public bool IsTargetVerificationBlocked(string target)
  {
    if (string.IsNullOrWhiteSpace(target))
    {
      return false;
    }

    return _cache.TryGetValue(TargetFailKey(target), out int fails) && fails >= MaxFailedVerificationsPerTarget;
  }

  public int RegisterFailedVerificationForTarget(string target)
  {
    if (string.IsNullOrWhiteSpace(target))
    {
      return 0;
    }

    var key = TargetFailKey(target);
    var fails = _cache.TryGetValue(key, out int current) ? current : 0;
    fails++;
    _cache.Set(
        key,
        fails,
        new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) });
    return fails;
  }

  public void ClearTargetVerificationAttempts(string target)
  {
    if (!string.IsNullOrWhiteSpace(target))
    {
      _cache.Remove(TargetFailKey(target));
    }
  }

  private static string TargetFailKey(string target) => $"otp:vf-fail:{target.Trim().ToLowerInvariant()}";

  private static string CooldownKey(Guid appointmentId) => $"otp:cd:{appointmentId:D}";

  private static string FailKey(Guid appointmentId) => $"otp:fail:{appointmentId:D}";

  private static string ApptSendsKey(Guid appointmentId) => $"otp:appt-sends:{appointmentId:D}";

  private static string EmailHourKey(string email) =>
      $"otp:email-hour:{email.Trim().ToLowerInvariant()}";

  private static string PhoneHourKey(string phone) =>
      $"otp:phone-hour:{phone.Trim()}";

  private string PhoneDayKey(string phone) =>
      $"otp:phone-day:{phone.Trim()}:{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd}";

  public void AssertCanSendOtpToEmail(string email)
  {
    if (string.IsNullOrWhiteSpace(email))
    {
      return;
    }

    var key = EmailHourKey(email);
    if (_cache.TryGetValue(key, out int n) && n >= MaxOtpPerEmailPerHour)
    {
      // 3600s (godzina) — retry-after; klient widzi czas oczekiwania.
      throw new RateLimitExceededException(
          "Wysłaliśmy już maksymalną liczbę kodów na ten adres email. Spróbuj ponownie za godzinę.",
          retryAfterSeconds: 3600);
    }
  }

  public void RegisterOtpSentToEmail(string email)
  {
    if (string.IsNullOrWhiteSpace(email))
    {
      return;
    }

    var key = EmailHourKey(email);
    var n = _cache.TryGetValue(key, out int current) ? current : 0;
    // AbsoluteExpirationRelativeToNow przesuwa wygaśnięcie z każdym kolejnym Set —
    // ale licznik jest zachowany, więc atakujący nie obejdzie tego przez wolne tempo:
    // 5. mail nadal blokuje, dopóki klucz żyje. Po godzinie ciszy klucz wygasa naturalnie.
    _cache.Set(
        key,
        n + 1,
        new MemoryCacheEntryOptions
        {
          AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        });
  }

  public void AssertCanSendOtpToPhone(string phoneE164)
  {
    if (string.IsNullOrWhiteSpace(phoneE164))
    {
      return;
    }

    var key = PhoneHourKey(phoneE164);
    if (_cache.TryGetValue(key, out int n) && n >= MaxOtpPerPhonePerHour)
    {
      throw new RateLimitExceededException(
          "Wysłaliśmy już maksymalną liczbę kodów na ten numer telefonu. Spróbuj ponownie za godzinę.",
          retryAfterSeconds: 3600);
    }

    var dayKey = PhoneDayKey(phoneE164);
    if (_cache.TryGetValue(dayKey, out int d) && d >= MaxOtpPerPhonePerDay)
    {
      throw new RateLimitExceededException(
          "Osiągnięto dzienny limit kodów na ten numer telefonu. Spróbuj ponownie jutro.",
          retryAfterSeconds: 3600);
    }
  }

  // ── [Skip-OTP] Cap potwierdzeń bez OTP (confirm-with-session) ─────────────────────────────────
  // Potwierdzenie holdu sesją wyzwala płatny SMS potwierdzający, NIE objęty capami OTP. Bez tego jedna
  // ważna sesja (koszt: 1 OTP) drenowałaby miesięczny budżet SMS salonu pętlą hold→confirm.
  //
  // Check i inkrement są w JEDNEJ sekcji krytycznej (lock) — inaczej równoległe potwierdzenia tą samą
  // sesją czytałyby licznik < cap zanim którekolwiek go podbije (TOCTOU) i przekraczały limit. Serwis
  // jest singletonem, a IMemoryCache proces-global, więc lock instancyjny serializuje rezerwacje slotu.
  private readonly object _confirmCapLock = new();

  public void AssertCanConfirmWithSession(Guid sessionToken, string? clientIp)
  {
    lock (_confirmCapLock)
    {
      var sKey = ConfirmSessionKey(sessionToken);
      var sCount = _cache.TryGetValue(sKey, out int sc) ? sc : 0;
      if (sCount >= MaxConfirmWithSessionPerSessionPerHour)
      {
        throw new RateLimitExceededException(
            "Zbyt wiele rezerwacji potwierdzonych tą sesją. Spróbuj ponownie za godzinę lub potwierdź kodem.",
            retryAfterSeconds: 3600);
      }

      string? ipKey = null;
      var ipCount = 0;
      if (!string.IsNullOrWhiteSpace(clientIp))
      {
        ipKey = ConfirmIpKey(clientIp!);
        ipCount = _cache.TryGetValue(ipKey, out int ic) ? ic : 0;
        if (ipCount >= MaxConfirmWithSessionPerIpPerHour)
        {
          throw new RateLimitExceededException(
              "Zbyt wiele rezerwacji z tego adresu. Spróbuj ponownie za godzinę.",
              retryAfterSeconds: 3600);
        }
      }

      // Rezerwacja slotu w tej samej sekcji krytycznej co sprawdzenie — eliminuje TOCTOU.
      _cache.Set(sKey, sCount + 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
      if (ipKey is not null)
      {
        _cache.Set(ipKey, ipCount + 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
      }
    }
  }

  private static string ConfirmSessionKey(Guid sessionToken) => $"confirm:sess:{sessionToken:D}";
  private static string ConfirmIpKey(string ip) => $"confirm:ip:{ip}";

  private readonly object _rescheduleCapLock = new();

  public void AssertCanReschedule(Guid sessionToken, Guid appointmentId, string? clientIp)
  {
    lock (_rescheduleCapLock)
    {
      var sKey = RescheduleSessionKey(sessionToken);
      var sCount = _cache.TryGetValue(sKey, out int sc) ? sc : 0;
      if (sCount >= MaxRescheduleSelfServicePerSessionPerHour)
      {
        throw new RateLimitExceededException(
            "Zbyt wiele zmian terminu tą sesją. Spróbuj ponownie za godzinę.",
            retryAfterSeconds: 3600);
      }

      var aKey = RescheduleAppointmentKey(appointmentId);
      var aCount = _cache.TryGetValue(aKey, out int ac) ? ac : 0;
      if (aCount >= MaxRescheduleSelfServicePerAppointmentPerDay)
      {
        throw new RateLimitExceededException(
            "Tę wizytę przekładano dziś zbyt wiele razy. Spróbuj ponownie jutro.",
            retryAfterSeconds: 3600);
      }

      string? ipKey = null;
      var ipCount = 0;
      if (!string.IsNullOrWhiteSpace(clientIp))
      {
        ipKey = RescheduleIpKey(clientIp!);
        ipCount = _cache.TryGetValue(ipKey, out int ic) ? ic : 0;
        if (ipCount >= MaxRescheduleSelfServicePerIpPerHour)
        {
          throw new RateLimitExceededException(
              "Zbyt wiele zmian terminu z tego adresu. Spróbuj ponownie za godzinę.",
              retryAfterSeconds: 3600);
        }
      }

      // Rezerwacja slotów w tej samej sekcji krytycznej co sprawdzenie — eliminuje TOCTOU.
      _cache.Set(sKey, sCount + 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
      _cache.Set(aKey, aCount + 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });
      if (ipKey is not null)
      {
        _cache.Set(ipKey, ipCount + 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
      }
    }
  }

  private static string RescheduleSessionKey(Guid sessionToken) => $"resched:sess:{sessionToken:D}";
  private static string RescheduleAppointmentKey(Guid appointmentId) => $"resched:appt:{appointmentId:D}";
  private static string RescheduleIpKey(string ip) => $"resched:ip:{ip}";

  public void RegisterOtpSentToPhone(string phoneE164)
  {
    if (string.IsNullOrWhiteSpace(phoneE164))
    {
      return;
    }

    var key = PhoneHourKey(phoneE164);
    var n = _cache.TryGetValue(key, out int current) ? current : 0;
    _cache.Set(
        key,
        n + 1,
        new MemoryCacheEntryOptions
        {
          AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        });

    // Licznik dobowy per-numer — klucz datowany, wygasa po dobie.
    var dayKey = PhoneDayKey(phoneE164);
    var dn = _cache.TryGetValue(dayKey, out int dcurrent) ? dcurrent : 0;
    _cache.Set(
        dayKey,
        dn + 1,
        new MemoryCacheEntryOptions
        {
          AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
        });
  }

  // ── [M1] Per-IP/godzinę cap WYSŁANYCH OTP-SMS (niezależny od numeru) ──────────────────────────
  // Rotacja numeru z jednego IP omijała capy per-numer (reset z każdym numerem). Ten licznik liczy
  // realnie wysłane OTP-SMS w oknie godziny per IP — twardy sufit kosztu PRZED per-IP/global cap
  // SmsApiClienta (dobowy). Klucz datowany do godziny; wygasa naturalnie po godzinie.

  public void AssertCanSendOtpSmsFromIp(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp) || _options.MaxOtpSmsPerIpPerHour <= 0)
    {
      return;
    }

    var key = OtpSmsIpHourKey(clientIp);
    if (_cache.TryGetValue(key, out int n) && n >= _options.MaxOtpSmsPerIpPerHour)
    {
      throw new RateLimitExceededException(
          "Zbyt wiele kodów SMS wysłano z tego adresu. Spróbuj ponownie za godzinę.",
          retryAfterSeconds: 3600);
    }
  }

  public void RegisterOtpSmsSentFromIp(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp) || _options.MaxOtpSmsPerIpPerHour <= 0)
    {
      return;
    }

    var key = OtpSmsIpHourKey(clientIp);
    var n = _cache.TryGetValue(key, out int current) ? current : 0;
    _cache.Set(
        key,
        n + 1,
        new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
  }

  // ── [M1e] Per-IP/godzinę cap WYSŁANYCH OTP-maili (niezależny od adresu) ───────────────────────
  // Lustro bloku [M1] dla kanału e-mail. Cap per-adres (MaxOtpPerEmailPerHour) omija rotacja adresu
  // przez catch-all, a dla maili — inaczej niż dla SMS — nie było żadnego backstopu per IP: ani tutaj,
  // ani niżej w SmsApiCliencie (ten dotyczy wyłącznie SMS). Salon na CustomerVerificationChannel.Email
  // miał więc jako jedyną barierę limiter 30/min, co przekłada się na setki potwierdzeń na godzinę.

  public void AssertCanSendOtpEmailFromIp(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp) || _options.MaxOtpEmailsPerIpPerHour <= 0)
    {
      return;
    }

    var key = OtpEmailIpHourKey(clientIp);
    if (_cache.TryGetValue(key, out int n) && n >= _options.MaxOtpEmailsPerIpPerHour)
    {
      throw new RateLimitExceededException(
          "Zbyt wiele kodów wysłano z tego adresu. Spróbuj ponownie za godzinę.",
          retryAfterSeconds: 3600);
    }
  }

  public void RegisterOtpEmailSentFromIp(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp) || _options.MaxOtpEmailsPerIpPerHour <= 0)
    {
      return;
    }

    var key = OtpEmailIpHourKey(clientIp);
    var n = _cache.TryGetValue(key, out int current) ? current : 0;
    _cache.Set(
        key,
        n + 1,
        new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
  }

  // ── [M4] Per-IP/godzinę cap POTWIERDZONYCH rezerwacji ─────────────────────────────────────────
  // Ostatnia bramka, niezależna od kanału weryfikacji. Capy wyżej ograniczają koszt WYSYŁKI kodów;
  // ta ogranicza liczbę wizyt, które realnie trafiają do kalendarza salonu. Sprawdzenie i inkrement
  // pod jednym lockiem — równoległe potwierdzenia nie mogą przecisnąć się ponad próg (TOCTOU).
  // Rezerwacja slotu jest skuteczna nawet gdy dalszy zapis padnie: wolimy zawyżyć licznik niż
  // pozwolić na pętlę „potwierdź → wywróć zapis → potwierdź ponownie".

  public void AssertCanConfirmBookingFromIp(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp) || _options.MaxConfirmedBookingsPerIpPerHour <= 0)
    {
      return;
    }

    var key = ConfirmedBookingIpKey(clientIp!);
    lock (_confirmedBookingLock)
    {
      var n = _cache.TryGetValue(key, out int current) ? current : 0;
      if (n >= _options.MaxConfirmedBookingsPerIpPerHour)
      {
        throw new RateLimitExceededException(
            "Zbyt wiele rezerwacji z tego adresu. Spróbuj ponownie za godzinę.",
            retryAfterSeconds: 3600);
      }

      _cache.Set(
          key,
          n + 1,
          new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
    }
  }

  // ── [M3] Per-IP cap liczby jednocześnie aktywnych holdów (AwaitingOtp) ─────────────────────────
  // Auto-anulowanie poprzedniego holdu działa tylko per-AnonSessionId; rotacja sesji / brak cookie
  // omijała je → slot hoarding. Tu trzymamy licznik aktywnych holdów per IP: +1 przy utworzeniu,
  // -1 przy zwolnieniu (anulowanie poprzedniego holdu tej sesji). TTL klucza = bufor ponad życie
  // holdu, więc zawieszone (nigdy nie zwolnione) wpisy same się goją.

  // Check i inkrement muszą być w JEDNEJ sekcji krytycznej — inaczej N równoległych /hold z tego samego
  // IP czyta ten sam licznik zanim którekolwiek go podbije (TOCTOU) i wszystkie przechodzą próg. Ta sama
  // ochrona co przy confirm-capie; bez niej cap 5 nie rekompensuje długiego HoldTtl.
  private readonly object _holdCapLock = new();

  public void RegisterHoldCreatedForIp(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp) || _options.MaxConcurrentHoldsPerIp <= 0)
    {
      return;
    }

    var key = ActiveHoldsIpKey(clientIp);

    lock (_holdCapLock)
    {
      var current = _cache.TryGetValue(key, out int n) ? n : 0;
      if (current >= _options.MaxConcurrentHoldsPerIp)
      {
        throw new RateLimitExceededException(
            "Masz zbyt wiele rozpoczętych rezerwacji z tego adresu. Dokończ lub anuluj istniejącą i spróbuj ponownie.",
            retryAfterSeconds: 0);
      }

      _cache.Set(
          key,
          current + 1,
          // Z zapasem ponad lease OTP (3 min) — gdyby release nigdy nie przyszedł (expiry holdu),
          // licznik i tak wygasa, więc nie blokuje IP na stałe.
          new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
    }
  }

  public void ReleaseHoldForIp(string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp) || _options.MaxConcurrentHoldsPerIp <= 0)
    {
      return;
    }

    var key = ActiveHoldsIpKey(clientIp);

    // Dekrement pod tym samym lockiem co inkrement — inaczej równoległe release/register gubią zliczenia.
    lock (_holdCapLock)
    {
      if (!_cache.TryGetValue(key, out int current) || current <= 0)
      {
        return;
      }

      var next = current - 1;
      if (next <= 0)
      {
        _cache.Remove(key);
      }
      else
      {
        _cache.Set(
            key,
            next,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
      }
    }
  }

  private string OtpSmsIpHourKey(string ip) =>
      $"otp:sms-ip-hour:{ip}:{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMddHH}";

  private string OtpEmailIpHourKey(string ip) =>
      $"otp:mail-ip-hour:{ip}:{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMddHH}";

  private string ConfirmedBookingIpKey(string ip) =>
      $"booking:confirmed-ip-hour:{ip}:{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMddHH}";

  /// <summary>Serializuje sprawdzenie+inkrement capu [M4] — bez tego równoległe potwierdzenia przechodzą ponad próg.</summary>
  private readonly object _confirmedBookingLock = new();

  private static string ActiveHoldsIpKey(string ip) => $"hold:active-ip:{ip}";

  private void AssertIpWindow(string prefix, string? clientIp, int maxPerMinute)
  {
    if (string.IsNullOrWhiteSpace(clientIp))
    {
      return;
    }

    var ipKey = IpMinuteKey(prefix, clientIp);
    if (_cache.TryGetValue(ipKey, out int n) && n >= maxPerMinute)
    {
      throw new RateLimitExceededException(
          "Zbyt wiele żądań z tego adresu. Spróbuj ponownie za około minutę.",
          60);
    }
  }

  private void BumpIpWindow(string prefix, string? clientIp)
  {
    if (string.IsNullOrWhiteSpace(clientIp))
    {
      return;
    }

    var ipKey = IpMinuteKey(prefix, clientIp);
    var n = 0;
    if (_cache.TryGetValue(ipKey, out int current))
    {
      n = current;
    }

    _cache.Set(
        ipKey,
        n + 1,
        new MemoryCacheEntryOptions { AbsoluteExpiration = _timeProvider.GetUtcNow().AddMinutes(2) });
  }

  private string IpMinuteKey(string prefix, string ip) =>
      $"{prefix}:{ip}:{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMddHHmm}";
}
