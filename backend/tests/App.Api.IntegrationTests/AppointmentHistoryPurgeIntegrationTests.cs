using App.Api.E2eSupport;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Api.IntegrationTests;

/// <summary>
/// NO-HISTORY-001..004 — destrukcyjny cykl <see cref="AppointmentHistoryPurgeHostedService"/>
/// (tryb „nie przechowuj historii wizyt"). Testcontainers Postgres (owned Money/Items + xmin
/// nie działają na EF InMemory). Sprawdzamy, że job:
/// (a) usuwa wizyty terminalne (Completed/Canceled) z przeszłości tenanta z włączoną flagą,
/// (b) ZACHOWUJE przyszłe oraz aktywne/nie-terminalne wizyty (Booked/Pending/InProgress + dzisiejsze),
/// (c) NIE rusza wizyt tenanta BEZ flagi,
/// (d) izoluje tenanty — flaga jednego salonu nie kasuje danych drugiego.
/// </summary>
public sealed class AppointmentHistoryPurgeIntegrationTests
{
  private const int BatchSize = 500;

  [Fact]
  public async Task Purge_deletes_terminal_past_and_preserves_active_future_for_flagged_tenant()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // WYJĄTEK od reguły „żadnych literałów dat" (patrz TestDates): tutaj czas jest WSTRZYKIWANY —
    // `utcNow` idzie wprost do RunCycleAsync, a pozostałe daty są względem niego. Zestaw jest
    // wewnętrznie spójny i nie zależy od dnia uruchomienia, więc nie jest bombą zegarową.
    // Data jest tu przedmiotem testu (przeszłość / dziś / przyszłość względem cyklu purge).
    var utcNow = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
    var pastDate = new DateOnly(2026, 6, 10);
    var todayDate = new DateOnly(2026, 6, 24);
    var futureDate = new DateOnly(2026, 7, 10);

