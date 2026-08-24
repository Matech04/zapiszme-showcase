using App.Application.Common.Email;
using App.Application.Notifications;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Notifications;

/// <summary>
/// Kanał e-mail systemu powiadomień. Buduje <see cref="EmailDocument"/> z <see cref="NotificationPayload"/>
/// (per typ), renderuje go marką salonu i deleguje wysyłkę do <see cref="IEmailTransport"/>.
/// Gdy odbiorca nie ma adresu e-mail — pomija.
/// </summary>
public sealed class EmailNotificationChannel : INotificationChannel
{
  private readonly IEmailTransport _transport;
  private readonly INotificationUsageRecorder _usage;
  private readonly ILogger<EmailNotificationChannel> _logger;

  public EmailNotificationChannel(
    IEmailTransport transport,
    INotificationUsageRecorder usage,
    ILogger<EmailNotificationChannel> logger)
  {
    _transport = transport;
    _usage = usage;
    _logger = logger;
  }

  public NotificationChannelKind Kind => NotificationChannelKind.Email;

  public async Task<NotificationDeliveryStatus> SendAsync(NotificationMessage message, CancellationToken ct)
  {
    var email = message.Recipient.Email;
    if (string.IsNullOrWhiteSpace(email))
    {
      _logger.LogDebug("Pomijam e-mail dla {Type} — odbiorca bez adresu e-mail.", message.Type);
      return NotificationDeliveryStatus.Skipped;
    }

    // Markę ustawia dispatcher z encji Tenant. Gdy jej nie ma (tenant zniknął między zdarzeniem
    // a wysyłką) — składamy ją z samej nazwy salonu z payloadu, na kolorze domyślnym.
    var brand = message.Brand ?? EmailBrand.ForTenant(message.Payload.SalonName, accentHex: null);
    var document = BuildDocument(message.Type, message.Payload) with { Preheader = message.ShortText };

    try
    {
      await _transport.SendAsync(email, message.Subject, EmailRenderer.Render(document, brand), ct);
    }
    catch
    {
      await _usage.RecordEmailAsync(message.TenantId, message.Type, success: false, ct);
      throw;
    }

    await _usage.RecordEmailAsync(message.TenantId, message.Type, success: true, ct);
    return NotificationDeliveryStatus.Sent;
  }

