/**
 * Logika rozwiązania godzin pracy pracownika na konkretny dzień. Priorytet:
 *   1. zatwierdzony urlop / L4 (`EmployeeLeaveDto`) → brak pasa (dzień wolny),
 *   2. dzień specjalny (`ScheduleOverrideDto`) nadpisuje grafik tygodniowy,
 *   3. grafik tygodniowy z cyklami (`EmployeeScheduleDto`).
 *
 * Wszystkie funkcje są pure — pobierają dane jako argumenty, nie korzystają z DI.
 * Logikę dzielimy między widoki kalendarza, sheet wizyty i wizard reschedule.
 */
import {
  AbsenceStatus,
  AbsenceType,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  EmployeeScheduleDayDto,
  ScheduleOverrideDto,
  SlotGenerationMode,
  TimeRangeDto,
} from '@core/api/api-client';
import { coerceDate, formatYyyyMmDd, startOfDay } from './date-utils';

export type WorkingTimeRange = { startTime?: string; endTime?: string };

/**
 * Godziny pracy dla danej daty z uwzględnieniem urlopu, dnia specjalnego i grafiku tygodniowego.
 * Zwraca przedziały pracy z wyciętymi przerwami (gotowe do pozycjonowania pasów na osi).
 */
export function resolveWorkingRangesForDate(
  schedules: EmployeeScheduleDto[] | undefined,
  overrides: ScheduleOverrideDto[] | undefined,
  leaves: EmployeeLeaveDto[] | undefined,
  date: Date,
): WorkingTimeRange[] {
  if (findBlockingLeaveForDate(leaves, date)) return [];
  const override = pickOverrideForDate(overrides, date);
  if (override) return scheduleDayToWorkingRanges(override);
  const schedule = pickScheduleForDate(schedules, date);
  if (!schedule) return [];
  const day = pickScheduleDay(schedule, date);
  return scheduleDayToWorkingRanges(day);
}

/**
 * Stałe godziny startu („grafik statyczny", `SlotGenerationMode.FixedStartTimes`) dla daty —
 * lub `null`, gdy dzień NIE jest w trybie stałych slotów (dynamiczna siatka `Grid` / brak
 * obowiązującego grafiku / urlop / dzień wolny). Priorytet jak w {@link resolveWorkingRangesForDate}:
 *   1. urlop / L4 → `null` (dzień wolny),
 *   2. dzień specjalny (override) → jego tryb decyduje (nadpisuje grafik tygodniowy),
 *   3. grafik tygodniowy.
 *
 * Pusta lista stałych godzin (dzień wolny w cyklu / brak skonfigurowanych slotów) też daje
 * `null` — nie ma „statycznego dnia pracy", więc licznik nie ma czego pokazać.
 */
export function resolveFixedStartTimesForDate(
  schedules: EmployeeScheduleDto[] | undefined,
  overrides: ScheduleOverrideDto[] | undefined,
  leaves: EmployeeLeaveDto[] | undefined,
  date: Date,
): string[] | null {
  if (findBlockingLeaveForDate(leaves, date)) return null;
  const override = pickOverrideForDate(overrides, date);
  if (override) {
    if (override.slotGenerationMode !== SlotGenerationMode.FixedStartTimes) return null;
    const times = (override.fixedStartTimes ?? []).filter((t): t is string => !!t);
    return times.length ? times : null;
  }
  const schedule = pickScheduleForDate(schedules, date);
  if (!schedule || schedule.slotGenerationMode !== SlotGenerationMode.FixedStartTimes) {
    return null;
  }
  const day = pickScheduleDay(schedule, date);
  const times = (day?.fixedStartTimes ?? []).filter((t): t is string => !!t);
  return times.length ? times : null;
}

/** Minutowy przedział (od północy) — pas pracy lub zakres wizyty na osi dnia. */
export interface MinuteRange {
  startMin: number;
  endMin: number;
}

