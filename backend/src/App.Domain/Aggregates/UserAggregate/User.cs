using App.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace App.Domain.Aggregates.UserAggregate;

public class User : IdentityUser<Guid>, IAggregateRoot
{
  public string DisplayName { get; private set; } = string.Empty;

  /// <summary>UTC timestamp utworzenia konta — wykorzystywane przez cleanup
  /// niepotwierdzonych rejestracji do liczenia grace window.</summary>
  public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

  /// <summary>
  /// Kod promocyjny podany przy slim-rejestracji, przechowany do czasu utworzenia tenanta w kreatorze
  /// (<c>CompleteProfileCommand</c>). Redempcja wymaga istniejącego <c>Tenant</c>, który powstaje dopiero
  /// w kroku „Jak się nazywasz?" — do tego momentu kod czeka tutaj. Znormalizowany (trim + upper);
  /// pusty/whitespace → <c>null</c>. Czyszczony po udanej redempcji.
  /// </summary>
  public string? PendingPromoCode { get; private set; }

  public User()
  {
  }

  public User(string email, string displayName)
  {
    Id = Guid.NewGuid();
    CreatedAt = DateTime.UtcNow;
    UpdateProfile(email, displayName);
  }

  public void UpdateProfile(string email, string displayName)
  {
    Guard.AgainstEmptyString(email, nameof(email));
    Guard.AgainstEmptyString(displayName, nameof(displayName));

    Email = email;
    UserName = email;
    DisplayName = displayName;
  }

  /// <summary>Zapisuje kod promocyjny do odroczonej redempcji (trim + upper; pusty/whitespace → <c>null</c>).</summary>
  public void SetPendingPromoCode(string? code) =>
      PendingPromoCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

  /// <summary>Czyści oczekujący kod promocyjny (po redempcji przy tworzeniu tenanta lub po jego odrzuceniu).</summary>
  public void ClearPendingPromoCode() => PendingPromoCode = null;
}