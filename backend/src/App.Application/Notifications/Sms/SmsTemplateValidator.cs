using System.Text.RegularExpressions;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;

namespace App.Application.Notifications.Sms;

/// <summary>
/// Reguły biznesowe treści custom szablonu SMS — wspólne dla zapisu salonu. Rzuca
/// <see cref="SmsTemplateException"/> z konkretnym error code, mapowanym na PL komunikat we froncie.
/// </summary>
public static class SmsTemplateValidator
{
  private static readonly Regex PlaceholderPattern =
    new(@"\{([a-zA-Z]+)\}", RegexOptions.Compiled);

  public static void Validate(NotificationType type, string body)
  {
    if (!SmsTemplatePlaceholders.IsCustomizable(type))
    {
      throw new SmsTemplateException(
        "Tego typu powiadomienia nie można personalizować.",
        ErrorCodes.SmsTemplateTypeNotCustomizable);
    }

    var trimmed = (body ?? string.Empty).Trim();
    if (trimmed.Length == 0)
    {
      throw new SmsTemplateException(
        "Treść szablonu SMS nie może być pusta.",
        ErrorCodes.SmsTemplateBodyEmpty);
    }

    // Limit zależy od kodowania: treść z polskimi/obcymi znakami → UCS-2 (70), inaczej GSM-7 (140).
    var limit = SmsLengthCalculator.LimitFor(trimmed);
    if (trimmed.Length > limit)
    {
      throw new SmsTemplateException(
        $"Treść przekracza limit {limit} znaków dla wybranego kodowania.",
        ErrorCodes.SmsTemplateTooLong);
    }

    // Makra smsapi (np. [%idzdo:url%]) są interpretowane przez dostawcę, nie przez nas. Moderator
    // (polskie UI) nie ma szans rozpoznać ich jako aktywnej składni, a zatwierdzony szablon leciałby
    // do wszystkich klientek salonu. Odrzucamy na wejściu — wysyłka i tak je rozbraja, ale nie chcemy
    // opierać bezpieczeństwa na jednej warstwie.
    if (trimmed.Contains("[%", StringComparison.Ordinal) || trimmed.Contains("%]", StringComparison.Ordinal))
    {
      throw new SmsTemplateException(
        "Treść nie może zawierać sekwencji [% ani %] — są zarezerwowane przez operatora SMS.",
        ErrorCodes.SmsTemplateInvalidPlaceholder);
    }

    // Tylko placeholdery dozwolone dla danego typu — inaczej salon mógłby wpisać token bez danych.
    var allowed = SmsTemplatePlaceholders.AllowedFor(type);
    foreach (Match m in PlaceholderPattern.Matches(trimmed))
    {
      var key = m.Groups[1].Value;
      if (!allowed.Contains(key))
      {
        throw new SmsTemplateException(
          $"Nieobsługiwany placeholder {{{key}}} dla tego typu powiadomienia.",
          ErrorCodes.SmsTemplateInvalidPlaceholder);
      }
    }
  }
}
