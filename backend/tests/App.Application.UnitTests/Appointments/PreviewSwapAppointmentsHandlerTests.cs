using App.Application.Appointments.Queries.PreviewSwapAppointments;
using App.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using static App.Application.UnitTests.Appointments.SwapTestSupport;

namespace App.Application.UnitTests.Appointments;

/// <summary>
/// APP-APPT — PreviewSwapAppointmentsHandler. Sprawdza flagi podglądu, na których panel
/// opiera decyzję, czy poprosić o potwierdzenie skrócenia dłuższej wizyty.
/// </summary>
public sealed class PreviewSwapAppointmentsHandlerTests
{
  [Fact]
  public async Task Preview_equal_duration_reports_equal_and_plain_fits()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(10, 0), 30);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(12, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    var dto = await BuildPreviewHandler(env).Handle(new PreviewSwapAppointmentsQuery(a.Id, b.Id), ct);

    Assert.True(dto.EqualDuration);
    Assert.True(dto.PlainSwapFits);
    Assert.False(dto.HarmonizationAvailable);
    Assert.Null(dto.ServiceChangeAppointmentId);
  }

  [Fact]
  public async Task Preview_unequal_plain_fits_reports_harmonization_details()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 0), 60);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    var dto = await BuildPreviewHandler(env).Handle(new PreviewSwapAppointmentsQuery(a.Id, b.Id), ct);

    Assert.False(dto.EqualDuration);
    Assert.True(dto.PlainSwapFits);
    Assert.True(dto.HarmonizationAvailable);
    Assert.True(dto.HarmonizedSwapFits);
    Assert.Equal(a.Id, dto.ServiceChangeAppointmentId); // dłuższa = A
    Assert.Equal(env.Long.Name, dto.FromServiceName);
    Assert.Equal(env.Short.Name, dto.ToServiceName);
    Assert.Equal(env.Long.Price.Amount, dto.OldPrice);
    Assert.Equal(env.Short.Price.Amount, dto.NewPrice);
  }

  [Fact]
  public async Task Preview_unequal_plain_does_not_fit_but_harmonized_fits()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 0), 60);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 0), 30);
    AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(13, 45), 30); // blocker
    await env.Db.SaveChangesAsync(ct);

    var dto = await BuildPreviewHandler(env).Handle(new PreviewSwapAppointmentsQuery(a.Id, b.Id), ct);

    Assert.False(dto.EqualDuration);
    Assert.False(dto.PlainSwapFits);
    Assert.True(dto.HarmonizationAvailable);
    Assert.True(dto.HarmonizedSwapFits);
    Assert.Equal(a.Id, dto.ServiceChangeAppointmentId);
  }

  [Fact]
  public async Task Preview_harmonization_unavailable_when_longer_employee_lacks_shorter_service()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct, emp1OffersShort: false);
    var a = AddAppointment(env, env.Emp1, env.Long, env.Day, new TimeOnly(10, 0), 60);
    var b = AddAppointment(env, env.Emp2, env.Short, env.Day, new TimeOnly(13, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    var dto = await BuildPreviewHandler(env).Handle(new PreviewSwapAppointmentsQuery(a.Id, b.Id), ct);

    Assert.False(dto.EqualDuration);
    Assert.False(dto.HarmonizationAvailable);
    Assert.False(dto.HarmonizedSwapFits);
    Assert.Null(dto.ServiceChangeAppointmentId);
  }

  [Fact]
  public async Task Preview_throws_TenantViolation_for_appointment_outside_current_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var env = await SeedAsync(ct);
    var a = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(10, 0), 30);
    var b = AddAppointment(env, env.Emp1, env.Short, env.Day, new TimeOnly(12, 0), 30);
    await env.Db.SaveChangesAsync(ct);

    // Podgląd nie ładuje tenanta, więc widoczność daje query filter (real tenant), a check
    // TenantId w handlerze wykrywa niezgodność z tenantem żądania.
    var handler = BuildPreviewHandler(env, tenantOverride: Guid.NewGuid());
    await Assert.ThrowsAsync<TenantViolation>(() =>
      handler.Handle(new PreviewSwapAppointmentsQuery(a.Id, b.Id), ct));
  }
}
