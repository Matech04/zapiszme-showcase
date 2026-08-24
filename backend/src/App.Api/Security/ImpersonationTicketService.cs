using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace App.Api.Security;

public sealed class ImpersonationTicketService : IImpersonationTicketService
{
  // Wersjonowana nazwa protektora — zmiana unieważnia wszystkie istniejące tickety.
  private const string Purpose = "Impersonation.Ticket.v1";

  private readonly IDataProtector _protector;
  private readonly TimeProvider _timeProvider;

  public ImpersonationTicketService(IDataProtectionProvider provider, TimeProvider timeProvider)
  {
    _protector = provider.CreateProtector(Purpose);
    _timeProvider = timeProvider;
  }

  public string Protect(Guid sessionId, DateTime expiresAtUtc)
  {
    var payload = $"{sessionId:N}:{expiresAtUtc.ToUniversalTime().Ticks}";
    return _protector.Protect(payload);
  }

  public bool TryUnprotect(string token, out Guid sessionId)
  {
    sessionId = Guid.Empty;
    if (string.IsNullOrWhiteSpace(token))
    {
      return false;
    }

    string payload;
    try
    {
      payload = _protector.Unprotect(token);
    }
    catch
    {
      // Nieprawidłowy podpis / uszkodzony token.
      return false;
    }

    var parts = payload.Split(':', 2);
    if (parts.Length != 2
        || !Guid.TryParseExact(parts[0], "N", out var id)
        || !long.TryParse(parts[1], out var expiryTicks))
    {
      return false;
    }

    // Wstępne odrzucenie wygasłych (twarda weryfikacja aktywności i tak następuje w bazie).
    var expiresAtUtc = new DateTime(expiryTicks, DateTimeKind.Utc);
    if (_timeProvider.GetUtcNow().UtcDateTime >= expiresAtUtc)
    {
      return false;
    }

    sessionId = id;
    return true;
  }
}
