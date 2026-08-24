namespace App.Application.Admin.Impersonation;

/// <summary>Konfiguracja trybu support impersonation (sekcja "Impersonation" w appsettings; domyślne wartości bezpieczne).</summary>
public sealed class ImpersonationOptions
{
  public const string SectionName = "Impersonation";

  /// <summary>Czas życia sesji wsparcia. Po wygaśnięciu middleware przestaje akceptować sesję.</summary>
  public TimeSpan SessionTtl { get; set; } = TimeSpan.FromMinutes(30);

  /// <summary>Czy nowe sesje domyślnie startują w trybie tylko-do-odczytu (jawne włączenie zapisu wymagane).</summary>
  public bool DefaultReadOnly { get; set; } = true;
}
