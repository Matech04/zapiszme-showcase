namespace App.Application.Common.Security;

/// <summary>
/// Dev-only: wymusza STAŁY kod OTP zamiast losowego, żeby dało się przeklikać rejestrację
/// i kreator onboardingu bez wyłuskiwania kodu z logów przy każdym przebiegu.
///
/// Świadomie działa wyłącznie na etapie GENEROWANIA kodu (<c>SendPhoneOtpCommandHandler</c>):
/// kod jest normalnie hashowany i zapisywany, więc ścieżka weryfikacji
/// (<c>ConfirmPhoneCommand</c> — TTL, licznik prób, lockout, porównanie hashy) zostaje
/// BAJT W BAJT produkcyjna. Nie ma tu żadnego bypassu do obejścia ani do przetestowania.
///
/// O tym, czy opcja jest aktywna, decyduje <c>Program.cs</c> (tam mieszka idiom środowiskowy) —
/// gate to <c>IsDevelopment()</c>, a NIE <c>!IsProduction()</c>: przy LOCAL_PROD
/// <c>IsProduction()</c> jest <c>true</c>, więc ten drugi wariant byłby tam martwy i kusiłby,
/// żeby dopisać wyjątek dla LOCAL_PROD — a stamtąd już tylko jedna literówka
/// (<c>LOCAL_PROD=true</c> na prawdziwym prodzie) do wyłączenia OTP klientom.
/// Dodatkowo <c>ValidateProductionConfiguration</c> twardo ubija start prod, jeśli flaga jest
/// ustawiona — bez wyjątku dla LOCAL_PROD.
/// </summary>
/// <param name="FixedCode">
/// Sześciocyfrowy kod (np. „000000”) albo <c>null</c> = zachowanie produkcyjne (kod losowy).
/// </param>
public sealed record PhoneOtpDevOptions(string? FixedCode)
{
  /// <summary>Zachowanie produkcyjne — kod losowy. Rejestrowane wszędzie poza Development.</summary>
  public static readonly PhoneOtpDevOptions Disabled = new((string?)null);
}
