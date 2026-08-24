using App.Application.Booking;
using App.Application.Common.Email;

namespace App.Infrastructure.Email;

/// <summary>
/// Buduje treść e-maila z kodem OTP rezerwacji i deleguje wysyłkę do <see cref="IEmailTransport"/>.
/// Błędy transportu propaguje do wołającego — handler komendy traktuje je jako błąd operacji.
/// </summary>
public sealed class BookingOtpEmailSender : IBookingOtpEmailSender
{
  private readonly IEmailTransport _transport;

  public BookingOtpEmailSender(IEmailTransport transport) => _transport = transport;

  public Task SendOtpCodeAsync(
    string toEmail,
    string sixDigitCode,
    EmailBrand brand,
    CancellationToken cancellationToken = default)
  {
    var document = new EmailDocument
    {
      Heading = "Twój kod weryfikacyjny",
      Preheader = $"Kod do rezerwacji w {brand.Name}",
      Paragraphs = [$"Wpisz poniższy kod, aby potwierdzić rezerwację w {brand.Name}:"],
      Highlight = sixDigitCode,
      Footnote = "Kod ważny jest 10 minut. Jeśli to nie Ty — zignoruj tę wiadomość.",
    };

    return _transport.SendAsync(
      toEmail,
      "Kod weryfikacyjny rezerwacji",
      EmailRenderer.Render(document, brand),
      cancellationToken);
  }
}
