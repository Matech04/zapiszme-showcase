using FluentValidation;

namespace App.Application.Notifications.Queries.GetNotificationUsage;

public class GetNotificationUsageQueryValidator : AbstractValidator<GetNotificationUsageQuery>
{
  public GetNotificationUsageQueryValidator()
  {
    When(q => q.Month is not null, () =>
      RuleFor(q => q.Month!.Value).InclusiveBetween(1, 12)
        .WithMessage("Miesiąc musi być z zakresu 1–12."));

    When(q => q.Year is not null, () =>
      RuleFor(q => q.Year!.Value).InclusiveBetween(2000, 2200)
        .WithMessage("Rok poza dozwolonym zakresem."));
  }
}
