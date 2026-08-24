import type { AppointmentPreviewDto } from '@core/api/api-client';
import {
  AppointmentStatusVariant,
  statusVariantFromIdOrName,
} from '@core/theme/status-tokens';
import type { CalendarFiltersValue } from '../filters/calendar-filters.component';

/**
 * Wariant statusu wyciągnięty z DTO. Tolerancyjny na camelCase/PascalCase
 * (NSwag bywa niespójny — `Status` vs `status`).
 */
export function statusVariantFromPreview(p: AppointmentPreviewDto): AppointmentStatusVariant {
  const x = p as unknown as Record<string, unknown>;
  const st = (p.status ?? x['Status']) as
    | { id?: number; Id?: number; name?: string; Name?: string }
    | undefined;
  const id = st?.id ?? st?.Id;
  const name = String(st?.name ?? st?.Name ?? '').trim();
  return statusVariantFromIdOrName(id ?? null, name);
}

/**
 * Filtruje listę wizyt wg ustawień kalendarza. Spójna logika dla widoków dnia, tygodnia i miesiąca.
 *
 * Semantyka statusów:
 *   - `statuses` puste → pokaż wszystkie AKTYWNE warianty (włącznie z `pending`); `canceled` jest
 *     opt-in (użytkownik musi jawnie zaznaczyć chip „Anulowane"),
 *   - `statuses` niepuste → pokaż dokładnie te zaznaczone (canceled gdy zaznaczony).
 *
 * Filtry pracownika i frazy są addytywne (każdy musi przepuścić wizytę).
 */
export function filterAppointments(
  list: readonly AppointmentPreviewDto[] | undefined | null,
  filters: CalendarFiltersValue,
): AppointmentPreviewDto[] {
  if (!list?.length) return [];
  const statuses = new Set(filters.statuses);
  const empIds = new Set(filters.employeeIds);
  const q = filters.searchQuery.trim().toLowerCase();
  return list.filter((a) => {
    const variant = statusVariantFromPreview(a);
    if (!passesStatus(variant, statuses)) return false;
    if (empIds.size > 0 && a.employeeId && !empIds.has(a.employeeId)) return false;
    if (q && !matchesQuery(a, q)) return false;
    return true;
  });
}

function passesStatus(
  variant: AppointmentStatusVariant,
  statuses: ReadonlySet<AppointmentStatusVariant>,
): boolean {
  if (statuses.size > 0) return statuses.has(variant);
  // Default: wszystko aktywne (pending widoczny). Canceled opt-in jawnym toggle'em.
  return variant !== 'canceled';
}

function matchesQuery(a: AppointmentPreviewDto, q: string): boolean {
  const haystack = [
    a.customerFirstName,
    a.customerLastName,
    a.customerPhoneNumber,
    a.serviceName,
  ]
    .filter((s): s is string => !!s)
    .join(' ')
    .toLowerCase();
  return haystack.includes(q);
}
