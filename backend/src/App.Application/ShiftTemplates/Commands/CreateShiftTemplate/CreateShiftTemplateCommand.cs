using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.ShiftTemplates.Common;
using App.Domain.Aggregates.EmployeeAggregate;
using MediatR;

namespace App.Application.ShiftTemplates.Commands.CreateShiftTemplate;

public record CreateShiftTemplateCommand(
  string Name,
  SlotGenerationMode SlotGenerationMode,
  IReadOnlyCollection<TimeRangeDto> WorkRanges,
  IReadOnlyCollection<TimeRangeDto> Breaks,
  IReadOnlyCollection<TimeOnly>? FixedStartTimes
) : IRequest<Guid>;

internal class CreateShiftTemplateCommandHandler : TenantHandler<CreateShiftTemplateCommand, Guid>
{
  private readonly IShiftTemplateRepository _repository;
  private readonly IUnitOfWork _uow;

  public CreateShiftTemplateCommandHandler(
    IShiftTemplateRepository repository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task<Guid> Handle(CreateShiftTemplateCommand request, CancellationToken ct)
  {
    var scheduleDay = ShiftTemplateScheduleDayFactory.Build(
      request.SlotGenerationMode, request.WorkRanges, request.Breaks, request.FixedStartTimes);

    var template = new ShiftTemplate(TenantId, request.Name, scheduleDay, request.SlotGenerationMode);
    await _repository.AddAsync(template);
    await _uow.SaveChangesAsync(ct);

    return template.Id;
  }
}
