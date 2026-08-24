/**
 * Helpery do mapowania `AppointmentDayAvailabilityDto[]` na "poziom" dnia
 * (none / low / medium / high) używany przez kalendarz w drawerach wizyt.
 *
 * Progi są celowo absolutne (a nie procentowe) — backend liczy realną liczbę slotów
 * po gap-fillingu, więc "4 wolne sloty" oznacza realnie cztery różne godziny startu.
 * Dla typowej usługi (30-60min) to ~2h wyboru = wygodny zapas.
 */

import { AppointmentDayAvailabilityDto } from '@core/api/api-client';
import { coerceDate, formatYyyyMmDd } from './date-utils';

export type AvailabilityLevel = 'none' | 'low' | 'medium' | 'high';

/** Próg "wolnych slotów" od którego dzień traktujemy jako zielony (dużo miejsc). */
const HIGH_THRESHOLD = 6;
/** Próg minimalny dla żółtego (1..MEDIUM_THRESHOLD−1 = pomarańczowy/low). */
const MEDIUM_THRESHOLD = 3;

export function classifyAvailability(count: number): AvailabilityLevel {
  if (count <= 0) return 'none';
  if (count < MEDIUM_THRESHOLD) return 'low';
  if (count < HIGH_THRESHOLD) return 'medium';
  return 'high';
}

/**
 * Buduje `Map<'yyyy-MM-dd', AvailabilityLevel>` z payloadu API.
 * Akceptuje `Date | string` w polu `date` (NSwag bywa niespójny — w runtime to string).
 */
export function buildAvailabilityMap(
  days: readonly AppointmentDayAvailabilityDto[] | null | undefined,
): Map<string, AvailabilityLevel> {
  const map = new Map<string, AvailabilityLevel>();
  if (!days) return map;
  for (const day of days) {
    if (day == null) continue;
    const d = coerceDate(day.date);
    if (!d) {
      if (typeof day.date === 'string') {
        const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(day.date);
        if (m) {
          map.set(`${m[1]}-${m[2]}-${m[3]}`, classifyAvailability(day.availableCount ?? 0));
        }
      }
      continue;
    }
    map.set(formatYyyyMmDd(d), classifyAvailability(day.availableCount ?? 0));
  }
  return map;
}

/** Klucz lookup dla komórki kalendarza (PrimeNG month = 0..11). */
export function dateMetaKey(meta: { year: number; month: number; day: number }): string {
  return `${meta.year}-${String(meta.month + 1).padStart(2, '0')}-${String(meta.day).padStart(2, '0')}`;
}
