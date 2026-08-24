using System.Security.Cryptography;
using System.Text;

namespace App.Application.Common;

/// <summary>
/// Deterministyczny SHA-256 (hex) z adresu IP klienta. Obecnie nieużywany — był planowany
/// jako per-IP fallback dla anti-slot-hoarding, ale ryzyko false-positive za NAT/CGNAT
/// (dwóch userów za jednym IP anulowałoby sobie nawzajem holdy) okazało się zbyt wysokie.
/// Zostawiony jako gotowy klocek, gdyby pojawiła się potrzeba hashowania IP w innych
/// kontekstach (audit, fingerprint, throttle).
/// </summary>
public static class AnonIpHasher
{
  public static string? HashOrNull(string? ip)
  {
    if (string.IsNullOrWhiteSpace(ip))
    {
      return null;
    }

    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip.Trim()));
    return Convert.ToHexString(bytes).ToLowerInvariant();
  }
}
