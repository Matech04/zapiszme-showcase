using App.Application.Appointments;
using App.Application.Booking.BookingAppointments.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.BookingAppointments.Queries;

public record GetBookingMonthAvailabilityQuery(int Year, int Month, Guid EmployeeId, IReadOnlyList<Guid> ServiceIds)
    : IRequest<BookingMonthAvailabilityDto>;

/// <summary>
/// Liczy liczbę wolnych slotów dla każdego dnia miesiąca (dla wybranego pracownika + usługi).
/// Reużywa tę samą logikę co <see cref="Appointments.Queries.GetAvailableTimeSlots.GetAvailableTimeSlotsQuery"/>,
/// ale pobiera wizyty całego miesiąca jednym zapytaniem zamiast per dzień.
/// </summary>
internal sealed class GetBookingMonthAvailabilityQueryHandler
    : TenantHandler<GetBookingMonthAvailabilityQuery, BookingMonthAvailabilityDto>
{
  private readonly IApplicationDbContext _context;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IAppointmentService _appointmentService;

  public GetBookingMonthAvailabilityQueryHandler(
      IApplicationDbContext context,
      IEmployeeRepository employeeRepository,
      ICurrentTenantService currentTenantService,
      IAppointmentService appointmentService)
      : base(currentTenantService)
  {
    _context = context;
    _employeeRepository = employeeRepository;
    _appointmentService = appointmentService;
  }

  public override async Task<BookingMonthAvailabilityDto> Handle(
      GetBookingMonthAvailabilityQuery request, CancellationToken ct)
  {
    if (request.Month is < 1 or > 12 || request.Year is < 1 or > 9999)
    {
      return new BookingMonthAvailabilityDto(false, null, new List<MonthDayAvailabilityDto>());
    }

    var tenant = await _context.Tenants.AsNoTracking()
      .Where(t => t.Id == TenantId)
      .FirstOrDefaultAsync(ct);

    var tenantTimeZoneId = tenant?.TimeZoneId ?? "Europe/Warsaw";
    var todayBiz = AppointmentScheduleGuard.TodayInBusinessTimeZone(tenantTimeZoneId);

    var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);
    var firstDay = new DateOnly(request.Year, request.Month, 1);
    var lastDay = new DateOnly(request.Year, request.Month, daysInMonth);

    // Dostępność liczona dla całego miesiąca — ładujemy override/urlopy ograniczone do [firstDay, lastDay].
    var employee = await _employeeRepository.GetForAvailabilityAsync(request.EmployeeId, firstDay, lastDay, ct)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    var horizonDays = tenant?.BookingHorizonDays ?? 120;

    // Miesiąc zamknięty jawną publikacją — kończymy od razu. Poza oszczędnością (pomijamy zapytanie
    // o wizyty całego miesiąca) nie wystawiamy też na zewnątrz obłożenia miesiąca, którego salon
    // jeszcze nie otworzył.
    var publication = employee.FindMonthPublication(request.Year, request.Month);
    if (publication != null && !publication.IsOpenAsOf(todayBiz))
    {
      var closedDays = Enumerable.Range(1, daysInMonth)
        .Select(d => new MonthDayAvailabilityDto(new DateOnly(request.Year, request.Month, d), 0, false))
        .ToList();

      return new BookingMonthAvailabilityDto(true, publication.OpensOn, closedDays);
    }

    // Czas combo = suma czasów usług; jedno zapytanie zamiast K round-tripów (combo).
    var serviceDuration = await ComboDurationResolver.ResolveAsync(
      _context, employee, request.ServiceIds, ct);

    var slotStepMinutes = tenant?.AppointmentSlotStepMinutes is { } step && step != 0 ? step : 15;

    // Wygasły anonimowy hold OTP (AwaitingOtp) nie blokuje slotu — patrz GetAvailableTimeSlotsQuery.
    var nowUtc = DateTime.UtcNow;

    // Wszystkie aktywne wizyty pracownika w całym miesiącu — jeden round-trip do DB
    // zamiast zapytania per dzień.
    var monthAppointmentsRaw = await _context.Appointments.AsNoTracking()
        .Where(a => a.TenantId == TenantId &&
                    a.EmployeeId == request.EmployeeId &&
                    a.Date >= firstDay && a.Date <= lastDay &&
                    a.Status != AppointmentStatus.Canceled &&
                    !(a.Status == AppointmentStatus.AwaitingOtp && a.Lease!.ExpiryTimeUtc < nowUtc))
        .Select(a => new { a.Date, a.StartTime, a.EndTime, a.Status })
        .ToListAsync(ct);

    var appointmentsByDate = monthAppointmentsRaw
        .GroupBy(a => a.Date)
        .ToDictionary(g => g.Key, g => g.ToList());

    var result = new List<MonthDayAvailabilityDto>(daysInMonth);

    for (var day = 1; day <= daysInMonth; day++)
    {
      var date = new DateOnly(request.Year, request.Month, day);

      // Tryb per-dzień: override może zmienić tryb pojedynczego dnia (grafik „klikany" bez cyklu).
      var isFixed = employee.IsFixedForDate(date);

      // Czy pracownik ma jakikolwiek grafik tego dnia (niezależnie od rezerwacji/przeszłości).
      // Front używa tego, by odróżnić „miesiąc zajęty" od „pracownik bez grafiku" (patrz DTO).
      var isWorkingDay = isFixed
        ? employee.GetFixedStartTimes(date).Count > 0
        : employee.GetWorkingRanges(date).Count > 0;

      if (date < todayBiz)
      {
        result.Add(new MonthDayAvailabilityDto(date, 0, isWorkingDay));
        continue;
      }

      // Poza horyzontem rezerwacji. Raportujemy isWorkingDay=false, mimo że grafik istnieje —
      // inaczej front zobaczyłby „dzień roboczy, 0 slotów" i pokazał to jako komplet rezerwacji,
      // czyli skłamałby o powodzie. Miesiąc może przecinać granicę horyzontu, stąd sprawdzenie per dzień.
      if (!employee.IsDateOpenForOnlineBooking(date, todayBiz, horizonDays))
      {
        result.Add(new MonthDayAvailabilityDto(date, 0, false));
        continue;
      }

      appointmentsByDate.TryGetValue(date, out var dayAppointmentsRaw);
      dayAppointmentsRaw ??= new();

      // Collision-check potrzebuje WSZYSTKICH aktywnych (włącznie z tymczasowymi
      // AwaitingOtp/Pending); gap-filling tylko potwierdzonych — patrz GetAvailableTimeSlotsQuery.
      var employeeAppointments = dayAppointmentsRaw
        .Select(a => new TimeRange(a.StartTime, a.EndTime))
        .ToList();

      var confirmedAppointments = dayAppointmentsRaw
        .Where(a => a.Status == AppointmentStatus.Booked
                 || a.Status == AppointmentStatus.InProgress
                 || a.Status == AppointmentStatus.Completed)
        .Select(a => new TimeRange(a.StartTime, a.EndTime))
        .ToList();

      var rawSlotsList = isFixed
        ? _appointmentService.EmployeeFixedSlotsList(
            employee.GetFixedStartTimes(date),
            employeeAppointments,
            employee,
            serviceDuration)
        : _appointmentService.EmployeeAvailableSlotsList(
            employee.GetWorkingRanges(date),
            employeeAppointments,
            employee,
            serviceDuration,
            slotStepMinutes);

      if (date == todayBiz)
      {
        rawSlotsList = rawSlotsList
          .Where(t => !AppointmentScheduleGuard.IsStartInPast(tenantTimeZoneId, date, t))
          .ToList();
      }

      // Tryb stałych slotów: gap-filling pomijamy (null = passthrough wszystkich slotów).
      var slots = GapFillingSlotFilter.Apply(
        rawSlotsList,
        confirmedAppointments,
        serviceDuration,
        slotStepMinutes,
        isFixed ? null : tenant?.GapFillingSettings);

      result.Add(new MonthDayAvailabilityDto(date, slots.Count, isWorkingDay));
    }

    return new BookingMonthAvailabilityDto(false, null, result);
  }
}
