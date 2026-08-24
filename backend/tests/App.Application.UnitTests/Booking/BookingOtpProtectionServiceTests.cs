using App.Domain.Exceptions;
using App.Infrastructure.Booking;
using Microsoft.Extensions.Caching.Memory;

namespace App.Application.UnitTests.Booking;

public sealed class BookingOtpProtectionServiceTests
{
  [Fact]
  public void AssertCanRequestOtp_after_RegisterOtpRequestSucceeded_throws_until_cooldown()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var appointmentId = Guid.NewGuid();

    sut.AssertCanRequestOtp(appointmentId, clientIp: null);
    sut.RegisterOtpRequestSucceeded(appointmentId, clientIp: null);

    var ex = Assert.Throws<RateLimitExceededException>(() => sut.AssertCanRequestOtp(appointmentId, clientIp: null));
    Assert.True(ex.RetryAfterSeconds > 0);
  }

  [Fact]
  public void AssertCanReschedule_blocks_after_per_appointment_daily_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var appointmentId = Guid.NewGuid();

    // Cap per-wizyta/dobę = 5. Każde odbicie A→B→A wyzwala płatny SMS — po 5 przełożeniach tej samej
    // wizyty (nawet z rotacją sesji/IP) 6. musi paść, żeby zamknąć drenaż budżetu SMS salonu.
    for (var i = 0; i < 5; i++)
    {
      sut.AssertCanReschedule(Guid.NewGuid(), appointmentId, clientIp: $"10.0.0.{i}");
    }

    var ex = Assert.Throws<RateLimitExceededException>(() =>
      sut.AssertCanReschedule(Guid.NewGuid(), appointmentId, clientIp: "10.0.0.9"));
    Assert.True(ex.RetryAfterSeconds > 0);
  }

  [Fact]
  public void AssertCanReschedule_blocks_after_per_session_hourly_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var sessionToken = Guid.NewGuid();

    // Cap per-sesja/godzinę = 5, licząc różne wizyty tej samej sesji.
    for (var i = 0; i < 5; i++)
    {
      sut.AssertCanReschedule(sessionToken, Guid.NewGuid(), clientIp: null);
    }

    Assert.Throws<RateLimitExceededException>(() =>
      sut.AssertCanReschedule(sessionToken, Guid.NewGuid(), clientIp: null));
  }

  [Fact]
  public void AssertCanReschedule_allows_first_reschedule()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);

    // Legalny pojedynczy przypadek (klient przekłada raz) nie może być blokowany.
    var ex = Record.Exception(() =>
      sut.AssertCanReschedule(Guid.NewGuid(), Guid.NewGuid(), clientIp: "10.0.0.1"));
    Assert.Null(ex);
  }

  [Fact]
  public void RegisterFailedVerificationAttempt_third_time_makes_IsVerificationBlocked_true()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var appointmentId = Guid.NewGuid();

    Assert.Equal(1, sut.RegisterFailedVerificationAttempt(appointmentId));
    Assert.False(sut.IsVerificationBlocked(appointmentId));

    Assert.Equal(2, sut.RegisterFailedVerificationAttempt(appointmentId));
    Assert.False(sut.IsVerificationBlocked(appointmentId));

    Assert.Equal(3, sut.RegisterFailedVerificationAttempt(appointmentId));
    Assert.True(sut.IsVerificationBlocked(appointmentId));
  }

  [Fact]
  public void ClearVerificationAttempts_resets_fail_counter()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var appointmentId = Guid.NewGuid();

    sut.RegisterFailedVerificationAttempt(appointmentId);
    sut.RegisterFailedVerificationAttempt(appointmentId);
    sut.ClearVerificationAttempts(appointmentId);

    Assert.False(sut.IsVerificationBlocked(appointmentId));
    Assert.Equal(1, sut.RegisterFailedVerificationAttempt(appointmentId));
  }

  [Fact]
  public void IsTargetVerificationBlocked_true_after_max_per_target_independent_of_appointment()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    const string target = "guest@example.com";

    // Per-target próg = 10 (MaxFailedVerificationsPerTarget). Symuluje rotację holdu:
    // każda próba na innym appointmentId, ale ten sam kontakt — per-target licznik trzyma się.
    for (var i = 0; i < BookingOtpProtectionService.MaxFailedVerificationsPerTarget - 1; i++)
    {
      sut.RegisterFailedVerificationForTarget(target);
      Assert.False(sut.IsTargetVerificationBlocked(target));
    }

    sut.RegisterFailedVerificationForTarget(target);
    Assert.True(sut.IsTargetVerificationBlocked(target));

    sut.ClearTargetVerificationAttempts(target);
    Assert.False(sut.IsTargetVerificationBlocked(target));
  }

  [Fact]
  public void AssertCanSendOtpToPhone_blocks_after_max_per_hour()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    const string phone = "+48501234567";

    // Cap godzinowy = 3 (MaxOtpPerPhonePerHour). 3 wysyłki OK, 4. blokowana.
    for (var i = 0; i < 3; i++)
    {
      sut.AssertCanSendOtpToPhone(phone);
      sut.RegisterOtpSentToPhone(phone);
    }

    Assert.Throws<RateLimitExceededException>(() => sut.AssertCanSendOtpToPhone(phone));
  }

  [Fact]
  public void AssertCanRequestOtp_blocks_after_max_sends_per_appointment()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    var appointmentId = Guid.NewGuid();

    // Per-hold cap = 3 (MaxOtpSendsPerAppointment). Symulujemy 3 udane wysyłki na ten sam hold.
    for (var i = 0; i < 3; i++)
    {
      sut.RegisterOtpRequestSucceeded(appointmentId, clientIp: null);
    }

    // Sprawdzenie per-hold jest PIERWSZE w AssertCanRequestOtp (przed cooldownem), więc rzuca
    // z RetryAfterSeconds == 0 — to odróżnia limit per-hold od 60s cooldownu (retry > 0).
    var ex = Assert.Throws<RateLimitExceededException>(() => sut.AssertCanRequestOtp(appointmentId, clientIp: null));
    Assert.Equal(0, ex.RetryAfterSeconds);
  }

  [Fact]
  public void AssertCanRequestOtp_blocks_after_max_requests_per_ip_in_same_minute_window()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System);
    const string ip = "203.0.113.50";

    for (var i = 0; i < 30; i++)
    {
      sut.AssertCanRequestOtp(Guid.NewGuid(), ip);
      sut.RegisterOtpRequestSucceeded(Guid.NewGuid(), ip);
    }

    Assert.Throws<RateLimitExceededException>(() => sut.AssertCanRequestOtp(Guid.NewGuid(), ip));
  }

  // ── [M1] Per-IP/godzinę cap WYSŁANYCH OTP-SMS (niezależny od numeru telefonu) ──────────────────

  [Fact]
  public void AssertCanSendOtpSmsFromIp_blocks_after_cap_regardless_of_phone_number()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxOtpSmsPerIpPerHour = 3 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);
    const string ip = "203.0.113.99";

    // Symulacja rotacji numeru z jednego IP: każda wysyłka na INNY numer, ale licznik per-IP rośnie.
    for (var i = 0; i < 3; i++)
    {
      sut.AssertCanSendOtpSmsFromIp(ip);
      sut.RegisterOtpSmsSentFromIp(ip);
    }

    var ex = Assert.Throws<RateLimitExceededException>(() => sut.AssertCanSendOtpSmsFromIp(ip));
    Assert.True(ex.RetryAfterSeconds > 0);
  }

  [Fact]
  public void AssertCanSendOtpSmsFromIp_isolated_per_ip()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxOtpSmsPerIpPerHour = 2 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);

    for (var i = 0; i < 2; i++)
    {
      sut.AssertCanSendOtpSmsFromIp("1.1.1.1");
      sut.RegisterOtpSmsSentFromIp("1.1.1.1");
    }

    // Inny IP — niezależny budżet, przechodzi.
    sut.AssertCanSendOtpSmsFromIp("2.2.2.2");
    Assert.Throws<RateLimitExceededException>(() => sut.AssertCanSendOtpSmsFromIp("1.1.1.1"));
  }

  [Fact]
  public void AssertCanSendOtpSmsFromIp_noop_for_null_ip_or_disabled_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var disabled = BookingOtpProtectionFactory.Create(
        cache, TimeProvider.System, new BookingOtpProtectionOptions { MaxOtpSmsPerIpPerHour = 0 });

    // Brak IP → no-op (tło bez HttpContext).
    sut_NoThrow(() => disabled.AssertCanSendOtpSmsFromIp(null));
    // Cap=0 → wyłączony, nigdy nie rzuca choćby po wielu wysyłkach.
    for (var i = 0; i < 100; i++)
    {
      disabled.RegisterOtpSmsSentFromIp("9.9.9.9");
    }
    sut_NoThrow(() => disabled.AssertCanSendOtpSmsFromIp("9.9.9.9"));
  }

  // ── [M1e] Per-IP cap wysłanych OTP-maili ───────────────────────────────────────────────────────
  // Regresja preflight: salony na CustomerVerificationChannel.Email miały wyłącznie cap per-adres,
  // który omija catch-all (atak+N@moja.domena). Dla SMS backstop per-IP istniał, dla maili nie.

  [Fact]
  public void AssertCanSendOtpEmailFromIp_blocks_after_cap_regardless_of_address()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxOtpEmailsPerIpPerHour = 3 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);
    const string ip = "203.0.113.77";

    // Rotacja adresu z jednego IP: każdy send na INNY adres, ale licznik per-IP rośnie.
    for (var i = 0; i < 3; i++)
    {
      sut.AssertCanSendOtpEmailFromIp(ip);
      sut.RegisterOtpEmailSentFromIp(ip);
    }

    var ex = Assert.Throws<RateLimitExceededException>(() => sut.AssertCanSendOtpEmailFromIp(ip));
    Assert.True(ex.RetryAfterSeconds > 0);
  }

  [Fact]
  public void AssertCanSendOtpEmailFromIp_isolated_per_ip_and_from_sms_budget()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions
    {
      MaxOtpEmailsPerIpPerHour = 2,
      MaxOtpSmsPerIpPerHour = 2,
    };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);

    for (var i = 0; i < 2; i++)
    {
      sut.AssertCanSendOtpEmailFromIp("1.1.1.1");
      sut.RegisterOtpEmailSentFromIp("1.1.1.1");
    }

    // Inny IP — niezależny budżet.
    sut.AssertCanSendOtpEmailFromIp("2.2.2.2");
    // Kanał SMS ma WŁASNY licznik: wyczerpanie maili nie może go blokować (i odwrotnie).
    sut.AssertCanSendOtpSmsFromIp("1.1.1.1");
    Assert.Throws<RateLimitExceededException>(() => sut.AssertCanSendOtpEmailFromIp("1.1.1.1"));
  }

  [Fact]
  public void AssertCanSendOtpEmailFromIp_noop_for_null_ip_or_disabled_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var disabled = BookingOtpProtectionFactory.Create(
        cache, TimeProvider.System, new BookingOtpProtectionOptions { MaxOtpEmailsPerIpPerHour = 0 });

    sut_NoThrow(() => disabled.AssertCanSendOtpEmailFromIp(null));
    for (var i = 0; i < 100; i++)
    {
      disabled.RegisterOtpEmailSentFromIp("9.9.9.9");
    }
    sut_NoThrow(() => disabled.AssertCanSendOtpEmailFromIp("9.9.9.9"));
  }

  // ── [M4] Per-IP cap potwierdzonych rezerwacji ──────────────────────────────────────────────────
  // Capy wysyłkowe ograniczają koszt kodów, nie liczbę wizyt w kalendarzu. ReleaseHoldForIp zwalnia
  // miejsce w liczniku holdów zaraz po weryfikacji, więc MaxConcurrentHoldsPerIp też tego nie łapie.

  [Fact]
  public void AssertCanConfirmBookingFromIp_blocks_after_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxConfirmedBookingsPerIpPerHour = 3 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);
    const string ip = "203.0.113.55";

    // Asercja sama rezerwuje slot (atomowe check+increment) — brak osobnego Register*.
    for (var i = 0; i < 3; i++)
    {
      sut.AssertCanConfirmBookingFromIp(ip);
    }

    var ex = Assert.Throws<RateLimitExceededException>(() => sut.AssertCanConfirmBookingFromIp(ip));
    Assert.True(ex.RetryAfterSeconds > 0);
  }

  [Fact]
  public void AssertCanConfirmBookingFromIp_does_not_reserve_slot_when_over_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxConfirmedBookingsPerIpPerHour = 1 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);
    const string ip = "203.0.113.56";

    sut.AssertCanConfirmBookingFromIp(ip);
    // Odrzucone żądania nie mogą podbijać licznika — inaczej blokada przedłużałaby się w nieskończoność
    // przy uporczywym kliencie, a okno godzinne nigdy by się nie otworzyło.
    Assert.Throws<RateLimitExceededException>(() => sut.AssertCanConfirmBookingFromIp(ip));
    Assert.Throws<RateLimitExceededException>(() => sut.AssertCanConfirmBookingFromIp(ip));

    Assert.True(cache.TryGetValue(
        $"booking:confirmed-ip-hour:{ip}:{DateTime.UtcNow:yyyyMMddHH}", out int n));
    Assert.Equal(1, n);
  }

  [Fact]
  public void AssertCanConfirmBookingFromIp_isolated_per_ip_and_noop_when_disabled()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var sut = BookingOtpProtectionFactory.Create(
        cache, TimeProvider.System, new BookingOtpProtectionOptions { MaxConfirmedBookingsPerIpPerHour = 1 });

    sut.AssertCanConfirmBookingFromIp("1.1.1.1");
    // Inny IP — niezależny budżet.
    sut.AssertCanConfirmBookingFromIp("2.2.2.2");
    Assert.Throws<RateLimitExceededException>(() => sut.AssertCanConfirmBookingFromIp("1.1.1.1"));

    using var cache2 = new MemoryCache(new MemoryCacheOptions());
    var disabled = BookingOtpProtectionFactory.Create(
        cache2, TimeProvider.System, new BookingOtpProtectionOptions { MaxConfirmedBookingsPerIpPerHour = 0 });
    sut_NoThrow(() => disabled.AssertCanConfirmBookingFromIp(null));
    for (var i = 0; i < 100; i++)
    {
      disabled.AssertCanConfirmBookingFromIp("9.9.9.9");
    }
  }

  // ── [M3] Per-IP cap liczby jednocześnie aktywnych holdów ───────────────────────────────────────

  [Fact]
  public void RegisterHoldCreatedForIp_blocks_after_max_concurrent_holds_per_ip()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxConcurrentHoldsPerIp = 5 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);
    const string ip = "198.51.100.7";

    // 5 holdów BEZ zwolnienia (rotacja AnonSessionId — nic nie anuluje poprzednich) — OK.
    for (var i = 0; i < 5; i++)
    {
      sut.RegisterHoldCreatedForIp(ip);
    }

    // 6. hold z tego samego IP → odrzucony (RetryAfterSeconds == 0 — odróżnia od cooldownu).
    var ex = Assert.Throws<RateLimitExceededException>(() => sut.RegisterHoldCreatedForIp(ip));
    Assert.Equal(0, ex.RetryAfterSeconds);
  }

  [Fact]
  public void Legitimate_single_user_cancelling_previous_hold_never_hits_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxConcurrentHoldsPerIp = 5 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);
    const string ip = "198.51.100.8";

    // Legalny user: każdy nowy /hold anuluje poprzedni (release przed kolejnym create).
    // 20 kolejnych slotów — nigdy nie dobija progu, bo aktywny jest zawsze 1 hold.
    sut.RegisterHoldCreatedForIp(ip);
    for (var i = 0; i < 20; i++)
    {
      sut.ReleaseHoldForIp(ip);
      sut.RegisterHoldCreatedForIp(ip);
    }

    // Brak wyjątku = test przechodzi (Assert.Throws nie wystąpił powyżej).
  }

  [Fact]
  public void ReleaseHoldForIp_frees_a_slot_below_cap()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var options = new BookingOtpProtectionOptions { MaxConcurrentHoldsPerIp = 2 };
    var sut = BookingOtpProtectionFactory.Create(cache, TimeProvider.System, options);
    const string ip = "198.51.100.9";

    sut.RegisterHoldCreatedForIp(ip);
    sut.RegisterHoldCreatedForIp(ip);
    Assert.Throws<RateLimitExceededException>(() => sut.RegisterHoldCreatedForIp(ip));

    // Po zwolnieniu jednego — znów jest miejsce.
    sut.ReleaseHoldForIp(ip);
    sut.RegisterHoldCreatedForIp(ip);
    Assert.Throws<RateLimitExceededException>(() => sut.RegisterHoldCreatedForIp(ip));
  }

  private static void sut_NoThrow(Action action) => action();
}
