namespace App.Application.Common.Interfaces;

/// <summary>
/// Anti-spam dla /api/auth/forgot-password: dla danego adresu email zezwala na wysyłkę
/// reset-maila tylko raz w określonym oknie czasowym (cooldown). Niezależne od IP —
/// chroni JEDNĄ ofiarę przed botnetem rozproszonym po wielu adresach źródłowych.
///
/// Anti-enum behaviour: endpoint zawsze zwraca 204, niezależnie czy throttle blokuje.
/// </summary>
public interface IPasswordResetEmailThrottle
{
  /// <summary>
  /// Próbuje zarezerwować slot wysyłki dla danego adresu. Zwraca true tylko raz w oknie cooldown.
  /// </summary>
  bool TryAcquire(string email);
}
