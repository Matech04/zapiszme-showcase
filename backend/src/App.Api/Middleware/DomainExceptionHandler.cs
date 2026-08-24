using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Infrastructure;

public class GlobalExceptionHandler : IExceptionHandler
{
  private readonly ILogger<GlobalExceptionHandler> _logger;

  public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
  {
    _logger = logger;
  }

  // Ta metoda jest wywoływana za każdym razem, gdy w aplikacji wybuchnie JAKIKOLWIEK błąd
  public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext,
      Exception exception,
      CancellationToken cancellationToken)
  {
    // 1. Sprawdzamy, czy to NASZ błąd biznesowy.
    if (exception is not DomainException domainException)
    {
      // Zwracamy FALSE. To nie nasz błąd (np. padła baza, NullReference).
      // Framework przejdzie do domyślnej obsługi (zwróci 500 Internal Server Error).
      return false;
    }

    // 2. Skoro to błąd biznesowy, logujemy go jako Warning, a nie krytyczny Error
    _logger.LogWarning("Wyjątek domenowy: {Message}", domainException.Message);

    // 3. Budujemy standardową odpowiedź ProblemDetails
    var problemDetails = new ProblemDetails
    {
      Status = StatusCodes.Status400BadRequest,
      Title = "Błąd walidacji biznesowej",
      Detail = domainException.Message,
      Type = domainException.GetType().Name // Angular będzie wiedział dokładnie jaki to typ błędu
    };

    // 5. Wysyłamy odpowiedź do klienta (Angulara)
    httpContext.Response.StatusCode = problemDetails.Status.Value;
    await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

    // 6. Zwracamy TRUE. Mówimy serwerowi: "Zająłem się tym błędem, nie rób nic więcej".
    return true;
  }
}