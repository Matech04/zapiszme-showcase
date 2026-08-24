using App.Application.Notifications;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Buduje treść SMS na bazie typu i payloadu powiadomienia. Twarde reguły kosztowe:
/// (1) <b>tylko ASCII</b> — polskie znaki ORAZ myślniki/wielokropki są transliterowane, żeby SMS
///     szedł jako GSM-7 (160 zn./segment), a nie UCS-2 (70 zn.) — jeden „obcy" znak windowałby
///     całość do UCS-2 i podwajał koszt;
/// (2) <b>max <see cref="MaxLength"/> znaków</b> — pola zmienne (nazwa salonu/klienta/usługi) są
///     przycinane, a całość ma twardy backstop, więc wiadomość zawsze mieści się w jednym segmencie.
/// </summary>
public static class SmsTextBuilder
{
  /// <summary>Twardy limit długości SMS-a. 1 segment GSM-7 = 160 zn.; 140 daje zapas.</summary>
  public const int MaxLength = 140;

  private const int SalonMax = 24;
  private const int CustomerMax = 20;
  private const int ServiceMax = 28;

  public static string Build(NotificationType type, NotificationPayload p)
  {
    var salon = Clip(p.SalonName, SalonMax);
    var customer = Clip(p.CustomerName, CustomerMax);
    var service = Clip(p.ServiceName, ServiceMax);
    var date = p.Date?.ToString("dd.MM") ?? string.Empty;
    var time = p.StartTime?.ToString("HH:mm") ?? string.Empty;
    var oldDate = p.OldDate?.ToString("dd.MM") ?? string.Empty;
    var oldTime = p.OldStartTime?.ToString("HH:mm") ?? string.Empty;

    var text = type switch
    {
      NotificationType.NewBookingToSalon =>
        $"{salon}: nowa rezerwacja online - {customer}, {service}, {date} {time}.",

      NotificationType.BookingConfirmationToCustomer =>
        $"{salon}: Twoja wizyta {date} {time} ({service}) potwierdzona. Do zobaczenia!",

      NotificationType.CancellationToSalon =>
        $"{salon}: klient {customer} anulował wizytę {date} {time} ({service}).",

      NotificationType.CancellationToCustomer =>
        $"{salon}: Twoja wizyta {date} {time} ({service}) została anulowana.",

      NotificationType.RescheduleToSalon =>
        $"{salon}: {customer} przełożył wizytę z {oldDate} {oldTime} na {date} {time} ({service}).",

      NotificationType.RescheduleToCustomer =>
        $"{salon}: Twoja wizyta ({service}) przeniesiona z {oldDate} {oldTime} na {date} {time}.",

      NotificationType.AppointmentReminderToCustomer =>
        $"{salon}: przypominamy o wizycie jutro {date} o {time} ({service}). Do zobaczenia!",

      NotificationType.AppointmentReminder2hToCustomer =>
        $"{salon}: Twoja wizyta dziś o {time} ({service}) - za ok. 2h. Do zobaczenia!",

      NotificationType.AwaitingConfirmationToSalon =>
        $"{salon}: nowa rezerwacja czeka na potwierdzenie - {customer}, {service}, {date} {time}.",

      NotificationType.CancelledBySalonToCustomer =>
        $"{salon}: niestety musimy odwołać wizytę {date} {time} ({service}). Przepraszamy.",

      NotificationType.RescheduledBySalonToCustomer =>
        $"{salon}: Twoja wizyta ({service}) przeniesiona z {oldDate} {oldTime} na {date} {time}.",

      NotificationType.StaffBookedAppointmentToCustomer =>
        $"{salon}: zarezerwowalismy dla Ciebie wizyte {date} {time} ({service}). Do zobaczenia!",

      // Link na końcu — prefiks jest krótki (salon clip 24 + kwota), więc URL nigdy nie jest ucinany.
      NotificationType.DepositLinkToCustomer =>
        $"{salon}: prosimy o zadatek {Clip(p.AmountText, 10)} za wizyte {date} {time}. Oplac: {p.ActionUrl}",

      _ => $"{salon}: powiadomienie.",
    };

    return Sanitize(text);
  }

