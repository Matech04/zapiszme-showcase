namespace App.Application.Common.Interfaces;

/// <summary>
/// Anti-spam dla /api/auth/resend-confirm-email: per-email cooldown, niezależny od IP.
/// Chroni JEDNĄ ofiarę (właściciela adresu) przed botnetem rozproszonym po wielu adresach
/// źródłowych — per-IP rate limiter (<c>AuthSensitive</c>) tego nie pokrywa.
///
/// Anti-enum behaviour: endpoint zawsze zwraca 204, niezależnie czy throttle blokuje.
/// </summary>
public interface IConfirmEmailResendThrottle
{
  /// <summary>
  /// Próbuje zarezerwować slot wysyłki dla danego adresu. Zwraca true tylko raz w oknie cooldown.
  /// </summary>
  bool TryAcquire(string email);
}
