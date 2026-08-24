using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Onboarding.Catalog;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Onboarding.Commands.ApplyIndustryTemplate;

/// <summary>Jedna usługa wybrana/edytowana przez właściciela w kroku „Twoje usługi".</summary>
public sealed record TemplateServiceSelection(string Name, decimal Price, int DurationMinutes);

/// <summary>
/// Zapisuje branżę salonu i tworzy WYBRANE usługi (front przysyła możliwie zmodyfikowany/odchudzony
/// zestaw — salon nigdy nie ma usług, których właściciel nie widział). Tworzy jedną kategorię,
/// usługi w niej i przypisuje każdą do pracownika-właściciela. Pusta lista (branża „other" lub
/// wszystko odznaczone) = tylko zapis branży.
/// </summary>
public sealed record ApplyIndustryTemplateCommand(
  string IndustryKey,
  IReadOnlyList<TemplateServiceSelection> Services) : IRequest<ApplyIndustryTemplateResult>;

public sealed record ApplyIndustryTemplateResult(Guid? CategoryId, int ServicesCreated);

public sealed class ApplyIndustryTemplateCommandValidator : AbstractValidator<ApplyIndustryTemplateCommand>
{
  public ApplyIndustryTemplateCommandValidator()
  {
    RuleFor(x => x.IndustryKey)
      .NotEmpty()
      .Must(key => IndustryTemplateCatalog.Find(key) != null)
      .WithMessage("Nieznana branża.");

    RuleFor(x => x.Services).NotNull();
    RuleForEach(x => x.Services).ChildRules(s =>
    {
      s.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
      s.RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
      s.RuleFor(x => x.DurationMinutes).GreaterThan(0);
    });
  }
}

internal sealed class ApplyIndustryTemplateCommandHandler
  : TenantHandler<ApplyIndustryTemplateCommand, ApplyIndustryTemplateResult>
{
  private readonly IApplicationDbContext _context;

  public ApplyIndustryTemplateCommandHandler(
    IApplicationDbContext context,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<ApplyIndustryTemplateResult> Handle(
    ApplyIndustryTemplateCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == TenantId, ct)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    tenant.SetIndustry(request.IndustryKey);

    // Idempotencja: usługi tworzymy TYLKO gdy salon nie ma jeszcze żadnej kategorii (świeży
    // onboarding). Ponowny submit / powrót nie duplikuje katalogu — jedynie odświeża branżę.
    var hasCategories = await _context.ServiceCategories.AnyAsync(c => c.TenantId == TenantId, ct);

    if (request.Services.Count == 0 || hasCategories)
    {
      await _context.SaveChangesAsync(ct);
      return new ApplyIndustryTemplateResult(null, 0);
    }

    // Pracownik-właściciel: jedyny (na tym etapie) pracownik z kontem logowania w tym salonie.
    var owner = await _context.Employees.FirstOrDefaultAsync(e => e.UserId != null, ct)
      ?? throw new NotFoundException(nameof(Employee), TenantId);

    var vatRate = await _context.VatRates.FirstOrDefaultAsync(v => v.IsDefault, ct)
      ?? await _context.VatRates.FirstOrDefaultAsync(ct)
      ?? throw new NotFoundException(nameof(VatRate), TenantId);

    var categoryName = IndustryTemplateCatalog.Find(request.IndustryKey)?.CategoryName ?? "Usługi";
    var category = new ServiceCategory(TenantId, categoryName, 0);
    _context.ServiceCategories.Add(category);

    var created = 0;
    foreach (var selection in request.Services)
    {
      var price = new Money(selection.Price, tenant.Currency);
      var service = new Service(
        TenantId,
        category.Id,
        vatRate.Id,
        selection.Name,
        price,
        selection.DurationMinutes);
      _context.Services.Add(service);
      owner.AssignService(TenantId, service.Id, selection.DurationMinutes, price);
      created++;
    }

    await _context.SaveChangesAsync(ct);

    return new ApplyIndustryTemplateResult(category.Id, created);
  }
}