/** Wejście decyzji „wizyta poza godzinami pracy" — patrz {@link isAppointmentOutsideWorkingHours}. */
export interface OutsideWorkingHoursInput {
  /** Początek wizyty w minutach od północy. */
  startMin: number;
  /** Koniec wizyty w minutach od północy. */
  endMin: number;
  /** Pasy pracy pracownika w danym dniu (po wycięciu przerw). */
  ranges: readonly MinuteRange[];
  /** Czy dla tego dnia w ogóle mamy aktywny grafik/dzień specjalny/urlop (kontekst do oceny). */
  hasSchedule: boolean;
  /** Czy wybrany dzień jest w przeszłości (przed dzisiaj). */
  isPastDay: boolean;
}

/**
 * Czy wizytę należy oznaczyć jako „poza godzinami pracy" (czerwona kropka w kalendarzu).
 *
 * Reguły (kolejność istotna):
 *   1. Dzień przeszły → NIGDY nie flagujemy. Grafik bywa już nieaktywny (activeTo minęło), więc
 *      `ranges` byłyby puste i każda miniona wizyta fałszywie dostawałaby ostrzeżenie. Ostrzeżenie
 *      jest akcyjne tylko dla dziś/przyszłości (można jeszcze przełożyć wizytę).
 *   2. Brak kontekstu grafiku (`hasSchedule=false`) → nie flagujemy (nie wiemy, jakie są godziny).
 *   3. Mamy grafik, ale 0 pasów pracy (dzień wolny/urlop) → wizyta jest poza godzinami.
 *   4. W przeciwnym razie: poza godzinami, jeśli żaden pas nie obejmuje całej wizyty.
 */
export function isAppointmentOutsideWorkingHours(input: OutsideWorkingHoursInput): boolean {
  if (input.isPastDay) return false;
  if (!input.hasSchedule) return false;
  if (!input.ranges.length) return true;
  return !input.ranges.some((r) => r.startMin <= input.startMin && r.endMin >= input.endMin);
}

/** Przerwy dla danej daty — z dnia specjalnego, jeśli istnieje, inaczej z grafiku tygodniowego. */
export function resolveBreaksForDate(
  schedules: EmployeeScheduleDto[] | undefined,
  overrides: ScheduleOverrideDto[] | undefined,
  leaves: EmployeeLeaveDto[] | undefined,
  date: Date,
): WorkingTimeRange[] {
  if (findBlockingLeaveForDate(leaves, date)) return [];
  const override = pickOverrideForDate(overrides, date);
  if (override) {
    return (override.breaks ?? []).filter((b) => !!b?.startTime && !!b?.endTime);
  }
  const schedule = pickScheduleForDate(schedules, date);
  if (!schedule) return [];
  const day = pickScheduleDay(schedule, date);
  return (day?.breaks ?? []).filter((b) => !!b?.startTime && !!b?.endTime);
}

/**
 * Zatwierdzony urlop / L4 obejmujący datę. Tylko `AbsenceStatus.Approved` blokuje dzień —
 * wnioski oczekujące (`Pending`) nie zdejmują jeszcze pasa pracy, odrzucone (`Rejected`)
 * są ignorowane.
 *
 * O tym, CZY nieobecność zdejmuje pas pracy, rozstrzyga `blocksDay` z backendu, nie jej typ:
 * kolega z zespołu i Recepcja nie dostają `absenceType` (art. 9 RODO — patrz `EmployeeLeaveDto`),
 * więc pytanie „czy to urlop albo L4?" nie ma u nich odpowiedzi. Fallback na typ zostaje na czas
 * rolling deployu — starsze API nie zna jeszcze tego pola.
 */
