/**
 * Warstwa dostępności pojedynczego dnia dla kalendarza (miesiąc) — status pracy/urlopu/dnia
 * specjalnego złożony z grafiku tygodniowego, dni specjalnych (override) i urlopów. Logika
 * wyodrębniona z `employee-full-schedule` przy scaleniu podglądu miesiąca z kalendarzem wizyt,
 * tak by komórka miesiąca i wspólny drawer dnia korzystały z jednego źródła prawdy.
 *
 * Wszystkie funkcje są pure — dane wchodzą argumentami, prymitywy z {@link ./schedule-resolution}.
 * Priorytet statusu (jak w reszcie kalendarza): urlop > dzień specjalny (override) > grafik > wolne.
 */
import {
  AbsenceStatus,
  AbsenceType,
  AppointmentPreviewDto,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  EmployeeScheduleDayDto,
  ScheduleOverrideDto,
  TimeRangeDto,
} from '@core/api/api-client';
import { statusVariantFromIdOrName } from '@core/theme/status-tokens';
import { coerceDate, formatYyyyMmDd, startOfDay } from './date-utils';
import { pickScheduleDay, pickScheduleForDate } from './schedule-resolution';

export type DayAvailabilityStatus = 'leave' | 'special' | 'work' | 'off';

export interface DayLeaveInfo {
  /**
   * `null` = powód nieobecności zamaskowany przez API. Kolega z zespołu i terminal „Recepcja"
   * dostają sam zakres dat — `AbsenceType.SickLeave` to dana o zdrowiu (art. 9 RODO).
   * NIE podstawiać tu `Vacation` jako fallbacku: pokazałoby „Urlop" tam, gdzie jest L4.
   */
  type: AbsenceType | null;
  /**
   * Status NIE jest maskowany — „zatwierdzone / oczekujące / odrzucone" to stan obiegu wniosku,
   * nie informacja o zdrowiu, a kalendarz musi odróżnić urlop zatwierdzony od oczekującego.
   */
  status: AbsenceStatus;
}

export interface DayOverrideInfo {
  isDayOff: boolean;
  summary: string;
}

export interface FixedSlot {
  time: string;
  taken: boolean;
}

/**
 * Rozwiązana dostępność dnia. `status` to dominujący stan (do akcentu/koloru), a pola
 * szczegółowe (`leave`/`override`/`scheduleSummary`/`fixedTimes`) pozwalają komórce i drawerowi
 * pokazać dokładniejszy opis. Zgodnie z zachowaniem podglądu miesiąca: obecność urlopu (dowolny
 * status) ukrywa sloty stałe.
 */
export interface DayAvailability {
  status: DayAvailabilityStatus;
  leave: DayLeaveInfo | null;
  override: DayOverrideInfo | null;
  scheduleSummary: string | null;
  fixedTimes: string[] | null;
}

/** „HH:mm:ss"/„HH:mm" → „HH:mm". */
function trimTime(t: string | undefined | null): string {
  return t ? t.substring(0, 5) : '';
}

function summarizeFixedTimes(times: string[]): string {
  return times
    .map((t) => trimTime(t))
    .filter((t) => !!t)
    .sort((a, b) => a.localeCompare(b))
    .join(', ');
}

function summarizeWorkAndBreaks(works: TimeRangeDto[], breaks: TimeRangeDto[]): string {
  const sorted = [...works].sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''));
  const base = sorted.map((r) => `${trimTime(r.startTime)}–${trimTime(r.endTime)}`).join(', ');
  if (!breaks.length) return base;
  const breaksLabel = breaks.length === 1 ? '1 przerwa' : `${breaks.length} przerwy`;
  return `${base} (${breaksLabel})`;
}

/**
 * Urlop / nieobecność obejmująca datę — DOWOLNY status i typ (do wyświetlenia, także oczekujące
 * i dni specjalne). Odróżnij od `findBlockingLeaveForDate` (schedule-resolution), które liczy tylko
 * zatwierdzone urlopy/L4 zdejmujące pas pracy.
 */
