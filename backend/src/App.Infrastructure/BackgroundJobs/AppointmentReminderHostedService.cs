using App.Application.Appointments;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Okresowo wysyła przypomnienia o nadchodzących wizytach: okno 24h i okno ~2h.
/// Idempotencja przez kolumny <c>Reminder24hSentAtUtc</c> / <c>Reminder2hSentAtUtc</c> na wizycie —
/// każde okno wysyłane dokładnie raz. Gating po ustawieniach tenanta robi handler zdarzenia.
/// </summary>
public sealed class AppointmentReminderHostedService : BackgroundService
{
  private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan Window24h = TimeSpan.FromHours(24);
  private static readonly TimeSpan Window2h = TimeSpan.FromHours(2);

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<AppointmentReminderHostedService> _logger;
  private readonly TimeProvider _timeProvider;

  public AppointmentReminderHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AppointmentReminderHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
  }

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
        _logger.LogError(ex, "Błąd podczas wysyłki przypomnień o wizytach.");
      }

      try
      {
        await Task.Delay(Interval, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }
  }

  /// <summary>
  /// E2E backdoor entry point — wymusza jeden cykl bez czekania na 5-min interval.
  /// Używane przez <c>POST /api/_e2e/bg/run-reminders</c> w env "E2E".
  /// </summary>
  internal Task ExecuteOnceAsync(CancellationToken ct) => RunCycleAsync(ct);

  private async Task RunCycleAsync(CancellationToken ct)
  {
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

    var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
    var tenantTimeZones = await db.Tenants
      .AsNoTracking()
      .Select(t => new { t.Id, t.TimeZoneId })
      .ToDictionaryAsync(t => t.Id, t => t.TimeZoneId, ct);

    // Tylko Booked z choć jednym oczekującym przypomnieniem; ograniczamy zakres dat z OBU stron,
    // żeby nie skanować/materializować całej historii ANI wszystkich przyszłych rezerwacji co 5 min.
    // Dolna granica: wczoraj (Booked w przeszłości i tak migruje do Completed). Górna: dziś+2 dni —
    // przypomnienia lecą tylko w oknie ≤24h (Window24h), a margines +1 dnia pokrywa strefy czasowe
    // (data lokalna tenanta może wyprzedzać datę UTC). W parze z indeksem (Status, Date) to range scan
    // zamiast Seq Scan rosnącego liniowo z liczbą przyszłych wizyt (spójnie z jobem cyklu życia).
    var lowerDate = DateOnly.FromDateTime(utcNow.Date.AddDays(-1));
    var upperDate = DateOnly.FromDateTime(utcNow.Date.AddDays(2));
    var candidates = await db.Appointments
      .IgnoreQueryFilters()
      .AsTracking()
      .Where(a =>
        a.Status == AppointmentStatus.Booked
        && a.Date >= lowerDate
        && a.Date <= upperDate
        && (a.Reminder24hSentAtUtc == null || a.Reminder2hSentAtUtc == null))
      .ToListAsync(ct);

    var sent = 0;

    foreach (var appointment in candidates)
    {
      var timeZoneId = tenantTimeZones.TryGetValue(appointment.TenantId, out var tz) ? tz : "Europe/Warsaw";
      var startUtc = AppointmentScheduleGuard.ToUtcInstant(timeZoneId, appointment.Date, appointment.StartTime);

      // Okno ~2h: wizyta zaczyna się w ciągu najbliższych 2h.
      if (appointment.Reminder2hSentAtUtc is null
          && startUtc > utcNow
          && startUtc <= utcNow + Window2h)
      {
        if (await TryPublishAsync(publisher, appointment, is24h: false, ct))
        {
          appointment.MarkReminder2hSent(utcNow);
          await db.SaveChangesAsync(ct);
          sent++;
        }
        continue;
      }

      // Okno 24h: wizyta w ciągu najbliższych 24h, ale dalej niż 2h (inaczej leci tylko 2h).
      if (appointment.Reminder24hSentAtUtc is null
          && startUtc > utcNow + Window2h
          && startUtc <= utcNow + Window24h)
      {
        if (await TryPublishAsync(publisher, appointment, is24h: true, ct))
        {
          appointment.MarkReminder24hSent(utcNow);
          await db.SaveChangesAsync(ct);
          sent++;
        }
      }
    }

    if (sent > 0)
    {
      _logger.LogInformation("Przypomnienia o wizytach: wysłano {Sent} zdarzeń.", sent);
    }
  }

  private async Task<bool> TryPublishAsync(
    IPublisher publisher,
    Appointment appointment,
    bool is24h,
    CancellationToken ct)
  {
    try
    {
      await publisher.Publish(
        new AppointmentReminderDueEvent(appointment.TenantId, appointment.Id, is24h),
        ct);
      return true;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(
        ex,
        "Publikacja AppointmentReminderDueEvent ({Window}) dla wizyty {Id} nie powiodła się — ponowimy w kolejnym cyklu.",
        is24h ? "24h" : "2h",
        appointment.Id);
      return false;
    }
  }
}
