using FluentValidation;

namespace App.Application.Notifications.SmsTemplates.Commands.SubmitSmsTemplate;

/// <summary>
/// Strukturalna walidacja zapisu szablonu (niepusta treść, twardy backstop długości). Reguły zależne
/// od kodowania i placeholderów per typ egzekwuje <c>SmsTemplateValidator</c> w handlerze (granularne
/// error code dla UI).
/// </summary>
public class SubmitSmsTemplateCommandValidator : AbstractValidator<SubmitSmsTemplateCommand>
{
  public SubmitSmsTemplateCommandValidator()
  {
    RuleFor(c => c.Body)
      .NotEmpty()
      // Backstop niezależny od kodowania — handler dokłada limit 70/140 wg znaków.
      .MaximumLength(320);
  }
}
