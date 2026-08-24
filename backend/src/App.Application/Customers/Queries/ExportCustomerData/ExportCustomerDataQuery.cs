using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Customers.Queries.ExportCustomerData;

/// <summary>
/// RODO art. 15 (dostęp) + art. 20 (przenoszalność). Zwraca komplet danych osobowych klienta
/// wraz z historią wizyt w ustrukturyzowanym formacie (JSON) — do wydania klientowi na żądanie.
/// Scope per tenant (klient salonu); inne salony niewidoczne.
/// </summary>
public record ExportCustomerDataQuery(Guid Id) : IRequest<CustomerDataExportDto>;

public record CustomerDataExportDto(
  Guid Id,
  string FirstName,
  string LastName,
  string Email,
  string? PhoneNumber,
  string? InstagramNick,
  DateOnly CreatedAt,
  string GeneralNotes,
  bool IsActive,
  bool IsWhitelisted,
  IReadOnlyList<CustomerAppointmentExportDto> Appointments,
  DateTimeOffset ExportedAtUtc);

public record CustomerAppointmentExportDto(
  Guid AppointmentId,
  DateOnly Date,
  TimeOnly StartTime,
  TimeOnly EndTime,
  string Status,
  string ServiceName,
  decimal TotalPrice,
  string Currency,
  string Notes);

internal class ExportCustomerDataHandler : TenantHandler<ExportCustomerDataQuery, CustomerDataExportDto>
{
  private readonly IApplicationDbContext _context;
  private readonly TimeProvider _timeProvider;

  public ExportCustomerDataHandler(
    IApplicationDbContext context,
    TimeProvider timeProvider,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _timeProvider = timeProvider;
  }

  public override async Task<CustomerDataExportDto> Handle(ExportCustomerDataQuery request, CancellationToken ct)
  {
    // IgnoreQueryFilters — eksport musi działać też dla soft-deletowanego klienta (art. 15 obowiązuje
    // niezależnie od statusu IsActive). Tenant ograniczamy ręcznie.
    var customer = await _context.Customers
      .AsNoTracking()
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(c => c.Id == request.Id && c.TenantId == TenantId, ct)
      ?? throw new NotFoundException(nameof(Customer), request.Id);

    var appointments = await (
      from a in _context.Appointments.AsNoTracking().IgnoreQueryFilters()
      where a.TenantId == TenantId && a.CustomerId == request.Id
      join s in _context.Services.AsNoTracking().IgnoreQueryFilters() on a.ServiceId equals s.Id into sj
      from s in sj.DefaultIfEmpty()
      orderby a.Date, a.StartTime
      select new CustomerAppointmentExportDto(
        a.Id,
        a.Date,
        a.StartTime,
        a.EndTime,
        a.Status.Name,
        s != null ? s.Name : string.Empty,
        a.TotalPrice.Amount,
        a.TotalPrice.Currency,
        a.AppointmentNotes))
      .ToListAsync(ct);

    return new CustomerDataExportDto(
      customer.Id,
      customer.FirstName,
      customer.LastName,
      customer.Email,
      customer.PhoneNumber?.Value,
      customer.InstagramNick,
      customer.CreatedAt,
      customer.GeneralNotes,
      customer.IsActive,
      customer.IsWhitelisted,
      appointments,
      _timeProvider.GetUtcNow());
  }
}
