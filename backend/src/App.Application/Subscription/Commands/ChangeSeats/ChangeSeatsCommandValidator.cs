using App.Application.Common.Validation;
using FluentValidation;

namespace App.Application.Subscription.Commands.ChangeSeats;

public sealed class ChangeSeatsCommandValidator : AppValidator<ChangeSeatsCommand>
{
  public ChangeSeatsCommandValidator()
  {
    RuleFor(x => x.NewSeats)
      .GreaterThanOrEqualTo(1)
      .WithMessage("Liczba stanowisk musi być >= 1.");
  }
}
