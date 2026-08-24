using System.Net;

namespace App.Infrastructure.Email;

/// <summary>Escapowanie tekstu wstawianego do HTML-a maila. Używane wyłącznie przez <see cref="EmailRenderer"/>.</summary>
internal static class EmailHtml
{
  public static string Encode(string value) => WebUtility.HtmlEncode(value);
}
