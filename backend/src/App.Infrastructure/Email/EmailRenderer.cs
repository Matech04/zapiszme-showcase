using System.Text;
using App.Application.Common.Email;

namespace App.Infrastructure.Email;

/// <summary>
/// Renderuje <see cref="EmailDocument"/> do HTML-a i wersji tekstowej. Jedyne miejsce w kodzie,
/// które wie jak wygląda mail.
///
/// Ograniczenia klientów pocztowych, których trzymamy się świadomie:
/// layout na zagnieżdżonych tabelach (Outlook renderuje przez Worda — bez flex/grid),
/// wyłącznie style inline (Gmail obcina &lt;style&gt; w niektórych widokach),
/// szerokość 600px (klasyczna bezpieczna szerokość okna podglądu),
/// bez obrazków (blokowane domyślnie u wielu odbiorców).
/// </summary>
public static class EmailRenderer
{
  private const string PageBackground = "#f4f4f5";
  private const string CardBackground = "#ffffff";
  private const string TextColor = "#374151";
  private const string HeadingColor = "#111827";
  private const string MutedColor = "#9ca3af";
  private const string LabelColor = "#6b7280";
  private const string BorderColor = "#f3f4f6";
  private const string FontStack =
    "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

  private const string FooterText =
    "Wiadomość wysłana automatycznie przez zapisz.me. Prosimy na nią nie odpowiadać.";

  // Ten sam anti-autolink co w nagłówku (patrz RenderBrandMark): bez własnej kotwicy Gmail
  // podświetla „zapisz.me” w stopce na niebiesko, wybijając ją ponad resztę szarego tekstu.
  private const string FooterHtml =
    """Wiadomość wysłana automatycznie przez <a href="https://zapisz.me" style="color:#6b7280;font-weight:600;text-decoration:none" target="_blank">zapisz.me</a>. Prosimy na nią nie odpowiadać.""";

  public static EmailBody Render(EmailDocument doc, EmailBrand brand) =>
    new(RenderHtml(doc, brand), RenderText(doc, brand));

  private static string RenderHtml(EmailDocument doc, EmailBrand brand)
  {
    var body = new StringBuilder();

    body.Append($"""<h1 style="margin:0 0 16px;font-size:20px;line-height:1.35;font-weight:600;color:{HeadingColor}">{E(doc.Heading)}</h1>""");

    foreach (var paragraph in doc.Paragraphs)
    {
      body.Append($"""<p style="margin:0 0 12px;font-size:15px;line-height:1.6;color:{TextColor}">{E(paragraph)}</p>""");
    }

    if (!string.IsNullOrWhiteSpace(doc.Highlight))
    {
      body.Append($"""
        <p style="margin:24px 0;text-align:center">
          <span style="display:inline-block;background:{BorderColor};border-radius:10px;padding:14px 24px;font-family:'SF Mono',Consolas,monospace;font-size:28px;font-weight:700;letter-spacing:0.28em;color:{HeadingColor}">{E(doc.Highlight)}</span>
        </p>
        """);
    }

    var rows = doc.Details.Where(d => !string.IsNullOrWhiteSpace(d.Value)).ToArray();
    if (rows.Length > 0)
    {
      body.Append($"""<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="width:100%;border-collapse:collapse;margin:20px 0">""");
      foreach (var row in rows)
      {
        body.Append($"""
          <tr>
            <td style="padding:10px 16px 10px 0;border-bottom:1px solid {BorderColor};font-size:14px;color:{LabelColor};white-space:nowrap;vertical-align:top">{E(row.Label)}</td>
            <td style="padding:10px 0;border-bottom:1px solid {BorderColor};font-size:14px;font-weight:600;color:{HeadingColor}">{E(row.Value)}</td>
          </tr>
          """);
      }

      body.Append("</table>");
    }

    if (!string.IsNullOrWhiteSpace(doc.Block))
    {
      body.Append($"""<div style="margin:16px 0;padding:14px;background:#f9fafb;border:1px solid {BorderColor};border-radius:8px;white-space:pre-wrap;font-size:14px;line-height:1.6;color:{TextColor}">{E(doc.Block)}</div>""");
    }

    if (doc.Cta is { } cta)
    {
      body.Append($"""
        <p style="margin:24px 0 10px">
          <a href="{E(cta.Url)}" style="display:inline-block;padding:12px 24px;border-radius:8px;background:{brand.AccentHex};color:{brand.AccentTextHex};font-size:15px;font-weight:600;text-decoration:none">{E(cta.Label)}</a>
        </p>
        <p style="margin:0;font-size:12px;line-height:1.5;color:{MutedColor};word-break:break-all">Jeśli przycisk nie działa, skopiuj ten link do przeglądarki:<br>{E(cta.Url)}</p>
        """);
    }

    if (!string.IsNullOrWhiteSpace(doc.Footnote))
    {
      body.Append($"""<p style="margin:20px 0 0;font-size:13px;line-height:1.5;color:{MutedColor}">{E(doc.Footnote)}</p>""");
    }

    // Preheader: tekst podglądu na liście maili. Ukryty w treści; sekwencja niełamliwych spacji
    // odpycha resztę treści, żeby klient nie doklejał do podglądu początku nagłówka.
    var preheader = string.IsNullOrWhiteSpace(doc.Preheader)
      ? string.Empty
      : $"""<div style="display:none;max-height:0;overflow:hidden;opacity:0">{E(doc.Preheader)}{string.Concat(Enumerable.Repeat("&#8199;&#65279;", 60))}</div>""";

    return $"""
      <!DOCTYPE html>
      <html lang="pl">
      <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{E(doc.Heading)}</title>
      </head>
      <body style="margin:0;padding:0;background:{PageBackground}">
        {preheader}
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="width:100%;border-collapse:collapse;background:{PageBackground}">
          <tr>
            <td align="center" style="padding:24px 12px">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="600" style="width:100%;max-width:600px;border-collapse:collapse;background:{CardBackground};border-radius:12px;overflow:hidden;font-family:{FontStack}">
                <tr>
                  <td style="padding:22px 28px;background:{brand.AccentHex}">
                    {RenderBrandMark(brand)}
                  </td>
                </tr>
                <tr>
                  <td style="padding:28px">{body}</td>
                </tr>
                <tr>
                  <td style="padding:18px 28px;background:#fafafa;border-top:1px solid #e5e7eb">
                    <p style="margin:0;font-size:12px;line-height:1.5;color:{MutedColor}">{FooterHtml}</p>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
        </table>
      </body>
      </html>
      """;
  }