  /// <summary>
  /// Wspólna sanityzacja kosztowa SMS — używana zarówno przez hardcoded szablony, jak i przez
  /// custom treści salonu (resolver). Transliteruje do ASCII (GSM-7), zwija białe znaki i przycina
  /// do <see cref="MaxLength"/>, więc wiadomość zawsze mieści się w jednym segmencie.
  /// </summary>
  public static string Sanitize(string text)
  {
    var result = ToAscii(NeutralizeMacros(Collapse(text ?? string.Empty)));
    return result.Length <= MaxLength ? result : result[..MaxLength].TrimEnd();
  }

  /// <summary>
  /// Sanityzacja dla CUSTOM szablonów, które świadomie ZACHOWUJĄ polskie znaki i emoji. Zwija białe
  /// znaki, NIE transliteruje, i przycina do limitu wg kodowania: treść z choćby jednym znakiem spoza
  /// GSM-7 (polski ogonek / emoji) → UCS-2, limit 70; sama łacina → GSM-7, limit 140. Nie tnie w środku
  /// pary surogatów UTF-16 (emoji), żeby nie zostawić uszkodzonego znaku na granicy.
  /// </summary>
  public static string SanitizeUnicode(string text)
  {
    var collapsed = NeutralizeMacros(Collapse(text ?? string.Empty));
    var limit = SmsLengthCalculator.LimitFor(collapsed);
    if (collapsed.Length <= limit)
    {
      return collapsed;
    }

    var end = limit;
    if (char.IsHighSurrogate(collapsed[end - 1]))
    {
      end--;
    }
    return collapsed[..end].TrimEnd();
  }

  /// <summary>
  /// Rozbraja składnię makr smsapi (<c>[%…%]</c>) w treści. smsapi interpretuje ją po swojej stronie
  /// (używamy tego świadomie dla skracacza idz.do), a treść zawiera pola sterowane przez użytkownika:
  /// imię klienta z publicznej rezerwacji, nazwę salonu, custom szablon. Bez tego anonim rezerwujący
  /// jako <c>[%idzdo:evil.pl%]</c> wstrzykuje salonowi do SMS-a link phishingowy na zaufanej domenie.
  ///
  /// Wołane PRZED <c>SmsNotificationChannel.ApplyLinkShortener</c>, które dokleja nasze własne,
  /// zaufane makro — kolejność jest tu istotna, inaczej rozbroilibyśmy sami siebie.
  /// </summary>
  internal static string NeutralizeMacros(string s) =>
    s.Replace("[%", "[ ", StringComparison.Ordinal)
     .Replace("%]", " ]", StringComparison.Ordinal);

  /// <summary>Przycina pole zmienne do <paramref name="max"/> znaków (po transliteracji długość
  /// się nie zmienia, więc liczymy na surowym wejściu).</summary>
  private static string Clip(string? value, int max)
  {
    var v = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    return v.Length <= max ? v : v[..max].TrimEnd();
  }

  private static string Collapse(string s) =>
    string.Join(" ", s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

  private static string ToAscii(string s)
  {
    var sb = new System.Text.StringBuilder(s.Length);
    foreach (var ch in s)
    {
      sb.Append(ch switch
      {
        'ą' => "a", 'ć' => "c", 'ę' => "e", 'ł' => "l", 'ń' => "n",
        'ó' => "o", 'ś' => "s", 'ź' => "z", 'ż' => "z",
        'Ą' => "A", 'Ć' => "C", 'Ę' => "E", 'Ł' => "L", 'Ń' => "N",
        'Ó' => "O", 'Ś' => "S", 'Ź' => "Z", 'Ż' => "Z",
        // Znaki spoza GSM-7, które inaczej wymusiłyby UCS-2:
        '—' or '–' => "-", '…' => "...", ' ' => " ",
        '„' or '”' or '“' => "\"", '’' or '‘' => "'",
        _ => ch.ToString(),
      });
    }
    return sb.ToString();
  }
}
