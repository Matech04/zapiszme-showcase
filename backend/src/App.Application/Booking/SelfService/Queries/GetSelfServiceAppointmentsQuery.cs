using App.Application.Booking.SelfService.Dtos;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.SelfService.Queries;

public record GetSelfServiceAppointmentsQuery(string Slug, Guid SessionToken)
    : IRequest<List<SelfServiceAppointmentDto>>;

internal sealed class GetSelfServiceAppointmentsQueryHandler
    : IRequestHandler<GetSelfServiceAppointmentsQuery, List<SelfServiceAppointmentDto>>
{
  // Twardy sufit na liczbę zwracanych wizyt — kontrakt (List) zostaje, ale zapytanie nie jest
  // już nieograniczone. 200 z zapasem pokrywa historię klienta od fromDate; spójnie z capami
  // w GetCustomersQuery / GetNotificationsQuery.
  private const int MaxItems = 200;

  private readonly IApplicationDbContext _context;
  private readonly TimeProvider _timeProvider;

  public GetSelfServiceAppointmentsQueryHandler(IApplicationDbContext context, TimeProvider timeProvider)
  {
    _context = context;
    _timeProvider = timeProvider;
  }

  public async Task<List<SelfServiceAppointmentDto>> Handle(GetSelfServiceAppointmentsQuery request, CancellationToken ct)
  {
    var session = await SelfServiceSessionResolver.ResolveAsync(_context, request.Slug, request.SessionToken, _timeProvider, ct);

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
    var fromDate = DateOnly.FromDateTime(nowUtc.AddDays(-30));

    // Klienci z tym kontaktem (w tenancie). Może istnieć więcej niż jeden, jeżeli wcześniej był stub-Customer
    // i ręcznie utworzony rekord — zbieramy ich wszystkich.
    var customerQuery = _context.Customers
      .IgnoreQueryFilters()
      .Where(c => c.TenantId == session.TenantId && c.IsActive);

    if (session.Email != null)
    {
      // Kolumna Email jest już lowercase (Guard.NormalizeEmail) — porównujemy wprost bez LOWER() po
      // stronie SQL, który defeatował indeks (TenantId, Email).
      var email = session.Email.ToLowerInvariant();
      customerQuery = customerQuery.Where(c => c.Email != string.Empty && c.Email == email);
    }
    else if (session.PhoneE164 != null)
    {
      // Równość po value-objekcie tłumaczy się przez value converter na porównanie kolumny z
      // literałem (indeks (TenantId, PhoneNumber)). Session.PhoneE164 jest już zwalidowane.
      var phone = new PhoneNumber(session.PhoneE164);
      customerQuery = customerQuery.Where(c => c.PhoneNumber == phone);
    }
    else
    {
      return new List<SelfServiceAppointmentDto>();
    }

    // Sufit na listę klientów-duplikatów (stub + ręczny rekord to zwykle 1–2); chroni przed dużym
    // parametrem IN, gdyby na jeden kontakt narósł patologicznie duży zbiór stub-Customerów.
    // OrderBy przed Take — inaczej po przekroczeniu sufitu Postgres wybiera dowolną pięćdziesiątkę
    // i ten sam klient widziałby przy kolejnym wejściu inny zestaw swoich wizyt.
    var customerIds = await customerQuery.OrderBy(c => c.Id).Select(c => c.Id).Take(50).ToListAsync(ct);
    if (customerIds.Count == 0)
    {
      return new List<SelfServiceAppointmentDto>();
    }

    var rows = await (
      from a in _context.Appointments.AsNoTracking().IgnoreQueryFilters()
      where a.TenantId == session.TenantId
        && a.CustomerId != null
        && customerIds.Contains(a.CustomerId.Value)
        && a.Date >= fromDate
      join e in _context.Employees.IgnoreQueryFilters() on a.EmployeeId equals e.Id
      join s in _context.Services.IgnoreQueryFilters() on a.ServiceId equals s.Id
      orderby a.Date descending, a.StartTime descending
      select new
      {
        a.Id,
        a.EmployeeId,
        a.ServiceId,
        a.Date,
        a.StartTime,
        a.EndTime,
        a.Status,
        EmployeeFirstName = e.FirstName,
        EmployeeLastName = e.LastName,
        ServiceName = s.Name
      }).Take(MaxItems).ToListAsync(ct);

    // Pełne combo wg pozycji — osobnym zapytaniem (SelectMany jest tłumaczone na SQL, w odróżnieniu
    // od korelowanej projekcji kolekcji w joinie). Złożenie wg AppointmentId robimy w pamięci.
    var apptIds = rows.Select(r => r.Id).ToList();
    var comboItems = await _context.Appointments.AsNoTracking().IgnoreQueryFilters()
      .Where(a => a.TenantId == session.TenantId && apptIds.Contains(a.Id))
      .SelectMany(a => a.Items)
      .Select(i => new { i.AppointmentId, i.ServiceId, i.Position })
      .ToListAsync(ct);
    var comboByAppointment = comboItems
      .GroupBy(i => i.AppointmentId)
      .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Position).Select(i => i.ServiceId).ToList());

    var todayUtc = DateOnly.FromDateTime(nowUtc);

    return rows.Select(r =>
    {
      var isFuture = r.Date >= todayUtc;
      var isCancelable = isFuture && r.Status != AppointmentStatus.Canceled && r.Status != AppointmentStatus.Completed;
      var isReschedulable = isCancelable;

      // Fallback na usługę główną — wizyty sprzed modelu combo mogą nie mieć pozycji.
      var serviceIds = comboByAppointment.TryGetValue(r.Id, out var ids) && ids.Count > 0
        ? ids
        : new List<Guid> { r.ServiceId };

      return new SelfServiceAppointmentDto(
        r.Id,
        r.EmployeeId,
        r.ServiceId,
        serviceIds,
        r.ServiceName,
        r.EmployeeFirstName,
        r.EmployeeLastName,
        r.Date,
        r.StartTime,
        r.EndTime,
        r.Status.Name,
        isCancelable,
        isReschedulable);
    }).ToList();
  }
}
