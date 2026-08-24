namespace App.Domain.Exceptions;

/// <summary>
/// Zbyt częste żądania (np. OTP) — mapowane na HTTP 429 z opcjonalnym Retry-After.
/// </summary>
public sealed class RateLimitExceededException : Exception, IErrorCodeException
{
  public int? RetryAfterSeconds { get; }
  public string ErrorCode { get; }

  public RateLimitExceededException(
      string message,
      int? retryAfterSeconds = null,
      string errorCode = ErrorCodes.RateLimitExceeded)
      : base(message)
  {
    RetryAfterSeconds = retryAfterSeconds;
    ErrorCode = errorCode;
  }
}
