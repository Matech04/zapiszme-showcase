using App.Application.Appointments.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.AppointmentAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments.Queries.GetAppointmentsByRange;

// Walidacja (cap zakresu dat 366 dni + niepuste daty) jest w App.Application.Common.Validation.InputValidators
// (GetAppointmentsByRangeQueryValidator). Nie duplikujemy jej tutaj.
public record GetAppointmentsByRangeQuery(DateOnly StartDate, DateOnly EndDate, Guid? EmployeeId) : IRequest<List<AppointmentPreviewDto>>;

internal class GetAppointmentsByRangeHandler : TenantHandler<GetAppointmentsByRangeQuery, List<AppointmentPreviewDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;

  public GetAppointmentsByRangeHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _access = access;
  }

  public override async Task<List<AppointmentPreviewDto>> Handle(GetAppointmentsByRangeQuery request, CancellationToken ct)
  {
    // Wygasły anonimowy hold OTP (AwaitingOtp z przeterminowaną dzierżawą) NIE jest realną wizytą —
    // slot jest już wolny (tak liczy go dostępność: GetAvailableTimeSlotsQuery), a job cyklu życia
    // i tak go zaraz usunie. Bez tego filtra taki hold pokazywał się w kalendarzu jako szary kafelek
    // „Oczekuje na kod" na godzinie, która faktycznie jest wolna. Booked/Pending/Canceled zwracamy
    // bez zmian (Canceled jest opt-in po stronie kalendarza).
    var nowUtc = DateTime.UtcNow;

    // Zakres ustalamy PRZED zapytaniem: pracownik z `OwnCalendarOnly` nie pobiera już cudzych wizyt
    // tylko po to, żeby je odrzucić. Jawne pytanie o cudzy kalendarz przy tej polityce = 403.
    var employeeScope = await _access.ResolveCalendarReadScopeAsync(request.EmployeeId, ct);

    var query = _context.Appointments.AsNoTracking()
        .Where(a => a.TenantId == TenantId && a.Date >= request.StartDate && a.Date <= request.EndDate
                    && !(a.Status == AppointmentStatus.AwaitingOtp && a.Lease!.ExpiryTimeUtc < nowUtc));

    if (employeeScope.HasValue)
    {
      query = query.Where(a => a.EmployeeId == employeeScope.Value);
    }

    // IgnoreQueryFilters + LEFT JOIN na Employees/Services: wizyta historyczna musi pozostać
    // widoczna w kalendarzu, nawet gdy pracownik lub usługa zostały soft-deletowane (IsActive=false).
    // Bez IgnoreQueryFilters globalny filtr wycina nieaktywne rekordy, a INNER JOIN usuwa całą wizytę.
    // Customer pozostaje LEFT JOIN (wizyty anonimowe). TenantId nadal filtrowany jawnie powyżej.
    var result = await (from appointment in query
                        join employee in _context.Employees.IgnoreQueryFilters() on appointment.EmployeeId equals employee.Id into empJoin
                        from employee in empJoin.DefaultIfEmpty()
                        join service in _context.Services.IgnoreQueryFilters() on appointment.ServiceId equals service.Id into svcJoin
                        from service in svcJoin.DefaultIfEmpty()
                        join customer in _context.Customers on appointment.CustomerId equals customer.Id into custJoin
                        from customer in custJoin.DefaultIfEmpty()
                        select new AppointmentPreviewDto(
                            appointment.Id,
                            appointment.EmployeeId,
                            employee != null ? employee.FirstName : "",
                            employee != null ? employee.LastName : "",
                            customer != null ? customer.FirstName : "",
                            customer != null ? customer.LastName : "",
                            customer != null && customer.PhoneNumber != null ? customer.PhoneNumber.Value : "",
                            customer != null ? customer.Email : "",
                            service != null ? service.Name : "",
                            appointment.Date,
                            appointment.StartTime,
                            appointment.EndTime,
                            appointment.Status,
                            !appointment.CustomerId.HasValue
                        )).ToListAsync(ct);

    return result;
  }
}
