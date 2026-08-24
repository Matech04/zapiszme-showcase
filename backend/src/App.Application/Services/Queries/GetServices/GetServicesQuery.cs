using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Services.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Services.Queries.GetServices;

public record GetServicesQuery(Guid? categoryId) : IRequest<List<ServiceDto>>;

internal class GetServicesHandler : TenantHandler<GetServicesQuery, List<ServiceDto>>
{
  private readonly IApplicationDbContext _context;

  public GetServicesHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  /// <summary>
  /// WYDAJNOŚĆ — nie scalaj tego z powrotem w jedną projekcję z trzema kolekcjami.
  /// Poprzednia wersja ciągnęła w jednym SELECT `Images`, `Addons` ORAZ `EmployeeIds`, co daje
  /// iloczyn kartezjański: baza zwraca `|obrazki| × |dodatki| × |pracownicy|` wierszy NA KAŻDĄ
  /// usługę, a EF deduplikuje je z powrotem w pamięci. EF sygnalizował to ostrzeżeniem
  /// `MultipleCollectionIncludeWarning` („either via Include or through projection").
  ///
  /// Zmierzone na produkcji: 82 wiersze zamiast 21 (salon z 5 obrazkami i 3 dodatkami przy usłudze)
  /// oraz 44 zamiast 7 (salon z 4 pracownikami na usługę). Dziś to narzut nieduży w liczbach
  /// bezwzględnych, ale MNOŻNIKOWY — przy 10 zdjęciach, 5 dodatkach i 8 pracownikach jedna usługa
  /// to już 400 wierszy, a galeria zdjęć usług dopiero się zapełnia.
  ///
  /// `AsSplitQuery` odpada: żyje w pakiecie .Relational, którego ta warstwa celowo nie referencuje
  /// (ten sam powód i to samo obejście co w `GetAppointmentByIdQuery` i `GetEmployeeSchedulesQuery`).
  /// Stąd cztery płaskie zapytania i złożenie w pamięci.
  /// </summary>
  public override async Task<List<ServiceDto>> Handle(GetServicesQuery request, CancellationToken ct)
  {
    var query = _context.Services.Where(x => x.TenantId == TenantId);

    if (request.categoryId != null)
    {
      query = query.Where(x => x.CategoryId == request.categoryId);
    }

    // 1) Same skalary — to one wyznaczają kolejność i liczbę pozycji odpowiedzi.
    var services = await query
      .OrderBy(x => x.OrderIndex)
      .ThenBy(x => x.Name)
      .AsNoTracking()
      .Select(x => new
      {
        x.Id,
        x.CategoryId,
        x.VatRateId,
        x.Name,
        x.Price,
        x.DurationInMinutes,
        x.PriceMaxAmount,
        x.DurationMinMinutes,
        x.DurationMaxMinutes,
        x.ComboGroup,
        x.HidePrice,
        x.OrderIndex,
        x.IsAddon,
        x.Description,
      })
      .ToListAsync(ct);

    if (services.Count == 0)
    {
      return [];
    }

    var serviceIds = services.Select(s => s.Id).ToList();

    // 2-4) Kolekcje osobno, każda płaska. `SelectMany` tłumaczy się na SQL (w odróżnieniu od
    // korelowanej projekcji kolekcji), więc wracają zwykłe wiersze `(ServiceId, wartość)`.
    var images = await _context.Services
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId && serviceIds.Contains(x.Id))
      .SelectMany(x => x.Images.Select(i => new
      {
        ServiceId = x.Id,
        i.Url,
        i.ThumbnailUrl,
        i.StorageKey,
        i.OrderIndex,
      }))
      .OrderBy(i => i.ServiceId)
      .ThenBy(i => i.OrderIndex)
      .ToListAsync(ct);

    var addons = await _context.Services
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId && serviceIds.Contains(x.Id))
      .SelectMany(x => x.Addons.Select(a => new { ServiceId = x.Id, a.AddonServiceId }))
      .OrderBy(a => a.ServiceId)
      .ThenBy(a => a.AddonServiceId)
      .ToListAsync(ct);

    // Pracownicy BEZ jawnego filtra tenanta i aktywności — tak jak poprzednio, bo `_context.Employees`
    // ma globalny query filter (TenantId + IsActive). Zachowanie pozostaje identyczne.
    var employeeAssignments = await _context.Employees
      .AsNoTracking()
      .SelectMany(e => e.Services
        .Where(es => serviceIds.Contains(es.ServiceId))
        .Select(es => new { es.ServiceId, EmployeeId = e.Id }))
      .OrderBy(x => x.ServiceId)
      .ThenBy(x => x.EmployeeId)
      .ToListAsync(ct);

    var imagesByService = images
      .GroupBy(i => i.ServiceId)
      .ToDictionary(
        g => g.Key,
        g => g.Select(i => new ServiceImageDto(i.Url, i.ThumbnailUrl, i.StorageKey, i.OrderIndex)).ToList());

    var addonsByService = addons
      .GroupBy(a => a.ServiceId)
      .ToDictionary(g => g.Key, g => g.Select(a => a.AddonServiceId).ToList());

    var employeesByService = employeeAssignments
      .GroupBy(x => x.ServiceId)
      .ToDictionary(g => g.Key, g => g.Select(x => x.EmployeeId).ToList());

    return services
      .Select(r => new ServiceDto(
        r.Id,
        r.CategoryId,
        r.VatRateId,
        r.Name,
        r.Price,
        r.DurationInMinutes,
        // Kontrakt DTO: kolekcje NIGDY nie są null — brak wpisów to pusta lista, tak jak przy
        // poprzedniej projekcji korelowanej.
        employeesByService.TryGetValue(r.Id, out var employeeIds) ? employeeIds : [],
        r.PriceMaxAmount,
        r.DurationMinMinutes,
        r.DurationMaxMinutes,
        r.ComboGroup,
        r.HidePrice,
        r.OrderIndex,
        r.IsAddon,
        addonsByService.TryGetValue(r.Id, out var addonIds) ? addonIds : [],
        r.Description,
        imagesByService.TryGetValue(r.Id, out var serviceImages) ? serviceImages : []))
      .ToList();
  }
}
