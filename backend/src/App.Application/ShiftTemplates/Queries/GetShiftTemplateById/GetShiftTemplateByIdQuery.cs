using App.Application.Common;
using App.Application.ShiftTemplates.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.ShiftTemplates.Queries.GetShiftTemplateById;

public record GetShiftTemplateByIdQuery(Guid Id) : IRequest<ShiftTemplateDto>;

internal class GetShiftTemplateByIdQueryHandler : TenantHandler<GetShiftTemplateByIdQuery, ShiftTemplateDto>
{
  private readonly IShiftTemplateRepository _repository;

  public GetShiftTemplateByIdQueryHandler(
    IShiftTemplateRepository repository,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _repository = repository;
  }

  public override async Task<ShiftTemplateDto> Handle(GetShiftTemplateByIdQuery request, CancellationToken ct)
  {
    var template = await _repository.GetByIdAsync(request.Id)
      ?? throw new NotFoundException(nameof(ShiftTemplate), request.Id);

    if (template.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    return new ShiftTemplateDto(
      template.Id,
      template.Name,
      template.SlotGenerationMode,
      template.ScheduleDay.WorkRanges.Select(r => new TimeRangeDto(r.StartTime, r.EndTime)).ToList(),
      template.ScheduleDay.Breaks.Select(b => new TimeRangeDto(b.StartTime, b.EndTime)).ToList(),
      template.ScheduleDay.FixedStartTimes.ToList()
    );
  }
}
