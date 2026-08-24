using App.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Middleware;

public class GlobalFallbackExceptionHandler : IExceptionHandler
{
  private readonly ILogger<GlobalFallbackExceptionHandler> _logger;

  public GlobalFallbackExceptionHandler(ILogger<GlobalFallbackExceptionHandler> logger)
  {
    _logger = logger;
  }

  public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext,
      Exception exception,
      CancellationToken cancellationToken)
  {
    // EF Core opakowuje wyjątki rzucone podczas ewaluacji parametrów zapytania (np. NoTenantHeader
    // z `Where(x => x.TenantId == TenantId)`) w InvalidOperationException → bez tego znany błąd
    // domenowy wpadałby w 500. Rozpakowujemy do faktycznego typu, który mapuje switch poniżej.
    if (exception is InvalidOperationException
        && exception.InnerException is { } inner
        && (inner is IErrorCodeException || inner is NoTenantHeader || inner is TenantViolation))
    {
      exception = inner;
    }

    // Jawne dopasowanie — wzorzec `exception switch { DomainException => ... }` potrafi nie
    // złapać typów z globalnej przestrzeni nazw w App.Domain; wtedy wszystko wpada w 500.
    int statusCode;
    string title;
    string detail;
    string errorCode;
    int? retryAfterSeconds = null;

    switch (exception)
    {
      case PhoneNotConfirmedException phoneNotConfirmed:
        statusCode = StatusCodes.Status401Unauthorized;
        title = "Brak potwierdzenia telefonu";
        detail = phoneNotConfirmed.Message;
        errorCode = phoneNotConfirmed.ErrorCode;
        break;

      case SmsServiceUnavailableException smsDown:
        statusCode = StatusCodes.Status503ServiceUnavailable;
        title = "Usługa SMS niedostępna";
        detail = smsDown.Message;
        errorCode = smsDown.ErrorCode;
        _logger.LogError(smsDown, "SMS service unavailable: {Message}", smsDown.Message);
        break;

      case PhoneOtpExpiredException expired:
        statusCode = StatusCodes.Status410Gone;
        title = "Kod SMS wygasł";
        detail = expired.Message;
        errorCode = expired.ErrorCode;
        break;

      case PhoneOtpLockedException locked:
        statusCode = StatusCodes.Status423Locked;
        title = "Zbyt wiele prób";
        detail = locked.Message;
        errorCode = locked.ErrorCode;
        break;

      case PhoneOtpInvalidException invalidOtp:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Nieprawidłowy kod SMS";
        detail = invalidOtp.Message;
        errorCode = invalidOtp.ErrorCode;
        break;

      case PhoneOtpCooldownException cooldown:
        statusCode = StatusCodes.Status429TooManyRequests;
        title = "Poczekaj przed kolejną wysyłką";
        detail = cooldown.Message;
        errorCode = cooldown.ErrorCode;
        retryAfterSeconds = cooldown.RetryAfterSeconds;
        break;

      case RegistrationConflictException conflict:
        // Świadomie generyczny komunikat — nie ujawniamy, że konflikt dotyczy email/telefon
        // (anti-enumeration). UI pokazuje to jako jeden globalny komunikat pod formularzem.
        statusCode = StatusCodes.Status409Conflict;
        title = "Nie udało się utworzyć konta";
        detail = conflict.Message;
        errorCode = conflict.ErrorCode;
        break;

      case IdentityOperationException identityOp:
        // Zwracamy KODY błędów Identity pod "errors" (jak dawny IdentityValidationProblem) —
        // tłumaczenie na polski robi frontend. Errors dołączamy do Extensions niżej.
        statusCode = StatusCodes.Status400BadRequest;
        title = identityOp.Title;
        detail = identityOp.Message;
        errorCode = identityOp.ErrorCode;
        break;

      case SalonSlugTakenException slugTaken:
        statusCode = StatusCodes.Status409Conflict;
        title = "Adres salonu zajęty";
        detail = slugTaken.Message;
        errorCode = slugTaken.ErrorCode;
        break;

      case OnboardingNotVerifiedException onboardingNotVerified:
        statusCode = StatusCodes.Status403Forbidden;
        title = "Wymagane potwierdzenie konta";
        detail = onboardingNotVerified.Message;
        errorCode = onboardingNotVerified.ErrorCode;
        break;

      case PhoneOtpEmailNotConfirmedException notConfirmed:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Wymagane potwierdzenie email";
        detail = notConfirmed.Message;
        errorCode = notConfirmed.ErrorCode;
        break;

      case PhoneAlreadyConfirmedException alreadyConfirmed:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Telefon już potwierdzony";
        detail = alreadyConfirmed.Message;
        errorCode = alreadyConfirmed.ErrorCode;
        break;

      case IdentityUserMissingEmployeeRecord identityMissing:
        statusCode = StatusCodes.Status403Forbidden;
        title = "Brak powiązania z pracownikiem";
        detail = identityMissing.Message;
        errorCode = identityMissing.ErrorCode;
        _logger.LogWarning("IdentityUserMissingEmployeeRecord");
        break;

      case RateLimitExceededException rateLimit:
        statusCode = StatusCodes.Status429TooManyRequests;
        title = "Zbyt wiele żądań";
        detail = rateLimit.Message;
        errorCode = rateLimit.ErrorCode;
        retryAfterSeconds = rateLimit.RetryAfterSeconds;
        _logger.LogWarning("RateLimitExceeded: {Message}", rateLimit.Message);
        break;

      case DomainException domainEx:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Błąd walidacji biznesowej";
        detail = domainEx.Message;
        errorCode = domainEx.ErrorCode;
        _logger.LogWarning("Wyjątek domenowy: {Message}", domainEx.Message);
        break;

      case ValidationException fv:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Błąd walidacji danych";
        detail = fv.Message;
        errorCode = ErrorCodes.ValidationFailed;
        _logger.LogWarning("FluentValidation: {Message}", fv.Message);
        break;

      // Middleware antiforgery (Program.cs) woła ValidateRequestAsync, które przy braku lub
      // niezgodności tokenu RZUCA. Bez tego przypadku wyjątek wpadał w `default` i wracał jako 500
      // „Krytyczny błąd serwera" — mylące w dwie strony: użytkownik dostawał komunikat o awarii
      // zamiast prośby o odświeżenie, a log alarmował o błędzie serwera przy zwykłym wygaśnięciu
      // tokenu. To błąd żądania (400), nie 401: sesja jest ważna, brakuje tylko świeżego tokenu,
      // więc NIE wolno tego mapować na 401 — front wylogowuje użytkownika przy każdym 401.
      case AntiforgeryValidationException antiforgery:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Nieaktualny token bezpieczeństwa";
        detail = "Odśwież stronę i spróbuj ponownie.";
        errorCode = ErrorCodes.AntiforgeryInvalid;
        _logger.LogWarning("Antiforgery: {Message}", antiforgery.Message);
        break;

      case ArgumentException argument:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Błąd walidacji danych";
        // Generyczny komunikat do klienta (jak przy DbUpdateException): ArgumentException bywa rzucany
        // przez framework/biblioteki i jego Message potrafi nieść nazwy parametrów lub wartości.
        // Pełną treść zostawiamy w logu.
        detail = "Nieprawidłowe dane wejściowe.";
        errorCode = ErrorCodes.InvalidArgument;
        _logger.LogWarning("ArgumentException: {Message}", argument.Message);
        break;

      case NotFoundException notFound:
        statusCode = StatusCodes.Status404NotFound;
        title = "Nie znaleziono";
        detail = notFound.Message;
        errorCode = notFound.ErrorCode;
        _logger.LogWarning("NotFound: {Message}", notFound.Message);
        break;

      case NoTenantHeader:
        statusCode = StatusCodes.Status400BadRequest;
        title = "Brak kontekstu";
        detail = exception.Message;
        errorCode = ErrorCodes.TenantMissing;
        _logger.LogWarning("NoTenantHeader");
        break;

      case TenantViolation:
        statusCode = StatusCodes.Status403Forbidden;
        title = "Odmowa dostępu";
        detail = "Niedozwolona operacja w kontekście tenanta.";
        errorCode = ErrorCodes.TenantViolation;
        _logger.LogWarning("TenantViolation");
        break;

      case ForbiddenAccessException forbiddenAccess:
        statusCode = StatusCodes.Status403Forbidden;
        title = "Brak uprawnień";
        detail = forbiddenAccess.Message;
        errorCode = forbiddenAccess.ErrorCode;
        _logger.LogWarning("ForbiddenAccess: {Message}", forbiddenAccess.Message);
        break;

      case UnauthorizedAccessException:
        statusCode = StatusCodes.Status401Unauthorized;
        title = "Brak autoryzacji";
        detail = exception.Message;
        errorCode = ErrorCodes.Unauthorized;
        break;

      case KeyNotFoundException:
        statusCode = StatusCodes.Status404NotFound;
        title = "Nie znaleziono";
        detail = exception.Message;
        errorCode = ErrorCodes.NotFound;
        break;

      case DbUpdateException dbEx:
        _logger.LogError(dbEx, "DbUpdateException przy zapisie");
        statusCode = StatusCodes.Status400BadRequest;
        title = "Błąd zapisu";
        detail =
            "Nie udało się zapisać zmian (np. ograniczenia bazy lub niespójność danych). Spróbuj ponownie.";
        errorCode = ErrorCodes.PersistenceFailed;
        break;

      default:
        statusCode = StatusCodes.Status500InternalServerError;
        title = "Wewnętrzny błąd serwera";
        detail = "Wystąpił nieoczekiwany błąd.";
        errorCode = ErrorCodes.InternalError;
        _logger.LogError(exception, "Krytyczny błąd serwera: {Message}", exception.Message);
        break;
    }

    var problemDetails = new ProblemDetails
    {
      Status = statusCode,
      Title = title,
      Detail = detail,
      Instance = httpContext.Request.Path
    };
    problemDetails.Extensions["errorCode"] = errorCode;
    problemDetails.Extensions["messageKey"] = errorCode;
    problemDetails.Extensions["correlationId"] = httpContext.TraceIdentifier;

    if (exception is PhoneNotConfirmedException phoneEx)
    {
      problemDetails.Extensions["userId"] = phoneEx.UserId;
    }
    if (exception is PhoneOtpInvalidException otpInvalid)
    {
      problemDetails.Extensions["remainingAttempts"] = otpInvalid.RemainingAttempts;
    }

    if (exception is ValidationException fvEx)
    {
      problemDetails.Extensions.Add(
          "errors",
          fvEx.Errors
              .GroupBy(x => x.PropertyName)
              .ToDictionary(
                  g => g.Key,
                  g => g.Select(x => x.ErrorMessage).ToArray()));
    }

    if (exception is IdentityOperationException identityOpEx)
    {
      problemDetails.Extensions["errors"] = identityOpEx.Errors;
    }

    httpContext.Response.StatusCode = statusCode;
    if (retryAfterSeconds is > 0)
    {
      httpContext.Response.Headers.Append("Retry-After", retryAfterSeconds.Value.ToString());
    }

    await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

    return true;
  }
}
