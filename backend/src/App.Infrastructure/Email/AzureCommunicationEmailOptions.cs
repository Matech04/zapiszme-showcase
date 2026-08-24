namespace App.Infrastructure.Email;

public sealed class AzureCommunicationEmailOptions
{
  public const string SectionName = "AzureCommunication:Email";

  /// <summary>Parametry połączenia zasobu Azure Communication Services (Email).</summary>
  public string? ConnectionString { get; set; }

  /// <summary>
  /// Adres nadawcy (np. DoNotReply@&lt;subdomena&gt;.azurecomm.net albo ze zweryfikowanej domeny).
  /// </summary>
  public string? SenderAddress { get; set; }

  /// <summary>Adres odbiorcy zgłoszeń (błędy / propozycje) z panelu admina.</summary>
  public string? FeedbackRecipientEmail { get; set; }
}
