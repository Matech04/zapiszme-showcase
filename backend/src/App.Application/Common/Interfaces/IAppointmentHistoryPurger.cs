namespace App.Application.Common.Interfaces;

/// <summary>
/// Jednorazowe, na żądanie ownera, TRWAŁE usunięcie ISTNIEJĄCEJ historii wizyt bieżącego salonu
/// (Completed/Canceled z datą w przeszłości wg strefy salonu). Dopełnia tryb „nie przechowuj
/// historii" (<c>Tenant.DoNotRetainAppointmentHistory</c>): job w tle czyści na bieżąco, a to
/// kasuje to, co już zdążyło się nazbierać, ZANIM owner włączył tryb (scenariusz: salon zapisywał
/// historię przez jakiś czas, potem zdecydował się jej nie trzymać).
///
/// Reguła usuwania jest IDENTYCZNA jak w jobie (<c>AppointmentCleanupService.ShouldPurgeForNoHistoryRetention</c>):
/// tylko stany terminalne (Completed/Canceled) i tylko daty w przeszłości — wizyty bieżące/przyszłe
/// i aktywne holdy pozostają nietknięte. Operacja DESTRUKCYJNA i nieodwracalna.
/// </summary>
public interface IAppointmentHistoryPurger
{
  /// <summary>
  /// Trwale (hard-delete) usuwa terminalne, przeszłe wizyty danego tenanta. Zwraca liczbę usuniętych.
  /// </summary>
  Task<int> PurgePastHistoryAsync(Guid tenantId, CancellationToken ct = default);
}
