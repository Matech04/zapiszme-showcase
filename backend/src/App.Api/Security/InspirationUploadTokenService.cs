using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace App.Api.Security;

/// <summary>
/// Token uploadu inspiracji oparty o DataProtection (podpis + szyfrowanie) — wzorzec jak
/// <see cref="ImpersonationTicketService"/>. Payload: <c>{appointmentId:N}:{expiryTicks}</c>.
/// TTL ~15 min: tyle, ile klientka potrzebuje na wysłanie kilku zdjęć zaraz po potwierdzeniu wizyty.
/// </summary>
public sealed class InspirationUploadTokenService : IInspirationUploadTokenService
{
  // Wersjonowana nazwa protektora — zmiana unieważnia wszystkie istniejące tokeny.
  private const string Purpose = "Booking.InspirationUpload.v1";
  private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

  private readonly IDataProtector _protector;
  private readonly TimeProvider _timeProvider;

  public InspirationUploadTokenService(IDataProtectionProvider provider, TimeProvider timeProvider)
  {
    _protector = provider.CreateProtector(Purpose);
    _timeProvider = timeProvider;
  }

  public string Issue(Guid appointmentId)
  {
    var expiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.Add(Ttl);
    var payload = $"{appointmentId:N}:{expiresAtUtc.Ticks}";
    return _protector.Protect(payload);
  }

  public bool TryValidate(string token, out Guid appointmentId)
  {
    appointmentId = Guid.Empty;
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

    var expiresAtUtc = new DateTime(expiryTicks, DateTimeKind.Utc);
    if (_timeProvider.GetUtcNow().UtcDateTime >= expiresAtUtc)
    {
      return false;
    }

    appointmentId = id;
    return true;
  }
}
