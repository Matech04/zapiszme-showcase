/**
 * Pure data helpers dla kalendarza wizyt — bez stanu, używane przez
 * `schedule-resolution.ts` oraz komponenty widoków.
 */

export function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

export function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

export function sameCalendarDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  );
}

export function formatYyyyMmDd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/**
 * Parsuje wartość przychodzącą z API jako `Date | string` (NSwag bywa niespójny).
 * Zwraca `null` dla `null`/`undefined`/niepoprawnych dat — bez rzucania.
 */
export function coerceDate(value: Date | string | undefined | null): Date | null {
  if (value == null) return null;
  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? null : value;
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/**
 * Klucz żądania wizyt dla `rxResource`. Pracownik MUSI w nim być — `stream` czyta
 * `effectiveEmployeeId`, ale zasób powtarza strumień tylko przy zmianie `params`, więc bez id
 * wybór innego pracownika nie przeładowuje widoku (regresja w widoku tygodnia).
 */
export function appointmentsRequestKey(
  rangeStart: string,
  desktopColumns: boolean,
  employeeId: string | null | undefined,
): string {
  return [rangeStart, desktopColumns ? 'desktop-all' : 'single', employeeId ?? '-'].join('\x1e');
}
