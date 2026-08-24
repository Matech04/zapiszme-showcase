using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Queries.GetCustomerChangedAppointments;

public record GetCustomerChangedAppointmentsQuery : IRequest<List<CustomerChangedAppointmentDto>>;

public record CustomerChangedAppointmentDto(
    Guid AppointmentId,
    int ChangeKind,
    DateTimeOffset ChangedAt,
    string? CustomerName,
    string? ServiceName,
    Guid EmployeeId,
    string? EmployeeName,
    DateOnly Date,
    TimeOnly StartTime
);

internal class GetCustomerChangedAppointmentsQueryHandler
    : TenantHandler<GetCustomerChangedAppointmentsQuery, List<CustomerChangedAppointmentDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly ICurrentUserAccessor _currentUser;

  public GetCustomerChangedAppointmentsQueryHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService,
      ICurrentUserAccessor currentUser)
      : base(currentTenantService)
  {
    _context = context;
    _currentUser = currentUser;
  }

  public override async Task<List<CustomerChangedAppointmentDto>> Handle(
      GetCustomerChangedAppointmentsQuery request,
      CancellationToken ct)
  {
    var since = DateTimeOffset.UtcNow.AddHours(-48);

    // Dzwonek osobisty: zmianę zrobioną przez klienta widzi pracownik, którego wizyta dotyczy.
    // Recepcja (Kiosk) oraz tryb wsparcia (Admin w impersonacji) obsługują cały zespół — widzą wszystkie.
    var isDesk = _currentUser.IsDeskAccount || _currentUser.IsSupportSession;
    var callerEmployeeId = _currentUser.CallerEmployeeId;
    if (!isDesk && callerEmployeeId is null)
    {
      return [];
    }

    var appointments = await _context.Appointments
      .Where(a =>
        a.TenantId == TenantId
        && (isDesk || a.EmployeeId == callerEmployeeId!.Value)
        && a.SelfServiceChangedAt != null
        && a.SelfServiceChangedAt >= since)
      .OrderByDescending(a => a.SelfServiceChangedAt)
      .Take(30)
      .ToListAsync(ct);

    if (appointments.Count == 0)
      return [];

    var customerIds = appointments
      .Where(a => a.CustomerId.HasValue)
      .Select(a => a.CustomerId!.Value)
      .Distinct()
      .ToList();

    var customerNames = await _context.Customers
      .IgnoreQueryFilters()
      .Where(c => customerIds.Contains(c.Id))
      .Select(c => new { c.Id, FirstName = c.FirstName, LastName = c.LastName })
      .ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim(), ct);

    var serviceIds = appointments.Select(a => a.ServiceId).Distinct().ToList();
    var serviceNames = await _context.Services
      .Where(s => serviceIds.Contains(s.Id))
      .Select(s => new { s.Id, s.Name })
      .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

    var employeeIds = appointments.Select(a => a.EmployeeId).Distinct().ToList();
    var employeeNames = await _context.Employees
      .Where(e => employeeIds.Contains(e.Id))
      .Select(e => new { e.Id, FirstName = e.FirstName, LastName = e.LastName })
      .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim(), ct);

    return appointments.Select(a => new CustomerChangedAppointmentDto(
      AppointmentId: a.Id,
      ChangeKind: a.SelfServiceChangeKind!.Value,
      ChangedAt: a.SelfServiceChangedAt!.Value,
      CustomerName: a.CustomerId.HasValue && customerNames.TryGetValue(a.CustomerId.Value, out var cn)
        ? cn : null,
      ServiceName: serviceNames.TryGetValue(a.ServiceId, out var sn) ? sn : null,
      // EmployeeId (nie tylko nazwa) — dashboard linkuje powiadomienie prosto na
      // /admin/schedule/:employeeId, omijając redirect z gołej trasy, który przeładowywał kalendarz.
      EmployeeId: a.EmployeeId,
      EmployeeName: employeeNames.TryGetValue(a.EmployeeId, out var en) ? en : null,
      Date: a.Date,
      StartTime: a.StartTime
    )).ToList();
  }
}