export function findBlockingLeaveForDate(
  leaves: EmployeeLeaveDto[] | undefined,
  date: Date,
): EmployeeLeaveDto | undefined {
  if (!leaves?.length) return undefined;
  const target = startOfDay(date).getTime();
  return leaves.find((l) => {
    if (l.absenceStatus !== AbsenceStatus.Approved) return false;
    const blocks =
      l.blocksDay ??
      (l.absenceType === AbsenceType.Vacation || l.absenceType === AbsenceType.SickLeave);
    if (!blocks) return false;
    const start = coerceDate(l.startDate);
    const end = coerceDate(l.endDate);
    if (!start || !end) return false;
    return (
      target >= startOfDay(start).getTime() &&
      target <= startOfDay(end).getTime()
    );
  });
}

/** Dzień specjalny pasujący do daty (porównanie po kalendarzowym dniu). */
export function pickOverrideForDate(
  overrides: ScheduleOverrideDto[] | undefined,
  date: Date,
): ScheduleOverrideDto | undefined {
  if (!overrides?.length) return undefined;
  const key = formatYyyyMmDd(startOfDay(date));
  return overrides.find((o) => {
    const d = coerceDate(o.date);
    return d ? formatYyyyMmDd(startOfDay(d)) === key : false;
  });
}

/**
 * Zwraca grafik obowiązujący dla danej daty (po `activeFrom..activeTo`). Brak fallbacku do
 * `schedules[0]` — gdy żaden grafik nie pasuje (wygasł / jeszcze nie obowiązuje / luka),
 * zwraca `undefined`, dzięki czemu kalendarz nie rysuje fałszywego pasa z nieaktywnego grafiku.
 */
export function pickScheduleForDate(
  schedules: EmployeeScheduleDto[] | undefined,
  date: Date,
): EmployeeScheduleDto | undefined {
  if (!schedules?.length) return undefined;
  return schedules.find((s) => isWithinRange(date, s.activeFrom, s.activeTo));
}

/**
 * Wybiera dzień grafiku dla daty na podstawie cyklu N tygodni od `activeFrom`. Math:
 *   dayDiff = (date - activeFrom) w dniach
 *   cycleWeekIndex = floor(dayDiff/7) mod cycles
 *   cycleIndex = cycleWeekIndex*7 + date.getDay()  // edytor zapisuje tym samym wzorem
 */
export function pickScheduleDay(
  schedule: EmployeeScheduleDto,
  date: Date,
): EmployeeScheduleDayDto | undefined {
  const start = coerceDate(schedule.activeFrom);
  const cycles = schedule.numberOfCycles ?? 1;
  if (!start || cycles <= 0) return undefined;
  const dayDiff = Math.floor(
    (startOfDay(date).getTime() - startOfDay(start).getTime()) / 86400000,
  );
  const weekOffset = dayDiff >= 0 ? Math.floor(dayDiff / 7) : Math.ceil(dayDiff / 7);
  const cycleWeekIndex = ((weekOffset % cycles) + cycles) % cycles;
  const cycleIndex = cycleWeekIndex * 7 + date.getDay();
  return (schedule.days ?? []).find((d) => d.cycleIndex === cycleIndex);
}

/**
 * Konwertuje dzień grafiku / dzień specjalny do listy przedziałów pracy z wyciętymi przerwami.
 * Typ przyjmuje zarówno `EmployeeScheduleDayDto`, jak i `ScheduleOverrideDto` (ten sam kształt).
 */
export function scheduleDayToWorkingRanges(
  day: { workRanges?: TimeRangeDto[]; breaks?: TimeRangeDto[] } | undefined,
): WorkingTimeRange[] {
  const works = (day?.workRanges ?? [])
    .filter((r) => !!r?.startTime && !!r?.endTime)
    .slice()
    .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''));
  if (!works.length) return [];
  const sortedBreaks = [...(day?.breaks ?? [])]
    .filter((b) => !!b?.startTime && !!b?.endTime)
    .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''));
  const ranges: WorkingTimeRange[] = [];
  for (const work of works) {
    const breaksInWork = sortedBreaks.filter(
      (br) =>
        !!br.startTime && !!br.endTime &&
        (work.startTime ?? '') <= br.startTime &&
        br.endTime <= (work.endTime ?? ''),
    );
    let currentStart = work.startTime!;
    for (const br of breaksInWork) {
      if (currentStart < br.startTime!) {
        ranges.push({ startTime: currentStart, endTime: br.startTime });
      }
      currentStart = br.endTime!;
    }
    if (currentStart < work.endTime!) {
      ranges.push({ startTime: currentStart, endTime: work.endTime });
    }
  }
  return ranges;
}