  /// <summary>
  /// Czysta funkcja typ+payload → treść. Publiczna, żeby testy sprawdzały strukturę wiadomości,
  /// a nie kruche fragmenty wyrenderowanego HTML-a.
  /// </summary>
  public static EmailDocument BuildDocument(NotificationType type, NotificationPayload p) => type switch
  {
    NotificationType.NewBookingToSalon => new EmailDocument
    {
      Heading = "Nowa rezerwacja online",
      Paragraphs = [$"Witaj {p.StaffName}, masz nową rezerwację online."],
      Details = CustomerContact(p),
      Footnote = "Wizyta jest już w Twoim grafiku.",
    },

    NotificationType.BookingConfirmationToCustomer => new EmailDocument
    {
      Heading = "Rezerwacja potwierdzona",
      Paragraphs = [$"Witaj {p.CustomerName}, Twoja wizyta w salonie {p.SalonName} została potwierdzona."],
      Details = Visit(p),
      Footnote = "Do zobaczenia!",
    },

    NotificationType.CancellationToSalon => new EmailDocument
    {
      Heading = "Rezerwacja anulowana",
      Paragraphs = [$"Witaj {p.StaffName}, {p.CustomerName} anulował(a) swoją wizytę."],
      Details = ServiceWhen(p),
      Footnote = "Termin jest teraz wolny — możesz go przydzielić komuś innemu.",
    },

    NotificationType.CancellationToCustomer => new EmailDocument
    {
      Heading = "Wizyta anulowana",
      Paragraphs = [$"Witaj {p.CustomerName}, Twoja wizyta w salonie {p.SalonName} została anulowana."],
      Details = ServiceWhen(p),
      Footnote = "Jeśli to pomyłka, skontaktuj się z salonem lub zarezerwuj nowy termin.",
    },

    NotificationType.RescheduleToSalon => new EmailDocument
    {
      Heading = "Rezerwacja przełożona",
      Paragraphs = [$"Witaj {p.StaffName}, {p.CustomerName} przełożył(a) swoją wizytę."],
      Details = Rescheduled(p),
      Footnote = "Poprzedni termin jest teraz wolny.",
    },

    NotificationType.RescheduleToCustomer => new EmailDocument
    {
      Heading = "Wizyta przełożona",
      Paragraphs = [$"Witaj {p.CustomerName}, Twoja wizyta w salonie {p.SalonName} została przełożona."],
      Details = Rescheduled(p),
      Footnote = "Do zobaczenia w nowym terminie!",
    },

    NotificationType.AppointmentReminderToCustomer => new EmailDocument
    {
      Heading = "Przypomnienie o jutrzejszej wizycie",
      Paragraphs = [$"Witaj {p.CustomerName}, przypominamy o wizycie jutro w salonie {p.SalonName}."],
      Details = Visit(p),
    },

    NotificationType.AppointmentReminder2hToCustomer => new EmailDocument
    {
      Heading = "Twoja wizyta już wkrótce",
      Paragraphs = [$"Witaj {p.CustomerName}, Twoja wizyta w salonie {p.SalonName} odbędzie się już niedługo."],
      Details = Visit(p),
      Footnote = "Do zobaczenia!",
    },

    NotificationType.AwaitingConfirmationToSalon => new EmailDocument
    {
      Heading = "Rezerwacja czeka na potwierdzenie",
      Paragraphs = [$"Witaj {p.StaffName}, nowa rezerwacja online wymaga Twojego potwierdzenia."],
      Details = CustomerContact(p),
      Footnote = "Potwierdź lub odrzuć wizytę w panelu salonu.",
    },

    NotificationType.CancelledBySalonToCustomer => new EmailDocument
    {
      Heading = "Salon odwołał Twoją wizytę",
      Paragraphs = [$"Witaj {p.CustomerName}, z przykrością informujemy, że salon {p.SalonName} odwołał Twoją wizytę."],
      Details = ServiceWhen(p),
      Footnote = "W razie pytań skontaktuj się z salonem lub zarezerwuj nowy termin.",
    },

    NotificationType.RescheduledBySalonToCustomer => new EmailDocument
    {
      Heading = "Salon przełożył Twoją wizytę",
      Paragraphs = [$"Witaj {p.CustomerName}, salon {p.SalonName} przełożył Twoją wizytę na nowy termin."],
      Details = Rescheduled(p),
      Footnote = "Jeśli nowy termin Ci nie odpowiada, skontaktuj się z salonem.",
    },

    NotificationType.StaffBookedAppointmentToCustomer => new EmailDocument
    {
      Heading = "Zarezerwowano dla Ciebie wizytę",
      Paragraphs = [$"Witaj {p.CustomerName}, salon {p.SalonName} zarezerwował dla Ciebie wizytę."],
      Details = Visit(p),
      Footnote = "Do zobaczenia! Jeśli termin Ci nie odpowiada, skontaktuj się z salonem.",
    },

    NotificationType.DepositLinkToCustomer => new EmailDocument
    {
      Heading = "Zadatek do opłacenia",
      Paragraphs = [$"Witaj {p.CustomerName}, aby potwierdzić wizytę w salonie {p.SalonName}, prosimy o wpłatę zadatku."],
      Details =
      [
        new EmailDetail("Usługa", p.ServiceName),
        new EmailDetail("Data", Date(p.Date)),
        new EmailDetail("Godzina", Time(p.StartTime)),
        new EmailDetail("Zadatek", p.AmountText),
      ],
      Cta = p.ActionUrl is null ? null : new EmailCallToAction("Zapłać zadatek", p.ActionUrl),
    },

    _ => new EmailDocument { Heading = p.SalonName },
  };

  // Wiersze z pustą wartością renderer pomija — stąd bezwarunkowe „Telefon" i „E-mail".
  private static EmailDetail[] CustomerContact(NotificationPayload p) =>
  [
    new EmailDetail("Klient", p.CustomerFullName),
    new EmailDetail("Telefon", p.CustomerPhone),
    new EmailDetail("E-mail", p.CustomerEmail),
    new EmailDetail("Usługa", p.ServiceName),
    new EmailDetail("Data", Date(p.Date)),
    new EmailDetail("Godzina", Time(p.StartTime)),
  ];

  private static EmailDetail[] Visit(NotificationPayload p) =>
  [
    new EmailDetail("Usługa", p.ServiceName),
    new EmailDetail("Pracownik", p.StaffName),
    new EmailDetail("Data", Date(p.Date)),
    new EmailDetail("Godzina", Time(p.StartTime)),
  ];

  private static EmailDetail[] ServiceWhen(NotificationPayload p) =>
  [
    new EmailDetail("Usługa", p.ServiceName),
    new EmailDetail("Data", Date(p.Date)),
    new EmailDetail("Godzina", Time(p.StartTime)),
  ];

  private static EmailDetail[] Rescheduled(NotificationPayload p) =>
  [
    new EmailDetail("Usługa", p.ServiceName),
    new EmailDetail("Poprzedni termin", $"{Date(p.OldDate)}, {Time(p.OldStartTime)}"),
    new EmailDetail("Nowy termin", $"{Date(p.Date)}, {Time(p.StartTime)}"),
  ];

  private static string Date(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "—";

  private static string Time(TimeOnly? time) => time?.ToString("HH:mm") ?? "—";
}
