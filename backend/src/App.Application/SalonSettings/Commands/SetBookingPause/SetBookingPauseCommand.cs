using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;

namespace App.Application.SalonSettings.Commands.SetBookingPause;

/// <summary>
/// Żądanie przełączenia wstrzymania rezerwacji salonu (instant toggle z panelu właściciela / banera).
/// <paramref name="Message"/> jest opcjonalny i istotny tylko gdy <paramref name="Paused"/> = true.
/// </summary>
public record SetBookingPauseRequest(bool Paused, string? Message = null);

public record SetBookingPauseCommand(bool Paused, string? Message = null) : IRequest;

public class SetBookingPauseCommandValidator : AbstractValidator<SetBookingPauseCommand>
{
  public SetBookingPauseCommandValidator()
  {
    RuleFor(x => x.Message)
      .MaximumLength(Tenant.BookingPauseMessageMaxLength)
      .WithMessage($"Komunikat wstrzymania rezerwacji może mieć maksymalnie {Tenant.BookingPauseMessageMaxLength} znaków.");
  }
}

internal class SetBookingPauseCommandHandler : TenantHandler<SetBookingPauseCommand>
{
  private readonly ITenantRepository _repository;
  private readonly IUnitOfWork _uow;

  public SetBookingPauseCommandHandler(
    ICurrentTenantService currentTenantService,
    ITenantRepository repository,
    IUnitOfWork uow)
    : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
  }

  public override async Task Handle(SetBookingPauseCommand request, CancellationToken ct)
  {
    var tenant = await _repository.GetByIdAsync(TenantId)
        ?? throw new NotFoundException(nameof(Tenant), TenantId);

    tenant.SetBookingPause(request.Paused, request.Message);
    _repository.Update(tenant);
    await _uow.SaveChangesAsync(ct);
  }
}