function findDisplayLeave(leaves: EmployeeLeaveDto[], date: Date): DayLeaveInfo | null {
  const target = startOfDay(date).getTime();
  for (const l of leaves) {
    const s = coerceDate(l.startDate);
    const e = coerceDate(l.endDate);
    if (!s || !e) continue;
    if (target >= startOfDay(s).getTime() && target <= startOfDay(e).getTime()) {
      return {
        type: l.absenceType ?? null,
        status: l.absenceStatus ?? AbsenceStatus.Approved,
      };
    }
  }
  return null;
}

function findOverrideInfo(overrides: ScheduleOverrideDto[], date: Date): DayOverrideInfo | null {
  const iso = formatYyyyMmDd(startOfDay(date));
  for (const o of overrides) {
    const d = coerceDate(o.date);
    if (!d || formatYyyyMmDd(startOfDay(d)) !== iso) continue;
    const fixed = (o.fixedStartTimes ?? []).filter((t): t is string => !!t);
    if (fixed.length) return { isDayOff: false, summary: summarizeFixedTimes(fixed) };
    const works = (o.workRanges ?? []).filter((r) => !!r?.startTime && !!r?.endTime);
    if (!works.length) return { isDayOff: true, summary: 'Wolne' };
    return { isDayOff: false, summary: summarizeWorkAndBreaks(works, o.breaks ?? []) };
  }
  return null;
}

function baseDayEntry(
  schedules: EmployeeScheduleDto[],
  date: Date,
): EmployeeScheduleDayDto | undefined {
  const schedule = pickScheduleForDate(schedules, date);
  if (!schedule) return undefined;
  return pickScheduleDay(schedule, date);
}

function resolveScheduleSummary(schedules: EmployeeScheduleDto[], date: Date): string | null {
  const day = baseDayEntry(schedules, date);
  if (!day) return null;
  const fixed = (day.fixedStartTimes ?? []).filter((t): t is string => !!t);
  if (fixed.length) return summarizeFixedTimes(fixed);
  const works = (day.workRanges ?? []).filter((r) => !!r?.startTime && !!r?.endTime);
  if (!works.length) return null;
  return summarizeWorkAndBreaks(works, day.breaks ?? []);
}

/** Stałe godziny startu (HH:mm) dla dnia: override > grafik bazowy; `null` gdy tryb nie-stały. */
function resolveFixedTimes(
  schedules: EmployeeScheduleDto[],
  overrides: ScheduleOverrideDto[],
  date: Date,
): string[] | null {
  const iso = formatYyyyMmDd(startOfDay(date));
  const o = overrides.find((x) => {
    const d = coerceDate(x.date);
    return d ? formatYyyyMmDd(startOfDay(d)) === iso : false;
  });
  if (o) {
    const fixed = (o.fixedStartTimes ?? [])
      .filter((t): t is string => !!t)
      .map((t) => trimTime(t));
    return fixed.length ? fixed.sort((a, b) => a.localeCompare(b)) : null;
  }
  const day = baseDayEntry(schedules, date);
  const fixed = (day?.fixedStartTimes ?? [])
    .filter((t): t is string => !!t)
    .map((t) => trimTime(t));
  return fixed.length ? fixed.sort((a, b) => a.localeCompare(b)) : null;
}

/**
 * Dostępność dnia z grafiku tygodniowego, dni specjalnych i urlopów. Obecność urlopu (dowolny
 * status) ukrywa sloty stałe (parytet z podglądem miesiąca).
 */
export function resolveDayAvailability(
  schedules: EmployeeScheduleDto[] | undefined,
  overrides: ScheduleOverrideDto[] | undefined,
  leaves: EmployeeLeaveDto[] | undefined,
  date: Date,
): DayAvailability {
  const sch = schedules ?? [];
  const ovr = overrides ?? [];
  const lv = leaves ?? [];

  const leave = findDisplayLeave(lv, date);
  const override = findOverrideInfo(ovr, date);
  const scheduleSummary = resolveScheduleSummary(sch, date);
  const fixedTimes = leave ? null : resolveFixedTimes(sch, ovr, date);

  const status: DayAvailabilityStatus = leave
    ? 'leave'
    : override
      ? 'special'
      : fixedTimes?.length || scheduleSummary
        ? 'work'
        : 'off';

  return { status, leave, override, scheduleSummary, fixedTimes };
}