    Guid completedPast, canceledPast, completedToday, bookedFuture, pendingPast, inProgressPast;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      // Włącz tryb „nie przechowuj historii" na seed-tenancie.
      var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == seed.TenantId, ct);
      tenant.Update(tenant.Name, tenant.Slug, doNotRetainAppointmentHistory: true);
      await db.SaveChangesAsync(ct);

      var a1 = MakeAppt(seed, pastDate, new TimeOnly(8, 0), AppointmentStatus.Completed);
      var a2 = MakeAppt(seed, pastDate, new TimeOnly(9, 0), AppointmentStatus.Canceled);
      var a3 = MakeAppt(seed, todayDate, new TimeOnly(10, 0), AppointmentStatus.Completed);
      var a4 = MakeAppt(seed, futureDate, new TimeOnly(11, 0), AppointmentStatus.Booked);
      var a5 = MakeAppt(seed, pastDate, new TimeOnly(12, 0), AppointmentStatus.Pending);
      var a6 = MakeAppt(seed, pastDate, new TimeOnly(13, 0), AppointmentStatus.InProgress);

      db.Appointments.AddRange(a1, a2, a3, a4, a5, a6);
      await db.SaveChangesAsync(ct);

      completedPast = a1.Id;
      canceledPast = a2.Id;
      completedToday = a3.Id;
      bookedFuture = a4.Id;
      pendingPast = a5.Id;
      inProgressPast = a6.Id;
    }

    using (var runScope = factory.Services.CreateScope())
    {
      var db = runScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      await AppointmentHistoryPurgeHostedService.RunCycleAsync(db, utcNow, BatchSize, ct, NullLogger.Instance);
    }

    using var verifyScope = factory.Services.CreateScope();
    var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.False(await Exists(verify, completedPast, ct), "Completed z przeszłości MUSI być usunięta");
    Assert.False(await Exists(verify, canceledPast, ct), "Canceled z przeszłości MUSI być usunięta");
    Assert.True(await Exists(verify, completedToday, ct), "Completed dzisiejsza MA pozostać");
    Assert.True(await Exists(verify, bookedFuture, ct), "Booked w przyszłości MA pozostać");
    Assert.True(await Exists(verify, pendingPast, ct), "Pending (nie-terminalna) MA pozostać");
    Assert.True(await Exists(verify, inProgressPast, ct), "InProgress (nie-terminalna) MA pozostać");
  }

  [Fact]
  public async Task Purge_does_not_touch_tenant_without_flag_and_isolates_tenants()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var flagged = RestApiIntegrationSeed.Seed(factory.Services);
    var other = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var utcNow = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
    var pastDate = new DateOnly(2026, 6, 10);

    Guid flaggedTerminalPast, otherTerminalPast;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      // Tylko pierwszy tenant ma włączony tryb minimalizacji danych.
      var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == flagged.TenantId, ct);
      tenant.Update(tenant.Name, tenant.Slug, doNotRetainAppointmentHistory: true);
      await db.SaveChangesAsync(ct);

      var a1 = MakeAppt(flagged, pastDate, new TimeOnly(8, 0), AppointmentStatus.Completed);
      // Identyczna terminalna+przeszła wizyta, ale u tenanta BEZ flagi → musi przetrwać.
      var a2 = MakeAppt(other, pastDate, new TimeOnly(8, 0), AppointmentStatus.Completed);

      db.Appointments.AddRange(a1, a2);
      await db.SaveChangesAsync(ct);

      flaggedTerminalPast = a1.Id;
      otherTerminalPast = a2.Id;
    }

    using (var runScope = factory.Services.CreateScope())
    {
      var db = runScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      await AppointmentHistoryPurgeHostedService.RunCycleAsync(db, utcNow, BatchSize, ct, NullLogger.Instance);
    }

    using var verifyScope = factory.Services.CreateScope();
    var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.False(await Exists(verify, flaggedTerminalPast, ct),
      "Wizyta tenanta z flagą MUSI być usunięta");
    Assert.True(await Exists(verify, otherTerminalPast, ct),
      "Wizyta tenanta BEZ flagi MUSI pozostać (izolacja tenantów)");
  }

  [Fact]
  public async Task OnDemand_purge_deletes_terminal_past_and_returns_count_without_flag()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    // Daty względem realnego „dziś" (margines ±14 dni absorbuje granicę strefy czasowej).
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var pastDate = today.AddDays(-14);
    var futureDate = today.AddDays(14);

    Guid completedPast, canceledPast, completedToday, bookedFuture, pendingPast;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      // UWAGA: NIE włączamy flagi DoNotRetainAppointmentHistory — purge na żądanie działa
      // niezależnie (owner świadomie kasuje to, co już się nazbierało).
      var a1 = MakeAppt(seed, pastDate, new TimeOnly(8, 0), AppointmentStatus.Completed);
      var a2 = MakeAppt(seed, pastDate, new TimeOnly(9, 0), AppointmentStatus.Canceled);
      var a3 = MakeAppt(seed, today, new TimeOnly(10, 0), AppointmentStatus.Completed);
      var a4 = MakeAppt(seed, futureDate, new TimeOnly(11, 0), AppointmentStatus.Booked);
      var a5 = MakeAppt(seed, pastDate, new TimeOnly(12, 0), AppointmentStatus.Pending);

      db.Appointments.AddRange(a1, a2, a3, a4, a5);
      await db.SaveChangesAsync(ct);

      completedPast = a1.Id;
      canceledPast = a2.Id;
      completedToday = a3.Id;
      bookedFuture = a4.Id;
      pendingPast = a5.Id;
    }

    int deleted;
    using (var runScope = factory.Services.CreateScope())
    {
      var purger = runScope.ServiceProvider.GetRequiredService<IAppointmentHistoryPurger>();
      deleted = await purger.PurgePastHistoryAsync(seed.TenantId, ct);
    }

    Assert.Equal(2, deleted);

    using var verifyScope = factory.Services.CreateScope();
    var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.False(await Exists(verify, completedPast, ct), "Completed z przeszłości MUSI być usunięta");
    Assert.False(await Exists(verify, canceledPast, ct), "Canceled z przeszłości MUSI być usunięta");
    Assert.True(await Exists(verify, completedToday, ct), "Completed dzisiejsza MA pozostać");
    Assert.True(await Exists(verify, bookedFuture, ct), "Booked w przyszłości MA pozostać");
    Assert.True(await Exists(verify, pendingPast, ct), "Pending (nie-terminalna) MA pozostać");
  }

  [Fact]
  public async Task OnDemand_purge_isolates_tenants()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var target = RestApiIntegrationSeed.Seed(factory.Services);
    var other = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var pastDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14);

    Guid targetTerminalPast, otherTerminalPast;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var a1 = MakeAppt(target, pastDate, new TimeOnly(8, 0), AppointmentStatus.Completed);
      var a2 = MakeAppt(other, pastDate, new TimeOnly(8, 0), AppointmentStatus.Completed);
      db.Appointments.AddRange(a1, a2);
      await db.SaveChangesAsync(ct);
      targetTerminalPast = a1.Id;
      otherTerminalPast = a2.Id;
    }

    using (var runScope = factory.Services.CreateScope())
    {
      var purger = runScope.ServiceProvider.GetRequiredService<IAppointmentHistoryPurger>();
      var deleted = await purger.PurgePastHistoryAsync(target.TenantId, ct);
      Assert.Equal(1, deleted);
    }

    using var verifyScope = factory.Services.CreateScope();
    var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.False(await Exists(verify, targetTerminalPast, ct), "Wizyta wskazanego salonu MUSI być usunięta");
    Assert.True(await Exists(verify, otherTerminalPast, ct), "Wizyta innego salonu MUSI pozostać (izolacja)");
  }

  private static Task<bool> Exists(ApplicationDbContext db, Guid id, CancellationToken ct) =>
    db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id, ct);

  private static Appointment MakeAppt(
    RestApiIntegrationSeedResult seed,
    DateOnly date,
    TimeOnly startTime,
    AppointmentStatus status)
  {
    return new Appointment(
      seed.TenantId,
      seed.EmployeeId,
      seed.ServiceId,
      seed.CustomerId,
      date,
      startTime,
      startTime.AddMinutes(30),
      status,
      new Money(80m, "PLN"),
      string.Empty,
      lease: null);
  }
}
