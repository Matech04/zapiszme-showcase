using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Services.Commands;
using App.Application.Services.Commands.SetServiceEmployees;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Services.Commands.UpdateService;

/// <summary>
/// Aktualizuje istniejącą usługę. Opcjonalne pole <paramref name="EmployeeIds"/>
/// pozwala jednocześnie ustawić listę pracowników świadczących usługę
/// (multi-select w formularzu Service edit). null = pomiń synchronizację assignments
/// (back-compat dla callerów, którzy nie chcą tego dotykać); pusta lista = wyczyść
/// wszystkie przypisania.
/// </summary>
public record UpdateServiceCommand(
    Guid Id,
    Guid? CategoryId,
    Guid VatRateId,
    string Name,
    decimal Amount,
    string Currency,
    int DurationInMinutes,
    List<Guid>? EmployeeIds = null,
    decimal? MaxAmount = null,
    int? DurationMinMinutes = null,
    int? DurationMaxMinutes = null,
    string? ComboGroup = null,
    bool HidePrice = false,
    bool IsAddon = false,
    List<Guid>? AddonServiceIds = null,
    string? Description = null,
    List<ServiceImageInput>? Images = null) : IRequest;

internal class UpdateServiceHandler : TenantHandler<UpdateServiceCommand>
{
  private readonly IServiceRepository _repository;
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly ISender _mediator;
  private readonly IFileStorage _storage;
  private readonly ILogger<UpdateServiceHandler> _logger;

  public UpdateServiceHandler(
      IServiceRepository repository,
      IApplicationDbContext context,
      IUnitOfWork uow,
      ICurrentTenantService currentTenantService,
      ISender mediator,
      IFileStorage storage,
      ILogger<UpdateServiceHandler> logger)
      : base(currentTenantService)
  {
    _repository = repository;
    _context = context;
    _uow = uow;
    _mediator = mediator;
    _storage = storage;
    _logger = logger;
  }

  public override async Task Handle(UpdateServiceCommand request, CancellationToken ct)
  {
    var service = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(Service), request.Id);

    if (service.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    if (request.CategoryId is { } catId)
    {
      var exists = await _context.ServiceCategories.AnyAsync(c => c.Id == catId, ct);
      if (!exists) throw new NotFoundException(nameof(ServiceCategory), catId);
    }

    var price = new Money(request.Amount, request.Currency);
    service.Update(
        request.CategoryId,
        request.VatRateId,
        request.Name,
        price,
        request.DurationInMinutes,
        request.MaxAmount,
        request.DurationMinMinutes,
        request.DurationMaxMinutes,
        request.ComboGroup,
        request.HidePrice,
        request.IsAddon,
        request.Description);

    // Synchronizacja dodatków: null = pomiń (back-compat), lista = ustaw. Gdy usługa jest dodatkiem,
    // SetAddons/Update i tak czyści listę (dodatek nie ma własnych dodatków).
    if (request.AddonServiceIds is not null)
    {
      if (!request.IsAddon && request.AddonServiceIds.Count > 0)
      {
        await ServiceAddonValidator.EnsureAddonsValidAsync(_context, request.AddonServiceIds, service.Id, ct);
      }

      service.SetAddons(request.AddonServiceIds);
    }

    // Synchronizacja galerii: null = pomiń (back-compat), lista = reconcile (pusta = wyczyść galerię).
    // SetImages zwraca klucze zdjęć usuniętych — kasujemy je ze storage best-effort (PO udanym zapisie).
    IReadOnlyList<string> removedStorageKeys = [];
    if (request.Images is not null)
    {
      removedStorageKeys = service.SetImages(
        request.Images.Select(i => new ServiceImageData(i.Url, i.ThumbnailUrl, i.Key)));
    }

    _repository.Update(service);
    await _uow.SaveChangesAsync(ct);

    // Best-effort sprzątanie storage — porażka NIE może blokować zapisu usługi (DB już commit).
    foreach (var key in removedStorageKeys)
    {
      try
      {
        await _storage.DeleteAsync(key, ct);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Nie udało się usunąć osieroconego zdjęcia usługi ze storage (klucz: {StorageKey}).", key);
      }
    }

    // Synchronizacja assignments tylko jeśli caller jawnie podał listę
    // (null = pomiń, pusta = wyczyść wszystkie).
    if (request.EmployeeIds is not null)
    {
      await _mediator.Send(new SetServiceEmployeesCommand(service.Id, request.EmployeeIds), ct);
    }
  }
}
