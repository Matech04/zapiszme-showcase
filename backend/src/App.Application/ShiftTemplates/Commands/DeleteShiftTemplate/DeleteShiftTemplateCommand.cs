using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.ShiftTemplates.Commands.DeleteShiftTemplate;

public record DeleteShiftTemplateCommand(Guid Id) : IRequest;

internal class DeleteShiftTemplateCommandHandler : TenantHandler<DeleteShiftTemplateCommand>
{
  private readonly IShiftTemplateRepository _repository;
  private readonly IUnitOfWork _uow;

  public DeleteShiftTemplateCommandHandler(
    IShiftTemplateRepository repository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(DeleteShiftTemplateCommand request, CancellationToken ct)
  {
    var template = await _repository.GetByIdAsync(request.Id)
      ?? throw new NotFoundException(nameof(ShiftTemplate), request.Id);

    if (template.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    _repository.Remove(template);
    await _uow.SaveChangesAsync(ct);
  }
}
