using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.ShiftTemplates.Common;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.ShiftTemplates.Commands.UpdateShiftTemplate;

public record UpdateShiftTemplateCommand(
  Guid Id,
  string Name,
  SlotGenerationMode SlotGenerationMode,
  IReadOnlyCollection<TimeRangeDto> WorkRanges,
  IReadOnlyCollection<TimeRangeDto> Breaks,
  IReadOnlyCollection<TimeOnly>? FixedStartTimes
) : IRequest;

internal class UpdateShiftTemplateCommandHandler : TenantHandler<UpdateShiftTemplateCommand>
{
  private readonly IShiftTemplateRepository _repository;
  private readonly IUnitOfWork _uow;

  public UpdateShiftTemplateCommandHandler(
    IShiftTemplateRepository repository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(UpdateShiftTemplateCommand request, CancellationToken ct)
  {
    var template = await _repository.GetByIdAsync(request.Id)
      ?? throw new NotFoundException(nameof(ShiftTemplate), request.Id);

    if (template.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    var scheduleDay = ShiftTemplateScheduleDayFactory.Build(
      request.SlotGenerationMode, request.WorkRanges, request.Breaks, request.FixedStartTimes);

    template.Update(request.Name, scheduleDay, request.SlotGenerationMode);
    await _uow.SaveChangesAsync(ct);
  }
}
