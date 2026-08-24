namespace App.Api.Security;

/// <summary>
/// Każdy widget Turnstile (Managed / Invisible) ma własną parę site-key + secret-key.
/// Endpointy deklarują, którego typu widgetu oczekują — verifier wybiera odpowiedni secret.
/// </summary>
public enum TurnstileWidgetKind
{
  /// <summary>Widoczny widget (login / register / forgot-password).</summary>
  Managed = 0,

  /// <summary>Niewidoczny widget (publiczny booking-flow).</summary>
  Invisible = 1,
}

public interface ITurnstileVerifier
{
  Task<bool> VerifyAsync(
    string? token,
    string? remoteIp,
    TurnstileWidgetKind kind = TurnstileWidgetKind.Managed,
    CancellationToken cancellationToken = default);
}