function timeToMinutes(hhmm: string): number | null {
  const m = /^(\d{1,2}):(\d{2})$/.exec(trimTime(hhmm));
  if (!m) return null;
  return Number(m[1]) * 60 + Number(m[2]);
}

/**
 * Slot (HH:mm) jest zajęty, gdy któraś REALNA wizyta obejmuje jego godzinę startu. „Realna"
 * blokuje termin (Pending/Booked/InProgress/Completed); pomijamy `canceled` (znów wolny) i
 * `awaitingOtp` (anonimowy hold przed OTP — wygasa sam).
 */
function isSlotTaken(dayAppointments: AppointmentPreviewDto[], slotHm: string): boolean {
  const t = timeToMinutes(slotHm);
  if (t === null) return false;
  return dayAppointments.some((a) => {
    const st = a.status as { id?: number; name?: string } | undefined;
    const variant = statusVariantFromIdOrName(st?.id ?? null, st?.name ?? null);
    if (variant === 'canceled' || variant === 'awaitingOtp') return false;
    const s = timeToMinutes(trimTime(a.startTime));
    const e = timeToMinutes(trimTime(a.endTime));
    if (s === null || e === null) return false;
    return s <= t && t < e;
  });
}

/** Sloty stałe dnia z oznaczeniem wolny/zajęty na podstawie wizyt tego dnia. */
export function computeFixedSlots(
  fixedTimes: string[] | null,
  dayAppointments: AppointmentPreviewDto[],
): FixedSlot[] | null {
  if (!fixedTimes) return null;
  return fixedTimes.map((time) => ({ time, taken: isSlotTaken(dayAppointments, time) }));
}

// ── Prezentacja (etykiety i klasy) — wspólne dla komórki miesiąca i drawera dnia ──────────────

export function leaveLabel(type: AbsenceType | null): string {
  switch (type) {
    case AbsenceType.Vacation:
      return 'Urlop';
    case AbsenceType.SickLeave:
      return 'Chorobowe';
    case AbsenceType.SpecialDay:
      return 'Dzień specjalny';
    default:
      return 'Nieobecność';
  }
}

export function leaveStatusLabel(status: AbsenceStatus | null): string {
  switch (status) {
    case AbsenceStatus.Approved:
      return 'Zatwierdzone';
    case AbsenceStatus.Pending:
      return 'Oczekujące';
    case AbsenceStatus.Rejected:
      return 'Odrzucone';
    default:
      return '';
  }
}

export function leaveBadgeClass(type: AbsenceType | null, status: AbsenceStatus): string {
  const base = (() => {
    switch (type) {
      case AbsenceType.Vacation:
        return 'bg-amber-500/15 text-amber-900 dark:text-amber-200 border-amber-500/40';
      case AbsenceType.SickLeave:
        return 'bg-rose-500/15 text-rose-900 dark:text-rose-200 border-rose-500/40';
      case AbsenceType.SpecialDay:
        return 'bg-violet-500/15 text-violet-900 dark:text-violet-200 border-violet-500/40';
      default:
        return 'bg-surface-100 dark:bg-surface-100 border-surface-300 dark:border-surface-200';
    }
  })();
  if (status === AbsenceStatus.Pending) return base + ' opacity-70';
  if (status === AbsenceStatus.Rejected) return base + ' line-through opacity-60';
  return base;
}

export function overrideBadgeClass(isDayOff: boolean): string {
  return isDayOff
    ? 'bg-surface-200/60 dark:bg-surface-200/60 text-surface-700 border-surface-300 dark:border-surface-600'
    : 'bg-sky-500/15 text-sky-900 dark:text-sky-100 border-sky-500/40';
}

/** Kolor lewego akcentu wg statusu dnia (parytet z agendą podglądu miesiąca). */
export function dayAvailabilityAccentClass(av: DayAvailability): string {
  if (av.leave) {
    switch (av.leave.type) {
      case AbsenceType.SickLeave:
        return 'bg-rose-500';
      case AbsenceType.SpecialDay:
        return 'bg-violet-500';
      default:
        return 'bg-amber-500';
    }
  }
  if (av.override) return av.override.isDayOff ? 'bg-surface-300 dark:bg-surface-600' : 'bg-sky-500';
  if (av.status === 'work') return 'bg-emerald-500';
  return 'bg-surface-300 dark:bg-surface-600';
}
