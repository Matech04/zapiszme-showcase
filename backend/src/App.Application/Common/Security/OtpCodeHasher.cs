using System.Security.Cryptography;
using System.Text;

namespace App.Application.Common.Security;

/// <summary>
/// SHA-256 hex (lowercase). Entropia 6-cyfrowego OTP jest niska, więc właściwym
/// zabezpieczeniem przed brute-force jest limit prób + cooldown — hash chroni tylko przed
/// odczytaniem kodu z bazy w razie wycieku snapshota.
/// </summary>
public static class OtpCodeHasher
{
  public static string Hash(string code)
  {
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(code));
    var sb = new StringBuilder(bytes.Length * 2);
    foreach (var b in bytes)
    {
      sb.Append(b.ToString("x2"));
    }

    return sb.ToString();
  }
}
