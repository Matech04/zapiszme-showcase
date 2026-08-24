using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications.Sms;

/// <summary>
/// Katalog placeholderów dostępnych w custom szablonach SMS oraz biała lista typów powiadomień,
/// które salon może personalizować (wyłącznie DO KLIENTA; NIGDY OTP ani *ToSalon).
///
/// Placeholdery są spójne PL: <c>{salon}</c>, <c>{klient}</c>, <c>{usluga}</c>, <c>{data}</c>,
/// <c>{godzina}</c>, <c>{pracownik}</c>, <c>{staraData}</c>, <c>{staraGodzina}</c>, <c>{kwota}</c>,
/// <c>{link}</c>. Per typ udostępniamy tylko te, które niosą sens (np. {staraData} tylko przy
/// przełożeniu, {link}/{kwota} tylko przy linku do zadatku).
/// </summary>
public static class SmsTemplatePlaceholders
{
  public const string Salon = "salon";
  public const string Klient = "klient";
  public const string Usluga = "usluga";
  public const string Data = "data";
  public const string Godzina = "godzina";
  public const string Pracownik = "pracownik";
  public const string StaraData = "staraData";
  public const string StaraGodzina = "staraGodzina";
  public const string Kwota = "kwota";
  public const string Link = "link";

  /// <summary>
  /// Typy powiadomień DO KLIENTA, które salon może personalizować. WYKLUCZONE: wszystkie *ToSalon
  /// oraz <see cref="NotificationType.CustomerVerificationOtp"/>.
  /// </summary>
  public static readonly IReadOnlyList<NotificationType> CustomizableTypes = new[]
  {
    NotificationType.BookingConfirmationToCustomer,
    NotificationType.AppointmentReminderToCustomer,
    NotificationType.AppointmentReminder2hToCustomer,
    NotificationType.CancellationToCustomer,
    NotificationType.RescheduleToCustomer,
    NotificationType.CancelledBySalonToCustomer,
    NotificationType.RescheduledBySalonToCustomer,
    NotificationType.StaffBookedAppointmentToCustomer,
    NotificationType.DepositLinkToCustomer,
  };

  public static bool IsCustomizable(NotificationType type) => CustomizableTypes.Contains(type);

  private static readonly string[] BaseFields =
    { Salon, Klient, Usluga, Data, Godzina, Pracownik };

  /// <summary>Zwraca placeholdery dozwolone dla danego typu (bez nawiasów klamrowych).</summary>
  public static IReadOnlyList<string> AllowedFor(NotificationType type) => type switch
  {
    NotificationType.RescheduleToCustomer or NotificationType.RescheduledBySalonToCustomer =>
      BaseFields.Concat(new[] { StaraData, StaraGodzina }).ToArray(),

    NotificationType.DepositLinkToCustomer =>
      new[] { Salon, Klient, Usluga, Data, Godzina, Pracownik, Kwota, Link },

    _ => BaseFields,
  };
}
