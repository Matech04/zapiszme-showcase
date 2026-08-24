namespace App.Domain.Exceptions;

/// <summary>
/// Operacja ASP.NET Identity (utworzenie konta, przypisanie roli) nie powiodła się. Niesie kody
/// błędów Identity pogrupowane po <c>Code</c> — globalny handler mapuje je na 400 z listą pod
/// <c>errors</c> (jak dawny <c>ValidationProblemDetails</c>), żeby frontend tłumaczył je tak samo
/// jak dotąd (<c>core/errors/api-error-messages.ts</c>).
/// </summary>
public sealed class IdentityOperationException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.IdentityOperationFailed;

  public string Title { get; }

  public IReadOnlyDictionary<string, string[]> Errors { get; }

  public IdentityOperationException(
    IReadOnlyDictionary<string, string[]> errors,
    string title = "Nie udało się utworzyć konta użytkownika.")
    : base(title)
  {
    Errors = errors;
    Title = title;
  }
}
