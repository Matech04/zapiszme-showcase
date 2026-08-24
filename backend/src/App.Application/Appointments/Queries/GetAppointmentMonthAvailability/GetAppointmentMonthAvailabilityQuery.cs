using App.Application.Appointments.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments.Queries.GetAppointmentMonthAvailability;

public record GetAppointmentMonthAvailabilityQuery(int Year, int Month, Guid EmployeeId, IReadOnlyList<Guid> ServiceIds)
    : IRequest<List<AppointmentDayAvailabilityDto>>;

/// <summary>
/// Liczy liczbę wolnych slotów dla każdego dnia miesiąca (dla wybranego pracownika + usługi)
/// — odpowiednik <see cref="Booking.BookingAppointments.Queries.GetBookingMonthAvailabilityQuery"/>
/// dla panelu salonu. Tryb <c>AdjacentOnly</c> jest tu degradowany do <c>PreferAdjacent</c>,
/// bo personel może zawsze zarezerwować dowolny wolny termin (analogicznie do
/// <see cref="Appointments.Queries.GetAvailableTimeSlots.GetAvailableTimeSlotsQuery"/>).
/// </summary>
internal sealed class GetAppointmentMonthAvailabilityQueryHandler
    : TenantHandler<GetAppointmentMonthAvailabilityQuery, List<AppointmentDayAvailabilityDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IAppointmentService _appointmentService;

  public GetAppointmentMonthAvailabilityQueryHandler(
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

  public override async Task<List<AppointmentDayAvailabilityDto>> Handle(
      GetAppointmentMonthAvailabilityQuery request, CancellationToken ct)
  {
    if (request.Month is < 1 or > 12 || request.Year is < 1 or > 9999)
    {
      return new List<AppointmentDayAvailabilityDto>();
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

    // Czas combo = suma czasów usług; jedno zapytanie zamiast K round-tripów (combo).
    var serviceDuration = await ComboDurationResolver.ResolveAsync(
      _context, employee, request.ServiceIds, ct);

    var slotStepMinutes = tenant?.AppointmentSlotStepMinutes is { } step && step != 0 ? step : 15;

    // Tryb i gap-filling rozstrzygane PER DZIEŃ — override może zmienić tryb pojedynczego dnia
    // (kosmetyczka budująca grafik samymi klikniętymi dniami, bez cyklu tygodniowego).
    // gridGapSettings używane tylko dla dni w trybie siatki; dni stałe → null (passthrough).
    var gridGapSettings = StaffGapFillingSettingsResolver.Resolve(tenant?.GapFillingSettings);

    // Anonimowy hold OTP (AwaitingOtp) z WYGASŁYM lease nie blokuje już slotu — inaczej dzień
    // świeciłby się na czerwono/szaro do następnego przebiegu joba sprzątającego, mimo że po
    // wejściu w dzień slot-lista (GetAvailableTimeSlotsQuery) pokazuje go jako wolny. Booked/Pending
    // (panel lub manual po OTP) blokują zawsze, nawet ze stale lease — wykluczamy WYŁĄCZNIE AwaitingOtp.
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

    var result = new List<AppointmentDayAvailabilityDto>(daysInMonth);

    for (var day = 1; day <= daysInMonth; day++)
    {
      var date = new DateOnly(request.Year, request.Month, day);

      if (date < todayBiz)
      {
        result.Add(new AppointmentDayAvailabilityDto(date, 0));
        continue;
      }

      appointmentsByDate.TryGetValue(date, out var dayAppointmentsRaw);
      dayAppointmentsRaw ??= new();

      // Collision-check uwzględnia WSZYSTKIE aktywne wizyty (włącznie z AwaitingOtp/Pending);
      // gap-filling tylko potwierdzone — analogicznie do GetAvailableTimeSlotsQuery.
      var employeeAppointments = dayAppointmentsRaw
        .Select(a => new TimeRange(a.StartTime, a.EndTime))
        .ToList();

      var confirmedAppointments = dayAppointmentsRaw
        .Where(a => a.Status == AppointmentStatus.Pending
                 || a.Status == AppointmentStatus.Booked
                 || a.Status == AppointmentStatus.InProgress
                 || a.Status == AppointmentStatus.Completed)
        .Select(a => new TimeRange(a.StartTime, a.EndTime))
        .ToList();

      var isFixed = employee.IsFixedForDate(date);
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

      var slots = GapFillingSlotFilter.Apply(
        rawSlotsList,
        confirmedAppointments,
        serviceDuration,
        slotStepMinutes,
        isFixed ? null : gridGapSettings);

      result.Add(new AppointmentDayAvailabilityDto(date, slots.Count));
    }

    return result;
  }
}