function isWithinRange(
  date: Date,
  activeFrom: Date | string | undefined,
  activeTo: Date | string | undefined,
): boolean {
  const from = coerceDate(activeFrom);
  const to = coerceDate(activeTo);
  if (!from || !to) return false;
  const d = startOfDay(date).getTime();
  return d >= startOfDay(from).getTime() && d <= startOfDay(to).getTime();
}

// ──────────────────────────────────────────────────────────────────────────
// Szybkie przerwy w panelu (dodawanie/usuwanie przerwy jako dnia specjalnego).
// `ScheduleOverride` ZASTĘPUJE cały dzień, a domena wymaga, by każda przerwa mieściła
// się w pasie pracy (`BreakNotWithinWorkRange`). Dlatego dodanie przerwy = zbudowanie
// override z SUROWYMI pasami pracy dnia (bez wycinania) + listą przerw. Poniższe helpery
// rozwiązują surowy dzień i budują/porównują DTO override.
// ──────────────────────────────────────────────────────────────────────────

/** Surowy dzień grafiku (bez wycinania przerw) — wejście do budowy override przerwy. */
export interface RawScheduleDay {
  /** Tryb generowania slotów dnia — `Grid` (dynamiczny) lub `FixedStartTimes` (statyczny). */
  mode: SlotGenerationMode;
  /** Pasy pracy „jak zapisane" (NIE wycięte przerwami). */
  workRanges: TimeRangeDto[];
  /** Przerwy dnia. */
  breaks: TimeRangeDto[];
  /** Stałe godziny startu (tryb statyczny). */
  fixedStartTimes: string[];
}

function cleanRanges(ranges: TimeRangeDto[] | undefined): TimeRangeDto[] {
  return (ranges ?? []).filter((r): r is TimeRangeDto => !!r?.startTime && !!r?.endTime);
}

/**
 * Surowy dzień grafiku dla daty (priorytet: urlop → dzień specjalny → grafik tygodniowy).
 * `null` = brak czego edytować: urlop/L4, brak grafiku obowiązującego, albo dzień wolny w cyklu.
 * W odróżnieniu od {@link resolveWorkingRangesForDate} NIE wycina przerw — zwraca `workRanges`
 * tak, jak są zapisane, bo dokładnie tego potrzebuje override (przerwa musi leżeć w pasie pracy).
 */
export function resolveRawScheduleDayForDate(
  schedules: EmployeeScheduleDto[] | undefined,
  overrides: ScheduleOverrideDto[] | undefined,
  leaves: EmployeeLeaveDto[] | undefined,
  date: Date,
): RawScheduleDay | null {
  if (findBlockingLeaveForDate(leaves, date)) return null;
  const override = pickOverrideForDate(overrides, date);
  if (override) {
    return {
      mode: override.slotGenerationMode ?? SlotGenerationMode.Grid,
      workRanges: cleanRanges(override.workRanges),
      breaks: cleanRanges(override.breaks),
      fixedStartTimes: (override.fixedStartTimes ?? []).filter((t): t is string => !!t),
    };
  }
  const schedule = pickScheduleForDate(schedules, date);
  if (!schedule) return null;
  const day = pickScheduleDay(schedule, date);
  if (!day) return null;
  return {
    mode: schedule.slotGenerationMode ?? SlotGenerationMode.Grid,
    workRanges: cleanRanges(day.workRanges),
    breaks: cleanRanges(day.breaks),
    fixedStartTimes: (day.fixedStartTimes ?? []).filter((t): t is string => !!t),
  };
}

