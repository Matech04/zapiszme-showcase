using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Services.Commands;
using App.Application.Services.Commands.SetServiceEmployees;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Services.Commands.CreateService;

/// <summary>
/// Tworzy nową usługę. Opcjonalnie pozwala od razu przypisać pracowników, którzy
/// będą ją oferować (multi-select w formularzu UI) — bez tego trzeba osobno wchodzić
/// w profil pracownika i dodawać tam usługę.
/// </summary>
public record CreateServiceCommand(
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
    List<ServiceImageInput>? Images = null) : IRequest<Guid>;

internal class CreateServiceHandler : TenantHandler<CreateServiceCommand, Guid>
{
  private readonly IServiceRepository _repository;
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly ISender _mediator;

  public CreateServiceHandler(
      IServiceRepository repository,
      IApplicationDbContext context,
      IUnitOfWork uow,
      ICurrentTenantService currentTenantService,
      ISender mediator)
      : base(currentTenantService)
  {
    _repository = repository;
    _context = context;
    _uow = uow;
    _mediator = mediator;
  }

  public override async Task<Guid> Handle(CreateServiceCommand request, CancellationToken ct)
  {
    // Jeśli kategoria podana, musi istnieć w bieżącej tenancy (HasQueryFilter
    // automatycznie wycina inne tenanty — cross-tenant categoryId zwróci 404).
    if (request.CategoryId is { } catId)
    {
      var exists = await _context.ServiceCategories.AnyAsync(c => c.Id == catId, ct);
      if (!exists) throw new NotFoundException(nameof(ServiceCategory), catId);
    }

    var price = new Money(request.Amount, request.Currency);
    var service = new Service(
        TenantId,
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

    if (!request.IsAddon && request.AddonServiceIds is { Count: > 0 })
    {
      await ServiceAddonValidator.EnsureAddonsValidAsync(_context, request.AddonServiceIds, service.Id, ct);
      service.SetAddons(request.AddonServiceIds);
    }

    if (request.Images is { Count: > 0 })
    {
      service.SetImages(request.Images.Select(i => new ServiceImageData(i.Url, i.ThumbnailUrl, i.Key)));
    }

    await _repository.AddAsync(service);
    await _uow.SaveChangesAsync(ct);

    // Jeśli formularz wskazał pracowników — od razu utwórz przypisania.
    // Pomijamy gdy lista null (back-compat); pusta lista celowo czyściłaby przypisania
    // — przy tworzeniu nowej usługi i tak nikt jeszcze nie ma assignmentu.
    if (request.EmployeeIds is { Count: > 0 })
    {
      await _mediator.Send(new SetServiceEmployeesCommand(service.Id, request.EmployeeIds), ct);
    }

    return service.Id;
  }
}
