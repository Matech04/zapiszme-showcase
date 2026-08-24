namespace App.Domain.Exceptions;

/// <summary>Odmowa wykonania operacji na zasobie innego pracownika (Employee vs Owner/Manager).</summary>
public sealed class ForbiddenAccessException : Exception, IErrorCodeException
{
  public ForbiddenAccessException(string message, string errorCode = ErrorCodes.Forbidden)
      : base(message)
  {
    ErrorCode = errorCode;
  }

  public string ErrorCode { get; }
}
