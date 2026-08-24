using App.Application.Common.Email;

namespace App.Infrastructure.Email;

/// <summary>
/// Buduje treść maili autoryzacyjnych (potwierdzenie e-mail, reset hasła, zaproszenie
/// pracownika) i deleguje wysyłkę do <see cref="IEmailTransport"/>. Błędy transportu
/// propaguje do wołającego — call-site (AuthController) decyduje jak je obsłużyć.
///
/// Marka: <see cref="EmailBrand.Platform"/>, nie salonu — wszystkie te linki prowadzą do
/// centralnego dashboardu i dotyczą konta, nie wizyty w konkretnym salonie.
/// </summary>
public sealed class AuthEmailSender : IAuthEmailSender
{
  private const string IgnoreNotice = "Jeśli to nie Ty, zignoruj tę wiadomość.";

  private readonly IEmailTransport _transport;

  public AuthEmailSender(IEmailTransport transport) => _transport = transport;

  public Task SendConfirmEmailAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default) =>
    Send(
      toEmail,
      subject: "Potwierdź adres email",
      heading: "Potwierdź swój adres e-mail",
      intro: "Dziękujemy za założenie konta. Zostało już tylko potwierdzenie adresu.",
      ctaLabel: "Potwierdź adres",
      url: confirmUrl,
      cancellationToken);

  public Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default) =>
    Send(
      toEmail,
      subject: "Reset hasła",
      heading: "Ustaw nowe hasło",
      intro: "Otrzymaliśmy prośbę o zresetowanie hasła do Twojego konta.",
      ctaLabel: "Ustaw nowe hasło",
      url: resetUrl,
      cancellationToken);

  public Task SendEmployeeInviteAsync(string toEmail, string inviteUrl, CancellationToken cancellationToken = default) =>
    Send(
      toEmail,
      subject: "Zaproszenie do zespołu",
      heading: "Zaproszenie do zespołu",
      intro: "Zaproszono Cię do zespołu w zapisz.me. Aby dołączyć, ustaw hasło do swojego konta.",
      ctaLabel: "Przyjmij zaproszenie",
      url: inviteUrl,
      cancellationToken);

  public Task SendChangeEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default) =>
    Send(
      toEmail,
      subject: "Potwierdź nowy adres email",
      heading: "Potwierdź nowy adres e-mail",
      intro: "Aby dokończyć zmianę adresu e-mail w Twoim koncie, potwierdź nowy adres.",
      ctaLabel: "Potwierdź nowy adres",
      url: confirmUrl,
      cancellationToken);

  private Task Send(
    string toEmail,
    string subject,
    string heading,
    string intro,
    string ctaLabel,
    string url,
    CancellationToken ct)
  {
    var document = new EmailDocument
    {
      Heading = heading,
      Preheader = intro,
      Paragraphs = [intro],
      Cta = new EmailCallToAction(ctaLabel, url),
      Footnote = IgnoreNotice,
    };

    return _transport.SendAsync(toEmail, subject, EmailRenderer.Render(document, EmailBrand.Platform), ct);
  }
}
