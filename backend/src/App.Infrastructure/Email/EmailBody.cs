namespace App.Infrastructure.Email;

/// <summary>
/// Dwie reprezentacje tej samej wiadomości. Wysyłamy obie (multipart/alternative) — filtry
/// antyspamowe punktują maile HTML-only, a część czytników w ogóle nie renderuje HTML-a.
/// </summary>
public sealed record EmailBody(string Html, string Text);
