using App.Application.Appointments.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments.Queries.GetAppointmentById;

public record GetAppointmentByIdQuery(Guid Id) : IRequest<AppointmentDto>;

internal class GetAppointmentByIdHandler : TenantHandler<GetAppointmentByIdQuery, AppointmentDto>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;

  public GetAppointmentByIdHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
    _access = access;
  }
  public override async Task<AppointmentDto> Handle(GetAppointmentByIdQuery request, CancellationToken ct)
  {

    var query = _context.Appointments.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.Id == request.Id);


    // Materializujemy encję wizyty, potem mapujemy w pamięci — pozwala bezpiecznie użyć
    // PaymentStatus.ToString() (bez ryzyka translacji enum→SQL) oraz owned Money DepositAmount.
    // IgnoreQueryFilters + LEFT JOIN na Employees/Services: detal wizyty historycznej musi się
    // otwierać nawet po soft-delete pracownika/usługi (IsActive=false). Bez tego globalny filtr
    // wycina nieaktywny rekord, INNER JOIN gubi wizytę i handler rzuca NotFoundException.
    // Klient przeciwnie — BEZ IgnoreQueryFilters, żeby po soft-delete zwrócić puste dane osobowe.
    //
    // WYDAJNOŚĆ — nie zamieniaj tych projekcji z powrotem na `select new { employee, service }`.
    // Joinowanie ENCJI ładowało cały agregat `Employee`, a jego kolekcji owned (Services, Leaves,
    // Schedules→ScheduleDays→WorkRanges/Breaks, Overrides→ScheduleDay→…) EF pominąć NIE POTRAFI —
    // owned collections jadą zawsze z rodzicem. Razem z kolekcjami wizyty dawało to iloczyn
    // kartezjański (EF sygnalizował to `MultipleCollectionIncludeWarning`): zmierzone na produkcji
    // stałe ~2,8 s na wywołanie przy BEZCZYNNYM serwerze i 482 wizytach w bazie, rosnące do 20 s
    // pod obciążeniem. Projekcja na skalary nie materializuje kolekcji owned.
    //
    // Zostaje iloczyn dwóch kolekcji samej wizyty (Items × InspirationImages) i EF dalej wypisze
    // `MultipleCollectionIncludeWarning` — świadomie go zostawiamy: obie kolekcje są jednocyfrowe,
    // a `AsSplitQuery` żyje w pakiecie .Relational, którego ta warstwa celowo nie referencuje.
    var row = await (from appointment in query
                     join employee in _context.Employees.IgnoreQueryFilters() on appointment.EmployeeId equals employee.Id into empJoin
                     from employee in empJoin.DefaultIfEmpty()
                     join service in _context.Services.IgnoreQueryFilters() on appointment.ServiceId equals service.Id into svcJoin
                     from service in svcJoin.DefaultIfEmpty()
                     join customer in _context.Customers on appointment.CustomerId equals customer.Id into custJoin
                     from customer in custJoin.DefaultIfEmpty()
                     select new
                     {
                       appointment,
                       EmployeeFirstName = employee != null ? employee.FirstName : "",
                       EmployeeLastName = employee != null ? employee.LastName : "",
                       ServiceName = service != null ? service.Name : "",
                       CustomerFirstName = customer != null ? customer.FirstName : "",
                       CustomerLastName = customer != null ? customer.LastName : "",
                       // PhoneNumber ma ValueConverter → `.Value` NIE przetłumaczy się na SQL.
                       // Bierzemy całą wartość, rozpakowujemy w pamięci niżej.
                       CustomerPhoneNumber = customer != null ? customer.PhoneNumber : null,
                       CustomerInstagramNick = customer != null ? customer.InstagramNick : null,
                     })
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

    if (row is null)
    {
      throw new NotFoundException(nameof(Appointment), request.Id);
    }

    // Nieistniejąca wizyta = 404 (wyżej), cudza przy `OwnCalendarOnly` = 403. Kolejność ma znaczenie:
    // nie chcemy ujawniać istnienia wizyty przez rozróżnienie kodów... a jednak tak było dotąd i
    // siatka z fazy 0 to utrwala. Zmiana semantyki byłaby zmianą zachowania, nie refaktorem.
    await _access.EnsureCanViewEmployeeCalendarAsync(row.appointment.EmployeeId, ct);

    // Czy salon może pobierać zadatki (zadatki włączone + konto Stripe gotowe) — steruje
    // widocznością akcji „Generuj zadatek" w panelu. CanAcceptDeposits to property obliczeniowa,
    // więc liczymy w pamięci (EF nie przetłumaczy jej do SQL).
    var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == TenantId, ct);
    var depositsAvailable = tenant?.CanAcceptDeposits ?? false;

    var a = row.appointment;
    // Pozycje combo: nazwy usług dołączamy na żywo (IgnoreQueryFilters — usługa mogła zostać zdezaktywowana).
    var itemServiceIds = a.Items.Select(i => i.ServiceId).ToList();
    var serviceNames = await _context.Services.IgnoreQueryFilters().AsNoTracking()
        .Where(s => itemServiceIds.Contains(s.Id))
        .Select(s => new { s.Id, s.Name })
        .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

    // Fallback nazwy: gdy główna usługa została soft-deletowana (row.ServiceName puste),
    // a nazwy z combo-itemów też brak — używamy pustego stringa zamiast NRE.
    var inspirationImages = a.InspirationImages
        .Select(i => new AppointmentInspirationImageDto(i.Url, i.ThumbnailUrl))
        .ToList();

    var primaryServiceName = row.ServiceName ?? string.Empty;
    var serviceItems = a.Items
        .OrderBy(i => i.Position)
        .Select(i => new AppointmentServiceItemDto(
            i.ServiceId,
            serviceNames.TryGetValue(i.ServiceId, out var name) ? name : primaryServiceName,
            i.DurationMinutes,
            i.Price,
            i.Position))
        .ToList();

    return new AppointmentDto(
        a.Id,
        a.EmployeeId,
        a.ServiceId,
        a.CustomerId,
        !a.CustomerId.HasValue,
        row.EmployeeFirstName,
        row.EmployeeLastName,
        row.CustomerFirstName,
        row.CustomerLastName,
        row.CustomerPhoneNumber != null ? row.CustomerPhoneNumber.Value : "",
        row.CustomerInstagramNick,
        primaryServiceName,
        a.Date,
        a.StartTime,
        a.EndTime,
        a.Status,
        a.TotalPrice,
        a.AppointmentNotes,
        a.PaymentStatus.ToString(),
        a.DepositAmount,
        a.PaidAtUtc,
        a.PaymentLinkUrl,
        a.LinkExpiresAtUtc,
        a.IsDepositLinkExpired(DateTime.UtcNow),
        a.DepositLinkSentAtUtc,
        a.DepositLinkSentChannel,
        a.DepositLinkAttempts,
        a.ExpiredDepositLinkCount,
        depositsAvailable,
        a.FinalPrice,
        serviceItems,
        inspirationImages,
        a.CustomDurationMinutes);
  }
}