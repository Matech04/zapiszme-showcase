using App.Application.Onboarding.Catalog;
using MediatR;

namespace App.Application.Onboarding.Queries.GetIndustryTemplates;

public sealed record TemplateServiceDto(string Name, decimal Price, int DurationMinutes);

public sealed record IndustryTemplateDto(
  string Key,
  string Label,
  string? CategoryName,
  IReadOnlyList<TemplateServiceDto> Services);

public sealed record GetIndustryTemplatesResult(IReadOnlyList<IndustryTemplateDto> Industries);

/// <summary>
/// Zwraca statyczny katalog szablonów branżowych do kroku „Czym się zajmujesz?" / „Twoje usługi".
/// Nie zależy od tenanta (treść produktowa) — dlatego zwykły <c>IRequestHandler</c>, nie <c>TenantHandler</c>.
/// </summary>
public sealed record GetIndustryTemplatesQuery : IRequest<GetIndustryTemplatesResult>;

internal sealed class GetIndustryTemplatesQueryHandler
  : IRequestHandler<GetIndustryTemplatesQuery, GetIndustryTemplatesResult>
{
  public Task<GetIndustryTemplatesResult> Handle(GetIndustryTemplatesQuery request, CancellationToken ct)
  {
    var industries = IndustryTemplateCatalog.All
      .Select(t => new IndustryTemplateDto(
        t.Key,
        t.Label,
        t.CategoryName,
        t.Services.Select(s => new TemplateServiceDto(s.Name, s.Price, s.DurationMinutes)).ToList()))
      .ToList();

    return Task.FromResult(new GetIndustryTemplatesResult(industries));
  }
}
