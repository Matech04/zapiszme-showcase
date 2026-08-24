namespace App.Domain.Exceptions;

/// <summary>
/// Próba dokończenia profilu (utworzenia salonu w kreatorze onboardingu) zanim właściciel potwierdził
/// e-mail ORAZ numer telefonu. API broni się samo — front nie jest granicą bezpieczeństwa
/// (mina #4 z planu onboardingu). Mapowane na 403.
/// </summary>
public sealed class OnboardingNotVerifiedException : Exception, IErrorCodeException
{
  public string ErrorCode => ErrorCodes.OnboardingNotVerified;

  public OnboardingNotVerifiedException()
    : base("Najpierw potwierdź adres e-mail i numer telefonu, aby założyć salon.")
  {
  }
}
