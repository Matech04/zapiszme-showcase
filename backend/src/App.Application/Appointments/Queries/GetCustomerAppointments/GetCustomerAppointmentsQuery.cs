using App.Application.Appointments.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments.Queries.GetCustomerAppointments;

public record GetCustomerAppointmentsQuery(Guid Id) : IRequest<List<AppointmentPreviewDto>>;

internal class GetCustomerAppointmentsHandler : TenantHandler<GetCustomerAppointmentsQuery, List<AppointmentPreviewDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;

  public GetCustomerAppointmentsHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _access = access;
  }
  public override async Task<List<AppointmentPreviewDto>> Handle(GetCustomerAppointmentsQuery request, CancellationToken ct)
  {
    // ASYMETRIA, zamierzona i utrwalona w siatce z fazy 0: ten endpoint NIGDY nie zwraca 403.
    // Pracownik z `OwnCalendarOnly` dostaje historię klienta zawężoną do WŁASNYCH wizyt, bo pyta
    // o klienta, nie o cudzy kalendarz. Zawężamy w zapytaniu, a nie po fakcie jak dawniej kontroler.
    var employeeScope = await _access.ResolveCalendarReadScopeAsync(null, ct);

    var query = _context.Appointments.AsNoTracking()
        .Where(a => a.TenantId == TenantId && a.CustomerId == request.Id);

    if (employeeScope.HasValue)
    {
      query = query.Where(a => a.EmployeeId == employeeScope.Value);
    }

    // Historia MUSI być zachowana także dla zdezaktywowanych pracowników/usług (soft-delete).
    // Joiny korzystają z `IgnoreQueryFilters()`, bo globalny filtr niesie `IsActive` — bez tego
    // wizyty z dezaktywowanym pracownikiem lub usługą cicho znikają z historii (i z liczników KPI).
    // Izolacja tenanta zachowana: `appointment` jest już zawężony do TenantId + CustomerId, a integralność
    // FK gwarantuje, że joinowane encje należą do tego samego tenanta.
    var result = await (from appointment in query
                        join employee in _context.Employees.IgnoreQueryFilters() on appointment.EmployeeId equals employee.Id
                        // Rzutowanie na `Guid?` jest KONIECZNE: `Appointment.CustomerId` jest
                        // nullowalne (wizyta gościa), a `Customer.Id` nie. Bez wyrównania typów
                        // C# nie wnioskuje wspólnego klucza i boksuje oba do `object`, czego EF
                        // nie tłumaczy — całe zapytanie kończyło się wyjątkiem i HTTP 500.
                        join customer in _context.Customers.IgnoreQueryFilters() on appointment.CustomerId equals customer.Id
                        join service in _context.Services.IgnoreQueryFilters() on appointment.ServiceId equals service.Id
                        orderby appointment.Date descending, appointment.StartTime descending
                        select new AppointmentPreviewDto(
                            appointment.Id,
                            appointment.EmployeeId,
                            employee.FirstName,
                            employee.LastName,
                            customer.FirstName,
                            customer.LastName,
                            customer.PhoneNumber == null ? "" : customer.PhoneNumber.Value,
                            customer.Email,
                            service.Name,
                            appointment.Date,
                            appointment.StartTime,
                            appointment.EndTime,
                            appointment.Status,
                            false
                        ))
                        // Twardy limit — historia klienta jest prezentacyjna; bez capa zapytanie
                        // jest nieograniczone (klient z tysiącami wizyt → duży payload/pamięć).
                        //
                        // Determinizm limitu zapewnia `orderby` WYŻEJ, w składni zapytaniowej —
                        // wykonuje się przed `Take`, więc obcinany jest najstarszy ogon.
                        // NIE dokładaj tu `.OrderByDescending(x => ...)`: to sortowanie po polach
                        // ZPROJEKTOWANEGO DTO, którego EF nie tłumaczy — całe zapytanie wywala się
                        // wtedy wyjątkiem i endpoint zwraca 500 (zdarzyło się i trafiło na produkcję).
                        .Take(500)
                        .ToListAsync(ct);

    return result;
  }
}