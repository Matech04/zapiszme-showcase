namespace App.Application.Notifications.Sms;

/// <summary>
/// Liczy długość SMS i ustala limit segmentu wg kodowania: GSM-7 (bez polskich/obcych znaków) =
/// 160 zn., ale używamy bezpiecznego progu 140; UCS-2 (gdy treść zawiera CHOĆBY JEDEN znak spoza
/// GSM-7, np. polski ogonek) = 70 zn. To ta sama granica, którą pokazuje licznik w UI.
///
/// Walidacja backendowa liczy NA SUROWEJ treści szablonu (z placeholderami), bo to ona decyduje
/// o limicie kodowania — wartości placeholderów są transliterowane do ASCII przy wysyłce, więc nie
/// windują kodowania, ale literał szablonu już tak.
/// </summary>
public static class SmsLengthCalculator
{
  public const int GsmLimit = 140;
  public const int UnicodeLimit = 70;

  // Podstawowy alfabet GSM 03.38 + rozszerzenie (znaki zajmujące 2 jednostki, ale wciąż GSM-7).
  private const string GsmBasic =
    "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?"
    + "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

  private const string GsmExtension = "^{}\\[~]|€";

  /// <summary>True, gdy treść zawiera znak spoza GSM-7 → wymusza kodowanie UCS-2 (limit 70).</summary>
  public static bool RequiresUnicode(string text)
  {
    foreach (var ch in text ?? string.Empty)
    {
      if (GsmBasic.IndexOf(ch) < 0 && GsmExtension.IndexOf(ch) < 0)
      {
        return true;
      }
    }
    return false;
  }

  /// <summary>Limit znaków dla danej treści (140 GSM-7 / 70 UCS-2).</summary>
  public static int LimitFor(string text) => RequiresUnicode(text) ? UnicodeLimit : GsmLimit;
}