  /// <summary>
  /// Nazwa marki na pasku nagłówka. Gdy marka ma adres (platforma), renderujemy WŁASNY
  /// <c>&lt;a&gt;</c> — inaczej Gmail sam autolinkuje tekst „zapisz.me” (wygląda jak domena)
  /// i nadaje mu domyślny styl linku: niebieski + podkreślenie. Na fioletowym pasku było to
  /// nieczytelne. Własna kotwica przejmuje kolor i wyłącza podkreślenie; klienty pocztowe
  /// nie linkują ponownie tekstu, który już jest w &lt;a&gt;.
  /// </summary>
  private static string RenderBrandMark(EmailBrand brand)
  {
    var style =
      $"font-size:24px;line-height:1.2;font-weight:700;letter-spacing:-0.01em;color:{brand.AccentTextHex}";

    return brand.SiteUrl is null
      ? $"""<span style="{style}">{E(brand.Name)}</span>"""
      : $"""<a href="{E(brand.SiteUrl)}" style="{style};text-decoration:none" target="_blank">{E(brand.Name)}</a>""";
  }

  private static string RenderText(EmailDocument doc, EmailBrand brand)
  {
    var text = new StringBuilder();
    text.AppendLine(brand.Name).AppendLine();
    text.AppendLine(doc.Heading).AppendLine();

    foreach (var paragraph in doc.Paragraphs)
    {
      text.AppendLine(paragraph).AppendLine();
    }

    if (!string.IsNullOrWhiteSpace(doc.Highlight))
    {
      text.AppendLine(doc.Highlight).AppendLine();
    }

    var rows = doc.Details.Where(d => !string.IsNullOrWhiteSpace(d.Value)).ToArray();
    if (rows.Length > 0)
    {
      foreach (var row in rows)
      {
        text.AppendLine($"{row.Label}: {row.Value}");
      }

      text.AppendLine();
    }

    if (!string.IsNullOrWhiteSpace(doc.Block))
    {
      text.AppendLine(doc.Block).AppendLine();
    }

    if (doc.Cta is { } cta)
    {
      text.AppendLine($"{cta.Label}: {cta.Url}").AppendLine();
    }

    if (!string.IsNullOrWhiteSpace(doc.Footnote))
    {
      text.AppendLine(doc.Footnote).AppendLine();
    }

    text.AppendLine("--").Append(FooterText);
    return text.ToString();
  }

  private static string E(string? value) => EmailHtml.Encode(value ?? string.Empty);
}
