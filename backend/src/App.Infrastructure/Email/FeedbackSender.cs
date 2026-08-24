using App.Application.Common.Email;
using App.Application.Feedback;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Email;

/// <summary>
/// Buduje treść zgłoszeń feedback (błędy / propozycje) i deleguje wysyłkę do
/// <see cref="IEmailTransport"/>. Odbiorca pochodzi z <see cref="IEmailTransport.FeedbackRecipientEmail"/>.
/// Best-effort — błąd wysyłki jest logowany i połykany.
/// </summary>
public sealed class FeedbackSender : IFeedbackSender
{
  private readonly IEmailTransport _transport;
  private readonly ILogger<FeedbackSender> _logger;

  public FeedbackSender(IEmailTransport transport, ILogger<FeedbackSender> logger)
  {
    _transport = transport;
    _logger = logger;
  }

  public async Task SendAsync(
    string kind,
    string title,
    string description,
    string? pageUrl,
    string? userEmail,
    string salonName,
    CancellationToken ct = default)
  {
    var recipient = _transport.FeedbackRecipientEmail;
    if (string.IsNullOrWhiteSpace(recipient))
    {
      _logger.LogWarning(
        "Zgłoszenie feedback pominięte — brak skonfigurowanego adresu odbiorcy. Salon: {Salon}",
        salonName);
      return;
    }

    var kindLabel = kind == "bug" ? "Błąd" : "Propozycja";
    var subject = $"[Feedback] {kindLabel}: {title} | Salon: {salonName}";

    var document = new EmailDocument
    {
      Heading = $"[{kindLabel}] {title}",
      Preheader = $"{kindLabel} zgłoszony przez {salonName}",
      Details =
      [
        new EmailDetail("Salon", salonName),
        new EmailDetail("Użytkownik", userEmail),
        new EmailDetail("Strona", pageUrl),
        new EmailDetail("Data", $"{DateTimeOffset.UtcNow:dd.MM.yyyy HH:mm} UTC"),
      ],
      Block = description,
    };

    try
    {
      await _transport.SendAsync(recipient, subject, EmailRenderer.Render(document, EmailBrand.Platform), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // {ToEmail} (nie {To}) — nazwa property zawiera "email", więc enricher zamaskuje adres w logach.
      _logger.LogError(ex, "Błąd wysyłki zgłoszenia feedback do {ToEmail}.", recipient);
    }
  }
}
