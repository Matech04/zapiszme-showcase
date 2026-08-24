namespace App.Infrastructure.Email;

/// <summary>
/// Treść maila opisana strukturalnie — *co* powiedzieć, nie *jak* to wygląda. Renderowana do HTML-a
/// i do wersji tekstowej przez <see cref="EmailRenderer"/>, dzięki czemu obie wersje nie mogą się
/// rozjechać.
///
/// Wszystkie wartości trzymamy SUROWE (nieescape'owane) — escapowanie należy do renderera.
/// </summary>
public sealed record EmailDocument
{
  /// <summary>Nagłówek wiadomości (h1). Zwykle to samo co temat, ale bez nazwy salonu.</summary>
  public required string Heading { get; init; }

  /// <summary>Tekst podglądu na liście maili. Gdy null — klient pocztowy pokaże początek treści.</summary>
  public string? Preheader { get; init; }

  public IReadOnlyList<string> Paragraphs { get; init; } = [];

  /// <summary>Wyróżniona wartość na środku (kod OTP). Renderowana dużym monospace.</summary>
  public string? Highlight { get; init; }

  /// <summary>Wiersze tabeli szczegółów. Wiersze z pustą wartością są pomijane.</summary>
  public IReadOnlyList<EmailDetail> Details { get; init; } = [];

  /// <summary>Blok tekstu zachowujący łamanie linii (opis zgłoszenia feedback).</summary>
  public string? Block { get; init; }

  public EmailCallToAction? Cta { get; init; }

  /// <summary>Drobny szary tekst pod treścią („Jeśli to nie Ty, zignoruj tę wiadomość").</summary>
  public string? Footnote { get; init; }
}

public readonly record struct EmailDetail(string Label, string? Value);

public sealed record EmailCallToAction(string Label, string Url);
