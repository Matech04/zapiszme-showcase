using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// SIATKA CHARAKTERYZUJĄCA (faza 0 refaktoru autoryzacji wizyt).
///
/// Zamraża odpowiedź na jedno pytanie: dla kombinacji `rola × StaffCalendarVisibilityPolicy ×
/// akcja` — czy API odmawia dostępu (403), czy nie. Celowo NIE sprawdza 200 vs 400 vs 404:
/// walidacja i istnienie zasobu to nie autoryzacja, a mieszanie tego czyni test kruchym.
///
/// Powstała, gdy cała ta logika żyła w prywatnych helperach `AppointmentsController`. Od faz 2-3
/// egzekwuje ją `IStaffAccessPolicy` w handlerach, a kontroler nie ma już ani jednego `Forbid()`.
/// Ten plik przeszedł obie fazy BEZ ZMIAN — to jego jedyne zadanie. Każda różnica oznaczałaby
/// zmianę zachowania, nie refaktor.
///
/// Rola `Admin` pominięta: przechodzi polityki, ale tenanta dostaje dopiero przez impersonację,
/// więc jej macierz należy do testów trybu wsparcia.
/// </summary>
public sealed class AppointmentAuthorizationMatrixIntegrationTests
{
  private const string Owner = "Owner";
  private const string Manager = "Manager";
  private const string Employee = "Employee";
  private const string Kiosk = "Kiosk";

  // Polityki podajemy w [InlineData] jako int (atrybuty wymagają stałych):
  //   1 = OwnCalendarOnly, 2 = TeamReadOnly, 3 = TeamFull.

  // ── ODCZYT: lista wizyt cudzego pracownika ───────────────────────────────────────────────
  // Owner/Manager/Kiosk zawsze. Employee: tylko `OwnCalendarOnly` go blokuje.

  [Theory]
  [InlineData(Owner, 1, false)] [InlineData(Owner, 2, false)] [InlineData(Owner, 3, false)]
  [InlineData(Manager, 1, false)] [InlineData(Manager, 2, false)] [InlineData(Manager, 3, false)]
  [InlineData(Kiosk, 1, false)] [InlineData(Kiosk, 2, false)] [InlineData(Kiosk, 3, false)]
  [InlineData(Employee, 1, true)] [InlineData(Employee, 2, false)] [InlineData(Employee, 3, false)]
  public async Task List_range_for_other_employee(string role, int policy, bool forbidden)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    var response = await ctx.Client(role).GetAsync(
      $"/api/Appointments?startDate={TestDates.IsoInDays(30)}&endDate={TestDates.IsoInDays(60)}&employeeId={ctx.OtherEmployeeId}", ctx.Ct);

