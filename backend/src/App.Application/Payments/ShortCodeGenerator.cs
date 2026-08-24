using System.Security.Cryptography;

namespace App.Application.Payments;

/// <summary>Generuje losowy, krótki kod base62 dla linków skróconych (zapisz.me/p/{code}).</summary>
public static class ShortCodeGenerator
{
  private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

  public static string Generate(int length = 7)
  {
    var chars = new char[length];
    Span<byte> bytes = stackalloc byte[length];
    RandomNumberGenerator.Fill(bytes);
    for (int i = 0; i < length; i++)
    {
      chars[i] = Alphabet[bytes[i] % Alphabet.Length];
    }

    return new string(chars);
  }
}
