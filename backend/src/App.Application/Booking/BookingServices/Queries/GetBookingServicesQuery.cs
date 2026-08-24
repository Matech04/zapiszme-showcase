using System.Linq.Expressions;
using App.Application.Booking.BookingServices.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ServiceAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.BookingServices.Queries;

/// <param name="CategoryId">Opcjonalny filtr kategorii.</param>
/// <param name="EmployeeId">Opcjonalny pracownik. Gdy podany, zwracane są WYŁĄCZNIE usługi, które
/// ten pracownik oferuje, a cena/czas są zresolvowane per-pracownik (override
/// <see cref="App.Domain.Aggregates.EmployeeAggregate.EmployeeService.CustomPrice"/> /
/// <c>CustomDuration</c>, z fallbackiem do katalogu usługi). Dzięki temu klient widzi właściwą cenę
/// TEGO pracownika, a nie domyślną katalogową. <c>null</c> = katalog (dowolny pracownik).</param>
public record GetBookingServicesQuery(Guid? CategoryId, Guid? EmployeeId = null) : IRequest<List<BookingServiceDto>>;

internal class GetBookingServicesQueryHandler : TenantHandler<GetBookingServicesQuery, List<BookingServiceDto>>
{
  private readonly IApplicationDbContext _context;

  public GetBookingServicesQueryHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  /// <summary>
  /// Projekcja Service → publiczne DTO z ceną/czasem KATALOGOWYM. Dla wariantu per-pracownik
  /// cena/czas są nadpisywane w pamięci (override to owned <c>Money</c> — warunek <c>??</c> nie
  /// przetłumaczyłby się w projekcji EF).
  /// </summary>
  private static readonly Expression<Func<Service, BookingServiceDto>> ToDto = x => new BookingServiceDto(
      x.Id, x.CategoryId, x.Name, x.Price, x.DurationInMinutes,
      x.PriceMaxAmount, x.DurationMinMinutes, x.DurationMaxMinutes, x.ComboGroup,
      x.HidePrice, x.OrderIndex,
      x.IsAddon, x.Addons.Select(a => a.AddonServiceId).ToList(),
      x.Description,
      x.Images
        .OrderBy(i => i.OrderIndex)
        .Select(i => new BookingServiceImageDto(i.Url, i.ThumbnailUrl, i.OrderIndex))
        .ToList());

  public override async Task<List<BookingServiceDto>> Handle(GetBookingServicesQuery request, CancellationToken ct)
  {
    return request.EmployeeId is { } employeeId
        ? await HandleForEmployee(employeeId, request.CategoryId, ct)
        : await HandleCatalog(request.CategoryId, ct);
  }

  /// <summary>Katalog (bez kontekstu pracownika): wszystkie usługi z ≥1 aktywnym pracownikiem, cena katalogowa.</summary>
  private async Task<List<BookingServiceDto>> HandleCatalog(Guid? categoryId, CancellationToken ct)
  {
    var query = _context.Services.Where(x => x.TenantId == TenantId);

    if (categoryId != null)
    {
      query = query.Where(x => x.CategoryId == categoryId);
    }

    // Tylko usługi, do których przypisany jest co najmniej jeden aktywny pracownik (EmployeeServices).
    query = query.Where(s =>
        _context.Employees
            .Where(e => e.TenantId == TenantId)
            .SelectMany(e => e.Services)
            .Any(es => es.ServiceId == s.Id));

    return await query
        .OrderBy(x => x.OrderIndex)
        .ThenBy(x => x.Name)
        .AsNoTracking()
        .Select(ToDto)
        .ToListAsync(ct);
  }

  /// <summary>
  /// Usługi oferowane przez konkretnego pracownika, z ceną/czasem zresolvowanym per-pracownik
  /// (override <c>CustomPrice</c>/<c>CustomDuration</c>, fallback do katalogu). Override to owned
  /// <c>Money</c>, więc materializujemy usługi z ceną katalogową i nadpisujemy w pamięci.
  /// </summary>
  private async Task<List<BookingServiceDto>> HandleForEmployee(Guid employeeId, Guid? categoryId, CancellationToken ct)
  {
    // Overrides tego pracownika (owned Money → materializujemy encje, resolve w pamięci).
    var overrides = await _context.Employees
        .AsNoTracking()
        .Where(e => e.Id == employeeId && e.TenantId == TenantId)
        .SelectMany(e => e.Services)
        .ToListAsync(ct);

    if (overrides.Count == 0)
    {
      return [];
    }

    var overrideMap = overrides
        .GroupBy(o => o.ServiceId)
        .ToDictionary(g => g.Key, g => g.First());
    var serviceIds = overrideMap.Keys.ToList();

    var query = _context.Services.Where(x => x.TenantId == TenantId && serviceIds.Contains(x.Id));

    if (categoryId != null)
    {
      query = query.Where(x => x.CategoryId == categoryId);
    }

    var dtos = await query
        .OrderBy(x => x.OrderIndex)
        .ThenBy(x => x.Name)
        .AsNoTracking()
        .Select(ToDto)
        .ToListAsync(ct);

    return dtos
        .Select(d => overrideMap.TryGetValue(d.Id, out var o)
            ? d with
            {
              Price = o.CustomPrice ?? d.Price,
              DurationInMinutes = o.CustomDuration ?? d.DurationInMinutes,
            }
            : d)
        .ToList();
  }
}
