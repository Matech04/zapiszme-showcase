namespace App.Infrastructure.Notifications.Push;

/// <summary>
/// Konfiguracja Web Push (sekcja "WebPush"). Klucze VAPID i kontakt operatora.
/// Sekrety z env: WEBPUSH__VAPIDPUBLICKEY / WEBPUSH__VAPIDPRIVATEKEY / WEBPUSH__SUBJECT.
/// </summary>
public sealed class WebPushOptions
{
  public const string SectionName = "WebPush";

  public string VapidPublicKey { get; set; } = string.Empty;
  public string VapidPrivateKey { get; set; } = string.Empty;

  /// <summary>Kontakt operatora wymagany przez VAPID, np. "mailto:kontakt@zapisz.me".</summary>
  public string Subject { get; set; } = string.Empty;
}
