namespace App.Application.Guides;

/// <summary>
/// Reguły dla identyfikatorów przewodników przychodzących z dashboardu.
///
/// Backend celowo nie zna listy przewodników — rejestr żyje na froncie
/// (<c>core/guides/guides.registry.ts</c>), więc dodanie przewodnika nie wymaga deployu API.
/// Ceną za to jest przyjmowanie dowolnego stringa od klienta, dlatego obowiązuje wąski format
/// i twardy limit wierszy: bez nich zalogowany użytkownik mógłby w pętli zapełnić tabelę.
/// </summary>
public static class GuideIdRules
{
  public const int MaxLength = 64;

  /// <summary>Kebab-case — dokładnie ten kształt, który generuje rejestr front-endu.</summary>
  public const string Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

  /// <summary>
  /// Sufit liczby ukończonych przewodników na użytkownika. Rejestr ma ich kilkanaście,
  /// więc 100 to zapas na lata, a jednocześnie granica dla nadużycia.
  /// </summary>
  public const int MaxPerUser = 100;
}
