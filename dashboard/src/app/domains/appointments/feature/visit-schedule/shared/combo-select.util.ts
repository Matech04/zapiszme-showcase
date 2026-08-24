/**
 * Czysta logika doboru usług w combo (panel) — współdzielona przez create-appointment-drawer
 * i reschedule-appointment-dialog oraz testy. Reguła „ten sam typ": max jedna usługa z danej
 * (niepustej) grupy wariantów (Service.comboGroup) w jednej wizycie.
 */

/** Znormalizowany klucz grupy wariantów (trim + lowercase). Pusty = brak grupy. */
export function comboGroupKey(comboGroup: string | null | undefined): string {
  return (comboGroup ?? '').trim().toLowerCase();
}

/**
 * Przełącza usługę w wyborze combo:
 *  - już wybrana → usuwa,
 *  - ma niepustą grupę reprezentowaną już przez inną usługę → PODMIENIA tamtą,
 *  - inaczej dodaje (o ile nie przekroczono `max` — przy przekroczeniu zwraca kopię bez zmian).
 * Zwraca NOWĄ tablicę (kolejność zachowana; pierwsza = usługa główna).
 */
export function toggleServiceSelection(
  selected: readonly string[],
  id: string,
  groupOf: (id: string) => string,
  max: number,
): string[] {
  if (selected.includes(id)) {
    return selected.filter((x) => x !== id);
  }
  const group = groupOf(id);
  const base = group ? selected.filter((x) => groupOf(x) !== group) : selected;
  if (base.length >= max) {
    return [...selected];
  }
  return [...base, id];
}
