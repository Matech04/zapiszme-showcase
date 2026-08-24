namespace App.Application.Common.Interfaces;

/// <summary>
/// Unieważnianie cache'u slug→tenant, który <c>TenantIdentifierMiddleware</c> trzyma przez 5 minut
/// na najgorętszej, anonimowej ścieżce (<c>/api/booking/{slug}/...</c>).
///
/// Cache zakładał niezmienność slugu, ale właściciel może go zmienić w ustawieniach salonu, a
/// unikalny indeks natychmiast zwalnia stary slug do przejęcia. Bez unieważnienia powstaje okno
/// do 5 minut, w którym żądania na przejęty slug rozwiązują się na POPRZEDNIEGO tenanta —
/// rezerwacje klientów jednego salonu trafiają do kalendarza drugiego i obciążają jego budżet SMS.
/// Write-guard tego nie łapie: <c>TenantId</c> jest wtedy niepusty i spójny na całej ścieżce.
/// </summary>
public interface ITenantSlugCache
{
  /// <summary>Usuwa wpis dla slugu. Bezpieczne dla slugów, których nie ma w cache'u.</summary>
  void Invalidate(string slug);
}
