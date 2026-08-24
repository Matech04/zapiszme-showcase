using App.Application.Appointments;
using App.Application.Common;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Okresowo synchronizuje statusy wizyt z aktualnym czasem (strefa biznesowa).
/// Usuwa z bazy porzucone holdy (Pending/AwaitingOtp) po wygaśnięciu dzierżawy (HoldLease).
/// Po zakończeniu slotu: Pending/AwaitingOtp → Canceled (zachowujemy rekord), Booked/InProgress → Completed.
/// Po rozpoczęciu: Booked → InProgress.
/// </summary>
public sealed class AppointmentStatusLifecycleHostedService : BackgroundService
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<AppointmentStatusLifecycleHostedService> _logger;
  private readonly TimeProvider _timeProvider;

  public AppointmentStatusLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AppointmentStatusLifecycleHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
  }

  /// <summary>
  /// E2E backdoor entry point — wymusza jeden cykl bez czekania na 1-min interval.
  /// </summary>
  internal Task ExecuteOnceAsync(CancellationToken ct) => RunCycleAsync(ct);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await RunCycleAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Błąd podczas synchronizacji statusów wizyt.");
      }

      try
      {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }
  }

  private async Task RunCycleAsync(CancellationToken ct)
  {
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var inspirationCleanup = scope.ServiceProvider.GetRequiredService<IAppointmentInspirationCleanup>();

    var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

    // Atomiczne usunięcie wygasłych holdów: warunek sprawdzany przez DB w momencie DELETE,
    // nie z wcześniej załadowanego snapshotu. Eliminuje wyścig z RequestOtpCommand, który
    // wydłuża lease — gdybyśmy ładowali snapshot, moglibyśmy trafić na stary ExpiryTimeUtc
    // i usunąć wizytę, którą użytkownik właśnie weryfikuje OTP.
    var leaseExpiredRemoved = await db.Appointments
        .IgnoreQueryFilters()
        .Where(a =>
          (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.AwaitingOtp)
          && a.Lease != null
          && a.Lease.ExpiryTimeUtc < utcNow)
        .ExecuteDeleteAsync(ct);

    var tenantTimeZones = await db.Tenants
      .AsNoTracking()
      .Select(t => new { t.Id, t.TimeZoneId })
      .ToDictionaryAsync(t => t.Id, t => t.TimeZoneId, ct);

    // Tranzycje statusów dotyczą tylko wizyt z dziś/przeszłości (ResolveTransition zwraca null dla
    // przyszłych). Ograniczamy skan do okna [wczoraj, jutro] (margines ±1 dzień na strefy czasowe),
    // żeby nie ładować całej historii ani wszystkich przyszłych Booked co minutę — w parze z indeksem
    // (Status, Date) to range scan zamiast Seq Scan rosnącego z danymi.
    var lowerDate = DateOnly.FromDateTime(utcNow.Date.AddDays(-1));
    var upperDate = DateOnly.FromDateTime(utcNow.Date.AddDays(1));
    var candidates = await db.Appointments
        .IgnoreQueryFilters()
        .AsTracking()
        .Where(a =>
          (a.Status == AppointmentStatus.Pending
           || a.Status == AppointmentStatus.AwaitingOtp
           || a.Status == AppointmentStatus.Booked
           || a.Status == AppointmentStatus.InProgress)
          && a.Date >= lowerDate
          && a.Date <= upperDate)
        .ToListAsync(ct);

    var statusChanged = 0;
    // Klucze obiektów R2 (główny + miniatura) zdjęć inspiracji wizyt, które właśnie weszły w stan
    // terminalny — kasujemy je PO udanym commit (best-effort), poza pętlą.
    var inspirationKeysToPurge = new List<string>();

    foreach (var appointment in candidates)
    {
      var timeZoneId = tenantTimeZones.TryGetValue(appointment.TenantId, out var tz) ? tz : "Europe/Warsaw";
      var startUtc = AppointmentScheduleGuard.ToUtcInstant(timeZoneId, appointment.Date, appointment.StartTime);
      var endUtc = AppointmentScheduleGuard.ToUtcInstant(timeZoneId, appointment.Date, appointment.EndTime);

      var next = AppointmentStatusLifecycleRules.ResolveTransition(
        appointment.Status,
        startUtc,
        endUtc,
        utcNow);

      if (next == null || next == appointment.Status)
      {
        continue;
      }

      // Pending/AwaitingOtp po terminie → Canceled (zachowujemy rekord zamiast twardego DELETE).
      // Twarde usuwanie kasowało potwierdzone w trybie ręcznym wizyty Pending, których salon
      // nie zdążył zatwierdzić przed terminem — bez śladu w bazie. Porzucone holdy z wygasłą
      // dzierżawą i tak usuwa wcześniejszy ExecuteDeleteAsync (leaseExpiredRemoved).
      appointment.ChangeStatus(next);

      // Wizyta weszła w stan terminalny (Completed/Canceled) → podgląd inspiracji zbędny: zdejmij
      // rekordy (skasują się w tym samym SaveChanges) i zbierz klucze do skasowania z R2 po commit.
      if (next.IsTerminal)
      {
        inspirationKeysToPurge.AddRange(inspirationCleanup.DetachImages(appointment));
      }

      statusChanged++;
    }

    if (statusChanged > 0)
    {
      await db.SaveChangesAsync(ct);
      await inspirationCleanup.PurgeStorageAsync(inspirationKeysToPurge, ct);
    }

    if (leaseExpiredRemoved > 0 || statusChanged > 0)
    {
      _logger.LogInformation(
        "Synchronizacja wizyt: usunięto hold-expired {LeaseExpired}, zmieniono status {StatusChanged}.",
        leaseExpiredRemoved,
        statusChanged);
    }
  }
}