/**
 * Czy dla danego (surowego) dnia można dodać szybką przerwę: tylko grafik dynamiczny (`Grid`)
 * z niepustym pasem pracy. Tryb statyczny (`FixedStartTimes`), urlop i brak grafiku → `false`.
 */
export function canAddGridBreak(raw: RawScheduleDay | null): boolean {
  return !!raw && raw.mode === SlotGenerationMode.Grid && raw.workRanges.length > 0;
}

/** Normalizuje „HH:mm" / „H:m" / „HH:mm:ss" do „HH:mm:ss" (kontrakt TimeRange backendu). */
export function normalizeTimeHms(t: string): string {
  const p = t.trim().split(':').map((x) => x.trim());
  const hh = (p[0] ?? '0').padStart(2, '0');
  const mm = (p[1] ?? '0').padStart(2, '0');
  const ss = (p[2] ?? '00').padStart(2, '0');
  return `${hh}:${mm}:${ss}`;
}

/** Czy przerwa `brk` mieści się w którymś pasie pracy (porównanie leksykalne „HH:mm:ss"). */
export function isBreakWithinWorkRanges(brk: TimeRangeDto, workRanges: TimeRangeDto[]): boolean {
  const bs = brk.startTime ?? '';
  const be = brk.endTime ?? '';
  return workRanges.some((r) => (r.startTime ?? '') <= bs && be <= (r.endTime ?? ''));
}

/** Czy dwa zakresy czasowe nachodzą na siebie (półotwarcie: stykanie się końcami ≠ kolizja). */
export function timeRangesOverlap(
  a: { startTime?: string; endTime?: string },
  b: { startTime?: string; endTime?: string },
): boolean {
  return (a.startTime ?? '') < (b.endTime ?? '') && (b.startTime ?? '') < (a.endTime ?? '');
}

function sortRanges(ranges: TimeRangeDto[]): TimeRangeDto[] {
  return [...ranges].sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''));
}

/**
 * Buduje `ScheduleOverrideDto` (tryb `Grid`) dla danego dnia z surowymi pasami pracy i podaną
 * listą przerw. Używane i przy dodawaniu, i przy usuwaniu przerwy (lista przerw jest wyliczana
 * przez wołającego).
 */
export function buildGridOverrideDto(
  date: Date,
  raw: RawScheduleDay,
  breaks: TimeRangeDto[],
): ScheduleOverrideDto {
  return {
    date,
    slotGenerationMode: SlotGenerationMode.Grid,
    workRanges: raw.workRanges,
    breaks: sortRanges(breaks),
    fixedStartTimes: undefined,
  };
}

function sameRangeList(a: TimeRangeDto[], b: TimeRangeDto[]): boolean {
  if (a.length !== b.length) return false;
  const sa = sortRanges(a);
  const sb = sortRanges(b);
  return sa.every(
    (r, i) =>
      normalizeTimeHms(r.startTime ?? '') === normalizeTimeHms(sb[i].startTime ?? '') &&
      normalizeTimeHms(r.endTime ?? '') === normalizeTimeHms(sb[i].endTime ?? ''),
  );
}

/**
 * Czy dzień złożony z `workRanges` + `breaks` jest identyczny jak grafik TYGODNIOWY dla daty
 * (z pominięciem dni specjalnych). Jeśli tak — po usunięciu przerwy override jest zbędny i należy
 * go skasować (`removeScheduleOverride`), żeby dzień wrócił do bazowego grafiku.
 */
export function gridDayMatchesWeeklySchedule(
  schedules: EmployeeScheduleDto[] | undefined,
  leaves: EmployeeLeaveDto[] | undefined,
  date: Date,
  workRanges: TimeRangeDto[],
  breaks: TimeRangeDto[],
): boolean {
  const weekly = resolveRawScheduleDayForDate(schedules, undefined, leaves, date);
  if (!weekly || weekly.mode !== SlotGenerationMode.Grid) return false;
  return sameRangeList(weekly.workRanges, workRanges) && sameRangeList(weekly.breaks, breaks);
}
