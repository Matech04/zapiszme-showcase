using App.Application.Common;
using App.Application.ShiftTemplates.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using MediatR;

namespace App.Application.ShiftTemplates.Queries.GetShiftTemplates;

public record GetShiftTemplatesQuery : IRequest<List<ShiftTemplateDto>>;

internal class GetShiftTemplatesQueryHandler : TenantHandler<GetShiftTemplatesQuery, List<ShiftTemplateDto>>
{
  private readonly IShiftTemplateRepository _repository;

  public GetShiftTemplatesQueryHandler(
    IShiftTemplateRepository repository,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _repository = repository;
  }

  public override async Task<List<ShiftTemplateDto>> Handle(GetShiftTemplatesQuery request, CancellationToken ct)
  {
    var templates = await _repository.GetByTenantAsync(TenantId);

    return templates.Select(t => new ShiftTemplateDto(
      t.Id,
      t.Name,
      t.SlotGenerationMode,
      t.ScheduleDay.WorkRanges.Select(r => new TimeRangeDto(r.StartTime, r.EndTime)).ToList(),
      t.ScheduleDay.Breaks.Select(b => new TimeRangeDto(b.StartTime, b.EndTime)).ToList(),
      t.ScheduleDay.FixedStartTimes.ToList()
    )).ToList();
  }
}
