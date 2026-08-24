/**
 * Status dostępności dnia + budowa kafelków miesiąca.
 *
 * Progi są RELATYWNE do salonu: dla kosmetyki SOLO dzień ma naturalnie mało slotów
 * (długie zabiegi), więc sztywne progi (≥6 = „dużo") fałszywie straszyły „bardzo mało".
 * Zamiast tego punktem odniesienia jest mediana liczby wolnych slotów w danym miesiącu.
 */
import { WEEKDAYS_SHORT_PL, startOfDay, toISODate } from "./format";

export type DayAvailabilityStatus =
  | "free"
  | "limited"
  | "scarce"
  | "none"
  | "unknown";

export const STATUS_DOT: Record<"free" | "limited" | "scarce", string> = {
  free: "bg-emerald-500",
  limited: "bg-amber-400",
  scarce: "bg-red-500",
};

export const STATUS_LABEL: Record<DayAvailabilityStatus, string> = {
  free: "Dużo wolnych godzin",
  limited: "Mało wolnych godzin",
  scarce: "Bardzo mało wolnych godzin",
  none: "Brak wolnych godzin",
  unknown: "",
};

export interface AvailabilityScale {
  /** Liczba slotów od której dzień jest „dużo miejsc" (zielony). */
  free: number;
  /** Liczba slotów od której dzień jest „mało" (żółty); poniżej → „bardzo mało" (czerwony). */
  limited: number;
}

// Fallback gdy brak danych miesiąca (np. błąd) — odpowiada starym sztywnym progom.
const DEFAULT_SCALE: AvailabilityScale = { free: 6, limited: 3 };

/**
 * Wylicza progi kolorowania na podstawie rozkładu wolnych slotów w miesiącu.
 * `free` = mediana dni roboczych salonu (typowy dobry dzień), `limited` = połowa mediany.
 */
export function computeAvailabilityScale(
  avail: Map<string, number>,
): AvailabilityScale {
  const positives = [...avail.values()]
    .filter((c) => c > 0)
    .sort((a, b) => a - b);
  if (positives.length === 0) return DEFAULT_SCALE;
  const median = positives[Math.floor((positives.length - 1) / 2)];
  const free = Math.max(1, median);
  const limited = Math.max(1, Math.min(free, Math.ceil(median / 2)));
  return { free, limited };
}

export function availabilityStatus(
  count: number | undefined,
  scale: AvailabilityScale = DEFAULT_SCALE,
): DayAvailabilityStatus {
  if (count === undefined) return "unknown";
  if (count <= 0) return "none";
  if (count >= scale.free) return "free";
  if (count >= scale.limited) return "limited";
  return "scarce";
}

/**
 * Komunikat na poziomie miesiąca. Rozróżnia dwie sytuacje, które wcześniej dawały ten sam
 * tekst „brak terminów": salon/pracownik NIE MA jeszcze grafiku (klientka nic nie wskóra
 * przeglądając dalej) vs grafik jest, ale wszystko zaklepane (kolejny miesiąc ma sens).
 * Źródłem rozróżnienia jest `isWorkingDay` z endpointu miesięcznego.
 */
/** „2026-09-01" → „1 września". Zwraca null dla braku/niepoprawnej daty — wtedy nic nie obiecujemy. */
function formatOpensOn(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return null;
  return new Intl.DateTimeFormat("pl-PL", {
    day: "numeric",
    month: "long",
  }).format(parsed);
}

export function monthAvailabilityNotice(input: {
  loading: boolean;
  error: boolean;
  hasAnyFreeDay: boolean;
  hasWorkingDay: boolean;
  /** Czy da się jeszcze przejść do następnego miesiąca (limit okna rezerwacji). */
  canGoNext?: boolean;
  /** Salon jawnie zamknął ten miesiąc na zapisy (publikacja miesiąca). */
  closed?: boolean;
  /** ISO dnia, w którym miesiąc otworzy się sam. Brak = salon otworzy ręcznie. */
  opensOn?: string | null;
}): string | null {
  if (input.error)
    return "Nie udało się sprawdzić dostępności w tym miesiącu — odśwież stronę i spróbuj ponownie.";
  // W trakcie ładowania nie zgadujemy — dni są jeszcze „unknown".
  if (input.loading) return null;
  // Przed „brak terminów": zamknięty miesiąc wygląda identycznie (zero wolnych dni, zero dni
  // roboczych), ale znaczy coś zupełnie innego. Bez tej gałęzi klientka dostałaby „nie przygotowano
  // grafiku" i odeszła, zamiast wrócić w dniu otwarcia.
  if (input.closed) {
    const opensOn = formatOpensOn(input.opensOn);
    return opensOn
      ? `Zapisy na ten miesiąc ruszają ${opensOn} — zajrzyj tu wtedy.`
      : "Ten miesiąc nie jest jeszcze otwarty na rezerwacje.";
  }
  if (input.hasAnyFreeDay) return null;
  if (!input.hasWorkingDay)
    return "Dla tego miesiąca nie przygotowano jeszcze grafiku — terminy pojawią się, gdy salon go uzupełni.";
  // Na ostatnim miesiącu okna rezerwacji „sprawdź kolejny" jest radą nie do wykonania —
  // przycisk „następny miesiąc" jest wtedy wyłączony (MAX_MONTHS_AHEAD).
  return input.canGoNext === false
    ? "Wszystkie terminy są już zajęte — to najdalszy miesiąc otwarty na rezerwacje. Zajrzyj tu później, gdy zwolnią się miejsca."
    : "Wszystkie terminy w tym miesiącu są już zajęte — sprawdź kolejny.";
}

export type DayChip = {
  iso: string;
  dayNum: number;
  weekdayShort: string;
  isPast: boolean;
  isToday: boolean;
  status: DayAvailabilityStatus;
  /** Liczba wolnych slotów (undefined gdy brak danych). */
  count: number | undefined;
};

export function buildMonthDays(
  y: number,
  mo: number,
  avail: Map<string, number>,
  scale: AvailabilityScale = computeAvailabilityScale(avail),
  reference: Date = new Date(),
): DayChip[] {
  const today = startOfDay(reference);
  const todayIso = toISODate(today);
  const lastDay = new Date(y, mo, 0).getDate();
  const out: DayChip[] = [];
  for (let d = 1; d <= lastDay; d++) {
    const dt = startOfDay(new Date(y, mo - 1, d));
    const iso = toISODate(dt);
    const isPast = dt < today;
    const count = avail.get(iso);
    out.push({
      iso,
      dayNum: d,
      weekdayShort: WEEKDAYS_SHORT_PL[dt.getDay()] ?? "",
      isPast,
      isToday: iso === todayIso,
      status: isPast ? "none" : availabilityStatus(count, scale),
      count,
    });
  }
  return out;
}
