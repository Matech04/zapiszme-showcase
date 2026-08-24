using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Email;

/// <summary>Transport e-mail przez Azure Communication Services — używany w produkcji.</summary>
public sealed class AzureCommunicationEmailTransport : IEmailTransport
{
  private readonly IOptions<AzureCommunicationEmailOptions> _options;
  private readonly ILogger<AzureCommunicationEmailTransport> _logger;
  private readonly EmailClient? _client;

  public AzureCommunicationEmailTransport(
    IOptions<AzureCommunicationEmailOptions> options,
    ILogger<AzureCommunicationEmailTransport> logger)
  {
    _options = options;
    _logger = logger;
    if (!string.IsNullOrWhiteSpace(options.Value.ConnectionString))
    {
      // Jawny deadline na wywołanie HTTP do ACS — bez tego wolne/zawieszone ACS trzyma wątek
      // żądania (po rezerwacji) lub pętlę reminder-joba. Backstop: 15 s linked-CTS w NotificationDispatcher.
      var clientOptions = new EmailClientOptions();
      clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(10);
      _client = new EmailClient(options.Value.ConnectionString, clientOptions);
    }
  }

  public string? FeedbackRecipientEmail => _options.Value.FeedbackRecipientEmail;

  public async Task SendAsync(string toEmail, string subject, EmailBody body, CancellationToken ct = default)
  {
    if (_client is null || string.IsNullOrWhiteSpace(_options.Value.SenderAddress))
    {
      _logger.LogWarning(
        "E-mail '{Subject}' do {ToEmail} nie został wysłany — AzureCommunication:Email nie jest skonfigurowane.",
        subject,
        toEmail);
      return;
    }

    try
    {
      // Started, nie Completed — przyjęcie maila transakcyjnego przez ACS wystarcza; nie blokujemy
      // wątku na pollingu dostarczenia. Walidacja adresu/konfiguracji nadal rzuca synchronicznie.
      var message = new EmailMessage(
        _options.Value.SenderAddress!,
        new EmailRecipients([new EmailAddress(toEmail)]),
        new EmailContent(subject) { Html = body.Html, PlainText = body.Text });

      await _client.SendAsync(WaitUntil.Started, message, ct);
    }
    catch (RequestFailedException ex)
    {
      // Bez obiektu wyjątku: enricher maskuje właściwości zdarzenia, ale nie LogEvent.Exception,
      // a komunikat ACS potrafi zawierać adres odbiorcy (PII). Status + typ wystarczą do diagnozy.
      _logger.LogError(
        "Błąd wysyłki e-mail (ACS) '{Subject}' do {ToEmail}: {ExceptionType}, status={Status}",
        subject, toEmail, ex.GetType().Name, ex.Status);
      throw;
    }
    catch (FormatException ex)
    {
      _logger.LogError(
        ex,
        "Błąd wysyłki e-mail (ACS) '{Subject}' do {ToEmail} — nieprawidłowy adres lub konfiguracja AzureCommunication:Email.",
        subject,
        toEmail);
      throw;
    }
  }
}
