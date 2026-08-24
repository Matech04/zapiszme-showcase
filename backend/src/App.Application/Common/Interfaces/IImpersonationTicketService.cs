namespace App.Application.Common.Interfaces;

/// <summary>
/// Podpisuje/weryfikuje zawartość cookie sesji wsparcia (DataProtection). Cookie to tylko
/// kryptograficznie podpisany wskaźnik na id sesji — źródłem prawdy (aktywność, wygaśnięcie,
/// odwołanie) pozostaje rekord <c>ImpersonationSession</c> w bazie, sprawdzany co żądanie.
/// </summary>
public interface IImpersonationTicketService
{
  /// <summary>Tworzy podpisany token dla cookie.</summary>
  string Protect(Guid sessionId, DateTime expiresAtUtc);

  /// <summary>Weryfikuje podpis i (wstępnie) wygaśnięcie. Zwraca false dla podrobionych/wygasłych tokenów.</summary>
  bool TryUnprotect(string token, out Guid sessionId);
}
