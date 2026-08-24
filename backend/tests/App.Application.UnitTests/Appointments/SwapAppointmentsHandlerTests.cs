using App.Application.Appointments.Commands.SwapAppointments;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using static App.Application.UnitTests.Appointments.SwapTestSupport;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// APP-APPT — SwapAppointmentsHandler. Używa prawdziwego AppointmentService nad InMemory EF,
/// więc testy weryfikują realną logikę dostępności (kolizje + godziny pracy), nie atrapę.
/// </summary>
public sealed class SwapAppointmentsHandlerTests
{
  [Fact]
  public async Task Swap_equal_duration_same_employee_exchanges_slots()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(10, 0), 30);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(12, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    await BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id), ct);

    var (ra, rb) = await ReloadAsync(env, a.Id, b.Id, ct);
    Assert.Equal(new TimeOnly(12, 0), ra.StartTime);
    Assert.Equal(new TimeOnly(10, 0), rb.StartTime);
    Assert.Equal(env.Emp1.Id, ra.EmployeeId);
    Assert.Equal(env.Emp1.Id, rb.EmployeeId);
  }

  [Fact]
  public async Task Swap_cross_employee_keeps_each_employee_and_exchanges_slots()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(10, 0), 30);
    var b = AddAppointment(env, env.Emp2, env.Short, env.Day, new TimeOnly(12, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    await BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id), ct);

    var (ra, rb) = await ReloadAsync(env, a.Id, b.Id, ct);
    Assert.Equal(new TimeOnly(12, 0), ra.StartTime);
    Assert.Equal(env.Emp1.Id, ra.EmployeeId);
    Assert.Equal(new TimeOnly(10, 0), rb.StartTime);
    Assert.Equal(env.Emp2.Id, rb.EmployeeId);
  }

  [Fact]
  public async Task Swap_cross_employee_throws_when_target_employee_not_working_at_new_time()
  {
    var ct = TestContext.Current.CancellationToken;
    // Emp2 pracuje tylko 8:00–11:00.
    var env = await SeedAsync(ct, emp2Start: new TimeOnly(8, 0), emp2End: new TimeOnly(11, 0));
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(14, 0), 30);
    var b = AddAppointment(env, env.Emp2, env.Short, env.Day, new TimeOnly(9, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    // B (emp2) musiałby trafić na 14:00 — poza grafikiem emp2.
    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id), ct));
  }

  [Fact]
  public async Task Swap_unequal_duration_plain_succeeds_when_it_fits()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 0), 60);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    await BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id), ct);

    var (ra, rb) = await ReloadAsync(env, a.Id, b.Id, ct);
    Assert.Equal(new TimeOnly(13, 0), ra.StartTime);
    Assert.Equal(new TimeOnly(14, 0), ra.EndTime); // 60 min, usługa zachowana
    Assert.Equal(env.Long.Id, ra.ServiceId);
    Assert.Equal(new TimeOnly(10, 0), rb.StartTime);
    Assert.Equal(env.Short.Id, rb.ServiceId);
  }

  [Fact]
  public async Task Swap_unequal_duration_plain_throws_when_longer_does_not_fit()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 0), 60);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 0), 30);
    // Blocker tuż po B — dłuższa wizyta (60 min) wjeżdżałaby na 13:45.
    AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 45), 30);
    await env.Db.SaveChangesAsync(ct);

    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id, HarmonizeToShorter: false), ct));
  }

  [Fact]
  public async Task Swap_unequal_duration_harmonize_shortens_longer_appointment_and_succeeds()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 0), 60);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 0), 30);
    AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 45), 30);
    await env.Db.SaveChangesAsync(ct);

    await BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id, HarmonizeToShorter: true), ct);

    var (ra, rb) = await ReloadAsync(env, a.Id, b.Id, ct);
    // A (dłuższa) przyjęła krótszą usługę i skróciła się do 30 min — mieści się przed blockerem 13:45.
    Assert.Equal(env.Short.Id, ra.ServiceId);
    Assert.Equal(new TimeOnly(13, 0), ra.StartTime);
    Assert.Equal(new TimeOnly(13, 30), ra.EndTime);
    Assert.Equal(env.Short.Price.Amount, ra.TotalPrice.Amount);
    Assert.Equal(new TimeOnly(10, 0), rb.StartTime);
  }

  [Fact]
  public async Task Swap_harmonize_throws_when_longer_employee_lacks_shorter_service()
  {
    var ct = TestContext.Current.CancellationToken;
    // Emp1 oferuje TYLKO długą usługę.
    var env = await SeedAsync(ct, emp1OffersShort: false);
    var a = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 0), 60);
    var b = AddAppointment(env, env.Emp2, env.Short, env.Day, new TimeOnly(13, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id, HarmonizeToShorter: true), ct));
    Assert.Equal(ErrorCodes.AppointmentSwapHarmonizationUnavailable, ex.ErrorCode);
  }

  [Fact]
  public async Task Swap_throws_when_one_appointment_is_terminal()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(10, 0), 30, AppointmentStatus.Completed);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(12, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id), ct));
    Assert.Equal(ErrorCodes.AppointmentSwapTerminalStatus, ex.ErrorCode);
  }

  [Fact]
  public async Task Swap_throws_when_target_slot_in_past()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var past = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
    var a = AddAppointment(env, env.Emp1, env.Short, past, new TimeOnly(10, 0), 30);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(12, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    // B trafiłaby na slot A w przeszłości.
    await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
      BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id), ct));
  }

  [Fact]
  public async Task Swap_self_overlap_after_unequal_swap_same_employee_throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    // A 30 min @10:00 (10:00–10:30), B 60 min @10:45 (10:45–11:45) — nie nachodzą oryginalnie.
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(10, 0), 30);
    var b = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 45), 60);
    await env.Db.SaveChangesAsync(ct);

    // Po zamianie: A→10:45 (10:45–11:15), B→10:00 (10:00–11:00) — nachodzą na siebie.
    await Assert.ThrowsAsync<AppointmentSlotUnavailableException>(() =>
      BuildSwapHandler(env).Handle(new SwapAppointmentsCommand(a.Id, b.Id, HarmonizeToShorter: false), ct));
  }

  [Fact]
  public async Task Swap_throws_NotFound_for_appointment_outside_current_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(10, 0), 30);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(12, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    // Handler dla innego tenanta — lookup tenanta nie powiedzie się jako pierwszy.
    var handler = BuildSwapHandler(env, tenantOverride: Guid.NewGuid());
    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SwapAppointmentsCommand(a.Id, b.Id), ct));
  }
}
