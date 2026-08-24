/**
 * Czysta logika doboru usług w combo (multi-select) — współdzielona przez `BookingFlow`,
 * `ServicePicker` i testy. Reguła „ten sam typ": w combo może być maksymalnie jedna usługa
 * z danej (niepustej) grupy wariantów (`Service.comboGroup`).
 */
import type { BookingServiceDto } from "../booking-openapi-client";

/** Znormalizowany klucz grupy wariantów (trim + lowercase). Pusty string = brak grupy. */
export function serviceGroupKey(service: { comboGroup?: string | null } | undefined): string {
  return (service?.comboGroup ?? "").trim().toLowerCase();
}

/**
 * Przełącza usługę w wyborze combo:
 *  - już wybrana → usuwa,
 *  - ma niepustą grupę reprezentowaną już przez inną usługę → PODMIENIA tamtą (radio w grupie),
 *  - inaczej dodaje na koniec (o ile nie przekroczono `max` — przy przekroczeniu zwraca bez zmian).
 * Zwraca NOWĄ tablicę; kolejność zachowana (pierwsza pozycja = usługa główna).
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

/** Suma czasu trwania wybranych usług (minuty). */
export function comboDurationMinutes(
  services: ReadonlyArray<{ durationInMinutes?: number }>,
): number {
  return services.reduce((sum, s) => sum + (s.durationInMinutes ?? 0), 0);
}

export interface ComboPriceRange {
  /** Dolna granica = suma cen bazowych. */
  min: number;
  /** Górna granica = suma (maxAmount ?? cena bazowa). */
  max: number;
  /** Czy łączna cena to widełki (któraś usługa ma zakres). */
  hasRange: boolean;
  /** Czy któraś wybrana usługa ukrywa cenę (`hidePrice`) — wtedy sumy nie pokazujemy. */
  priceHidden: boolean;
  currency: string;
}

/**
 * Łączna cena combo. Gdy któraś usługa ma widełki (`maxAmount` > cena), suma też staje się
 * widełkami (od–do). Waluta brana z pierwszej usługi (zakładamy spójną walutę salonu).
 *
 * Jeśli którakolwiek wybrana usługa ma `hidePrice`, suma traci sens cenowy →
 * `priceHidden = true` (UI ukrywa kwotę łączną).
 */
export function comboPriceRange(
  services: ReadonlyArray<{
    price?: { amount?: number; currency?: string };
    maxAmount?: number;
    hidePrice?: boolean;
  }>,
): ComboPriceRange {
  const currency = services[0]?.price?.currency ?? "PLN";
  const min = services.reduce((sum, s) => sum + (s.price?.amount ?? 0), 0);
  const max = services.reduce((sum, s) => sum + (s.maxAmount ?? s.price?.amount ?? 0), 0);
  const priceHidden = services.some((s) => s.hidePrice === true);
  return { min, max, hasRange: max > min, priceHidden, currency };
}

/** Wygodny `groupOf` zbudowany z listy usług (po `id`). */
export function makeGroupResolver(
  services: ReadonlyArray<BookingServiceDto>,
): (id: string) => string {
  const byId = new Map(services.map((s) => [s.id ?? "", s]));
  return (id: string) => serviceGroupKey(byId.get(id));
}

/** Zbiór id dodatków dopuszczonych przez aktualnie wybrane usługi główne. */
export function allowedAddonIds(
  selectedIds: readonly string[],
  services: ReadonlyArray<BookingServiceDto>,
): Set<string> {
  const byId = new Map(services.map((s) => [s.id ?? "", s]));
  const allowed = new Set<string>();
  for (const id of selectedIds) {
    const s = byId.get(id);
    if (s && s.isAddon !== true) {
      for (const a of s.addonServiceIds ?? []) allowed.add(a);
    }
  }
  return allowed;
}

/**
 * Usuwa z wyboru dodatki, których nie dopuszcza już żadna wybrana usługa główna
 * (np. po odznaczeniu usługi głównej). Usługi główne zostają nietknięte.
 */
export function removeOrphanedAddons(
  selectedIds: readonly string[],
  services: ReadonlyArray<BookingServiceDto>,
): string[] {
  const byId = new Map(services.map((s) => [s.id ?? "", s]));
  const allowed = allowedAddonIds(selectedIds, services);
  return selectedIds.filter((id) => {
    const s = byId.get(id);
    return s?.isAddon !== true || allowed.has(id);
  });
}