    AssertForbidden(forbidden, response);
  }

  // ── ODCZYT: pojedyncza wizyta cudzego pracownika ─────────────────────────────────────────

  [Theory]
  [InlineData(Owner, 1, false)] [InlineData(Owner, 2, false)] [InlineData(Owner, 3, false)]
  [InlineData(Manager, 1, false)] [InlineData(Manager, 2, false)] [InlineData(Manager, 3, false)]
  [InlineData(Kiosk, 1, false)] [InlineData(Kiosk, 2, false)] [InlineData(Kiosk, 3, false)]
  [InlineData(Employee, 1, true)] [InlineData(Employee, 2, false)] [InlineData(Employee, 3, false)]
  public async Task Get_other_employees_appointment(string role, int policy, bool forbidden)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    var response = await ctx.Client(role).GetAsync($"/api/Appointments/{ctx.OtherAppointmentId}", ctx.Ct);

    AssertForbidden(forbidden, response);
  }

  // ── ODCZYT: wizyty klienta ───────────────────────────────────────────────────────────────
  // GOTCHA: nikt nie dostaje 403. `OwnCalendarOnly` FILTRUJE listę po cichu, zamiast odmówić.
  // Ta asymetria jest zamierzona i refaktor musi ją zachować.

  [Theory]
  [InlineData(Employee, 1)] [InlineData(Employee, 2)] [InlineData(Employee, 3)]
  [InlineData(Owner, 1)] [InlineData(Kiosk, 1)]
  public async Task Customer_appointments_never_forbidden_only_filtered(string role, int policy)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    var response = await ctx.Client(role).GetAsync($"/api/Appointments/customer/{ctx.CustomerId}", ctx.Ct);

    AssertForbidden(false, response);
  }

  // ── MUTACJA: utworzenie wizyty NA cudzego pracownika ─────────────────────────────────────
  // Employee potrzebuje `TeamFull`; `TeamReadOnly` daje wgląd, ale nie prawo zapisu.

  [Theory]
  [InlineData(Owner, 1, false)] [InlineData(Owner, 3, false)]
  [InlineData(Manager, 1, false)] [InlineData(Manager, 3, false)]
  [InlineData(Kiosk, 1, false)] [InlineData(Kiosk, 3, false)]
  [InlineData(Employee, 1, true)] [InlineData(Employee, 2, true)] [InlineData(Employee, 3, false)]
  public async Task Create_appointment_for_other_employee(string role, int policy, bool forbidden)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    var response = await ctx.Client(role).PostAsJsonAsync(
      "/api/Appointments",
      new
      {
        employeeId = ctx.OtherEmployeeId,
        serviceIds = new[] { ctx.ServiceId },
        date = TestDates.IsoInDays(50),
        startTime = "10:00:00",
        customerId = ctx.CustomerId,
        customerPhone = (string?)null,
        createAsBooked = true,
      },
      ctx.Ct);

    AssertForbidden(forbidden, response);
  }

  // ── MUTACJA: zmiana statusu cudzej wizyty (= anulowanie) ─────────────────────────────────

  [Theory]
  [InlineData(Owner, 1, false)] [InlineData(Owner, 3, false)]
  [InlineData(Manager, 1, false)] [InlineData(Manager, 3, false)]
  [InlineData(Kiosk, 1, false)] [InlineData(Kiosk, 3, false)]
  [InlineData(Employee, 1, true)] [InlineData(Employee, 2, true)] [InlineData(Employee, 3, false)]
  public async Task Change_status_of_other_employees_appointment(string role, int policy, bool forbidden)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    // 5 = Canceled
    var response = await ctx.Client(role).PatchAsync(
      $"/api/Appointments/{ctx.OtherAppointmentId}/status", JsonContent.Create(5), ctx.Ct);

    AssertForbidden(forbidden, response);
  }

  // ── MUTACJA: reschedule autoryzuje ŹRÓDŁO i CEL ──────────────────────────────────────────
  // Bez sprawdzenia źródła pracownik „ściąga" cudzą wizytę na własny kalendarz: podaje
  // AppointmentId kolegi i własne EmployeeId, więc cel = on sam i bramka przepuszcza.
  // Reszta mutacji autoryzuje `appointment.EmployeeId`; reschedule był jedynym wyjątkiem.

  [Theory]
  [InlineData(Owner, 1, false)] [InlineData(Owner, 3, false)]
  [InlineData(Manager, 1, false)] [InlineData(Manager, 3, false)]
  [InlineData(Kiosk, 1, false)] [InlineData(Kiosk, 3, false)]
  [InlineData(Employee, 1, true)] [InlineData(Employee, 2, true)] [InlineData(Employee, 3, false)]
  public async Task Reschedule_other_employees_appointment_onto_self(string role, int policy, bool forbidden)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);

    // Wizyta kolegi, ale przepinana na WŁASNY kalendarz wołającego.
    var response = await ctx.Client(role).PatchAsync(
      $"/api/Appointments/{ctx.OtherAppointmentId}/reschedule",
      JsonContent.Create(new
      {
        employeeId = ctx.CallerEmployeeId,
        serviceIds = new[] { ctx.ServiceId },
        date = TestDates.IsoInDays(55),
        startTime = "12:00:00",
        ignoreSchedule = false,
      }),
      ctx.Ct);

    AssertForbidden(forbidden, response);
  }

  // ── MUTACJA: notatka i cena końcowa idą tym samym helperem co status ─────────────────────

  [Theory]
  [InlineData(Employee, 1, true)] [InlineData(Employee, 3, false)] [InlineData(Owner, 1, false)]
  public async Task Update_note_of_other_employees_appointment(string role, int policy, bool forbidden)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    var response = await ctx.Client(role).PatchAsync(
      $"/api/Appointments/{ctx.OtherAppointmentId}/note", JsonContent.Create("notatka"), ctx.Ct);

    AssertForbidden(forbidden, response);
  }

  // ── USUNIĘCIE: twarde `StaffManagement`, poza `StaffCalendarVisibilityPolicy` ─────────────
  // Kiosk NIE usuwa wizyt, mimo że wszystko inne na nich może. Employee nigdy, nawet w TeamFull.

  [Theory]
  [InlineData(Owner, 1, false)] [InlineData(Owner, 3, false)]
  [InlineData(Manager, 1, false)] [InlineData(Manager, 3, false)]
  [InlineData(Kiosk, 1, true)] [InlineData(Kiosk, 3, true)]
  [InlineData(Employee, 1, true)] [InlineData(Employee, 2, true)] [InlineData(Employee, 3, true)]
  public async Task Delete_other_employees_appointment(string role, int policy, bool forbidden)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    var response = await ctx.Client(role).DeleteAsync($"/api/Appointments/{ctx.OtherAppointmentId}", ctx.Ct);

    AssertForbidden(forbidden, response);
  }

  // ── ODCZYT: własna wizyta jest dostępna zawsze, niezależnie od polityki ──────────────────

  [Theory]
  [InlineData(Employee, 1)] [InlineData(Employee, 2)] [InlineData(Employee, 3)]
  public async Task Own_appointment_is_always_readable(string role, int policy)
  {
    await using var ctx = await Fixture.CreateAsync((StaffCalendarVisibilityPolicy)policy);
    var response = await ctx.Client(role).GetAsync($"/api/Appointments/{ctx.OwnAppointmentId}", ctx.Ct);

    AssertForbidden(false, response);
  }

  private static void AssertForbidden(bool expected, HttpResponseMessage response)
  {
    if (expected)
    {
      Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    else
    {
      Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
  }

  /// <summary>Salon z dwoma pracownikami i po jednej wizycie każdego, o zadanej polityce.</summary>
  private sealed class Fixture : IAsyncDisposable
  {
    private readonly BookingApiApplicationFactory _factory;

    private Fixture(BookingApiApplicationFactory factory) => _factory = factory;

    public Guid OtherEmployeeId { get; private init; }
    /// <summary>Pracownik powiązany z kontem wołającego — wszystkie klienty testowe dzielą user id SalonOwner.</summary>
    public Guid CallerEmployeeId { get; private init; }
    public Guid OtherAppointmentId { get; private init; }
    public Guid OwnAppointmentId { get; private init; }
    public Guid ServiceId { get; private init; }
    public Guid CustomerId { get; private init; }
    public CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient Client(string role) => role switch
    {
      Owner => _factory.CreateOwnerClient(),
      Manager => _factory.CreateManagerClient(),
      Employee => _factory.CreateEmployeeClient(),
      Kiosk => _factory.CreateKioskClient(),
      _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Nieznana rola w siatce."),
    };

    public static Task<Fixture> CreateAsync(StaffCalendarVisibilityPolicy policy)
    {
      var factory = new BookingApiApplicationFactory();
      var seed = RestApiIntegrationSeed.Seed(factory.Services);
      SetPolicy(factory.Services, seed.TenantId, policy);

      var otherEmployeeId = SeedSecondEmployee(factory.Services, seed.TenantId);
      var otherAppointmentId = SeedAppointment(
        factory.Services, seed.TenantId, otherEmployeeId, seed.ServiceId, seed.CustomerId,
        TestDates.InDays(40));
      var ownAppointmentId = SeedAppointment(
        factory.Services, seed.TenantId, seed.EmployeeId, seed.ServiceId, seed.CustomerId,
        TestDates.InDays(41));

      return Task.FromResult(new Fixture(factory)
      {
        OtherEmployeeId = otherEmployeeId,
        CallerEmployeeId = seed.EmployeeId,
        OtherAppointmentId = otherAppointmentId,
        OwnAppointmentId = ownAppointmentId,
        ServiceId = seed.ServiceId,
        CustomerId = seed.CustomerId,
      });
    }

    public ValueTask DisposeAsync()
    {
      _factory.Dispose();
      return ValueTask.CompletedTask;
    }

    private static void SetPolicy(IServiceProvider services, Guid tenantId, StaffCalendarVisibilityPolicy policy)
    {
      using var scope = services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenant = db.Tenants.Single(t => t.Id == tenantId);
      tenant.Update(tenant.Name, tenant.Slug, staffCalendarVisibilityPolicy: policy);
      db.SaveChanges();
    }

    private static Guid SeedSecondEmployee(IServiceProvider services, Guid tenantId)
    {
      using var scope = services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var employee = new App.Domain.Aggregates.EmployeeAggregate.Employee(
        tenantId, userId: null, "Kolega", "Zespołowy", "teammate@authz-matrix.local");
      db.Employees.Add(employee);
      db.SaveChanges();
      return employee.Id;
    }

    private static Guid SeedAppointment(
      IServiceProvider services, Guid tenantId, Guid employeeId, Guid serviceId, Guid customerId, DateOnly date)
    {
      using var scope = services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var appointment = new Appointment(
        tenantId, employeeId, serviceId, customerId,
        date, new TimeOnly(9, 0), new TimeOnly(9, 30),
        AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);
      db.Appointments.Add(appointment);
      db.SaveChanges();
      return appointment.Id;
    }
  }
}
