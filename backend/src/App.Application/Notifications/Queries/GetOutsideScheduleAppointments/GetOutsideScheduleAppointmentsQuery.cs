using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Queries.GetOutsideScheduleAppointments;

/// <summary>
/// Wizyty w zadanym zakresie dat, które po stronie domeny (`Employee.IsAvailable`)
/// nie mieszczą się w aktualnym grafiku, override’ach lub urlopach. Zasila centrum
/// powiadomień (dzwonek w panelu admina) — pollowane co kilka sekund.
/// </summary>
public record GetOutsideScheduleAppointmentsQuery(DateOnly From, DateOnly To)
    : IRequest<List<OutsideScheduleNotificationDto>>;

public record OutsideScheduleNotificationDto(
    Guid AppointmentId,
    Guid EmployeeId,
    string? EmployeeName,
    string? ServiceName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime
);

internal class GetOutsideScheduleAppointmentsQueryHandler
    : TenantHandler<GetOutsideScheduleAppointmentsQuery, List<OutsideScheduleNotificationDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly ICurrentUserAccessor _currentUser;
  private readonly IEmployeeRepository _employees;

  public GetOutsideScheduleAppointmentsQueryHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService,
      ICurrentUserAccessor currentUser,
      IEmployeeRepository employees)
      : base(currentTenantService)
  {
    _context = context;
    _currentUser = currentUser;
    _employees = employees;
  }

  public override async Task<List<OutsideScheduleNotificationDto>> Handle(
      GetOutsideScheduleAppointmentsQuery request,
      CancellationToken ct)
  {
    if (request.To < request.From)
    {
      return new List<OutsideScheduleNotificationDto>();
    }

    // Dzwonek osobisty: alert widzi tylko pracownik, którego wizyta dotyczy. Recepcja (Kiosk)
    // oraz tryb wsparcia (Admin w impersonacji) obsługują cały zespół — widzą wszystkie.
    var isDesk = _currentUser.IsDeskAccount || _currentUser.IsSupportSession;
    var callerEmployeeId = _currentUser.CallerEmployeeId;
    if (!isDesk && callerEmployeeId is null)
    {
      return new List<OutsideScheduleNotificationDto>();
    }

    // Projekcja, nie encja: `Appointment` ma własne kolekcje owned (Items, InspirationImages),
    // które przy materializacji encji dociągają się razem z nią i mnożą wiersze. Pętla niżej używa
    // wyłącznie tych sześciu skalarów.
    var appointments = await _context.Appointments
      .Where(a =>
        a.TenantId == TenantId
        && (isDesk || a.EmployeeId == callerEmployeeId!.Value)
        && a.Date >= request.From
        && a.Date <= request.To
        && a.Status != AppointmentStatus.Canceled
        && a.Status != AppointmentStatus.AwaitingOtp)
      .Select(a => new
      {
        a.Id,
        a.EmployeeId,
        a.ServiceId,
        a.Date,
        a.StartTime,
        a.EndTime,
      })
      .ToListAsync(ct);

    if (appointments.Count == 0)
    {
      return new List<OutsideScheduleNotificationDto>();
    }

    var employeeIds = appointments.Select(a => a.EmployeeId).Distinct().ToList();

    // Repozytorium, nie `_context.Employees` — i to jest tu istotne, nie kosmetyczne.
    //
    // `Employee` ma 10 kolekcji owned, które EF dociąga ZAWSZE (nie da się ich wyłączyć). Surowe
    // `_context.Employees...ToListAsync()` generowało z tego 10 JOIN-ów w jednym zapytaniu, czyli
    // iloczyn kartezjański pięciu rodzeństwa kolekcji: |Leaves| × |MonthPublications| × |Overrides|
    // × |Schedules| × |Services| wierszy — bez `AsNoTracking`, więc każdy z nich jeszcze lądował
    // w change trackerze. A ten endpoint jest pollowany przez panel CO 8 SEKUND (POLL_MS w
    // notification-center.service.ts) i po `fa5c2fd2` obejmuje CAŁY zespół, nie jednego pracownika.
    // To ta sama pułapka, która dała 5,4 s na detalu wizyty (preflight 2026-07-31, CRITICAL).
    //
    // `GetManyForAvailabilityAsync` jest zbudowane dokładnie pod ten przypadek: `AsSplitQuery`
    // (koniec kartezjanu), `AsNoTracking` (odczyt) i zawężenie Overrides/Leaves/MonthPublications
    // do okna dat zamiast całej historii salonu. Zwraca encje, więc `IsAvailable` niżej działa
    // bez zmian — nie da się tego zastąpić projekcją na skalary, bo logika dostępności czyta grafik.
    var employees = await _employees.GetManyForAvailabilityAsync(
      employeeIds, request.From, request.To, ct);

    var employeesById = employees.ToDictionary(e => e.Id);

    var serviceIds = appointments.Select(a => a.ServiceId).Distinct().ToList();
    var serviceNames = await _context.Services
      .Where(s => s.TenantId == TenantId && serviceIds.Contains(s.Id))
      .Select(s => new { s.Id, s.Name })
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    var result = new List<OutsideScheduleNotificationDto>();
    foreach (var a in appointments)
    {
      if (!employeesById.TryGetValue(a.EmployeeId, out var employee))
      {
        continue;
      }
      var range = new TimeRange(a.StartTime, a.EndTime);
      if (employee.IsAvailable(range, a.Date))
      {
        continue;
      }
      result.Add(new OutsideScheduleNotificationDto(
        a.Id,
        a.EmployeeId,
        $"{employee.FirstName} {employee.LastName}".Trim(),
        serviceNames.TryGetValue(a.ServiceId, out var sn) ? sn : null,
        a.Date,
        a.StartTime,
        a.EndTime
      ));
    }
    return result;
  }
}
