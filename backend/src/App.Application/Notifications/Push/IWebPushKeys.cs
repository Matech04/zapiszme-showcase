namespace App.Application.Notifications.Push;

/// <summary>
/// Klucze VAPID Web Push (z konfiguracji). Publiczny trafia do przeglądarki przy subskrypcji,
/// prywatny podpisuje żądania do push-service. Abstrakcja trzyma warstwę Application wolną od
/// szczegółów konfiguracji Infrastructure.
/// </summary>
public interface IWebPushKeys
{
  string PublicKey { get; }
  string PrivateKey { get; }

  /// <summary>Kontakt operatora (mailto:/URL) wymagany przez protokół VAPID.</summary>
  string Subject { get; }

  /// <summary>Czy komplet kluczy jest skonfigurowany (inaczej kanał push jest no-op).</summary>
  bool IsConfigured { get; }
}
