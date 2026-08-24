using App.Domain.Exceptions;

public abstract class DomainException : Exception, IErrorCodeException
{
  protected DomainException(string message, string errorCode = ErrorCodes.InvalidArgument) : base(message)
  {
    ErrorCode = errorCode;
  }

  public string ErrorCode { get; }
}