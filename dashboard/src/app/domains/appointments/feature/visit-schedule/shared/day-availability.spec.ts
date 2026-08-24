import { describe, expect, it } from 'vitest';
import {
  AbsenceStatus,
  AbsenceType,
  AppointmentPreviewDto,
  AppointmentStatus,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  ScheduleOverrideDto,
  SlotGenerationMode,
} from '@core/api/api-client';
import {
  computeFixedSlots,
  dayAvailabilityAccentClass,
  leaveBadgeClass,
  leaveLabel,
  resolveDayAvailability,
} from './day-availability';

/** Tygodniowy grafik Pn–Pt 09:00–17:00 (cycleIndex = dayOfWeek Sun=0..Sat=6). */
function weeklySchedule(): EmployeeScheduleDto {
  const days = [1, 2, 3, 4, 5].map((cycleIndex) => ({
    cycleIndex,
    workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
    breaks: [],
  }));
  return {
    activeFrom: new Date(2026, 0, 4),
    activeTo: new Date(2030, 0, 1),
    numberOfCycles: 1,
    days,
  };
}

/** Tygodniowy grafik ze stałymi godzinami startu (tryb FixedStartTimes) na poniedziałek. */
function fixedWeeklySchedule(times: string[]): EmployeeScheduleDto {
  return {
    activeFrom: new Date(2026, 0, 4),
    activeTo: new Date(2030, 0, 1),
    numberOfCycles: 1,
    slotGenerationMode: SlotGenerationMode.FixedStartTimes,
    days: [{ cycleIndex: 1, fixedStartTimes: times, workRanges: [], breaks: [] }],
  };
}

function leave(type: AbsenceType | null, status: AbsenceStatus): EmployeeLeaveDto {
  return {
    id: 'l1',
    startDate: new Date(2026, 4, 11),
    endDate: new Date(2026, 4, 11),
    absenceType: type ?? undefined,
    absenceStatus: status,
  };
}

function overrideDayOff(): ScheduleOverrideDto {
  return { date: new Date(2026, 4, 11), workRanges: [], breaks: [] };
}

function overrideHours(): ScheduleOverrideDto {
  return {
    date: new Date(2026, 4, 11),
    workRanges: [{ startTime: '10:00:00', endTime: '14:00:00' }],
    breaks: [],
  };
}

function appt(start: string, end: string, statusName: string): AppointmentPreviewDto {
  return {
    startTime: start,
    endTime: end,
    status: { name: statusName } as AppointmentStatus,
  } as AppointmentPreviewDto;
}

const monday = new Date(2026, 4, 11); // 2026-05-11
const sunday = new Date(2026, 4, 10); // poza grafikiem Pn-Pt

describe('resolveDayAvailability — priorytet statusu', () => {
  it('urlop bije wszystko (status=leave, sloty ukryte)', () => {
    const av = resolveDayAvailability(
      [weeklySchedule()],
      [overrideHours()],
      [leave(AbsenceType.Vacation, AbsenceStatus.Approved)],
      monday,
    );
    expect(av.status).toBe('leave');
    expect(av.leave).toEqual({ type: AbsenceType.Vacation, status: AbsenceStatus.Approved });
    expect(av.fixedTimes).toBeNull();
  });

  it('urlop oczekujący też pokazywany (status=leave)', () => {
    const av = resolveDayAvailability([weeklySchedule()], [], [leave(AbsenceType.Vacation, AbsenceStatus.Pending)], monday);
    expect(av.status).toBe('leave');
    expect(av.leave?.status).toBe(AbsenceStatus.Pending);
  });

  it('dzień specjalny (override) bije grafik (status=special)', () => {
    const av = resolveDayAvailability([weeklySchedule()], [overrideHours()], [], monday);
    expect(av.status).toBe('special');
    expect(av.override).toEqual({ isDayOff: false, summary: '10:00–14:00' });
  });

  it('override bez godzin → dzień wolny (special, isDayOff)', () => {
    const av = resolveDayAvailability([weeklySchedule()], [overrideDayOff()], [], monday);
    expect(av.status).toBe('special');
    expect(av.override).toEqual({ isDayOff: true, summary: 'Wolne' });
  });

  it('sam grafik tygodniowy → status=work z podsumowaniem', () => {
    const av = resolveDayAvailability([weeklySchedule()], [], [], monday);
    expect(av.status).toBe('work');
    expect(av.scheduleSummary).toBe('09:00–17:00');
  });

  it('poza cyklem (niedziela) → status=off', () => {
    const av = resolveDayAvailability([weeklySchedule()], [], [], sunday);
    expect(av.status).toBe('off');
    expect(av.scheduleSummary).toBeNull();
  });

  it('brak danych → status=off', () => {
    const av = resolveDayAvailability(undefined, undefined, undefined, monday);
    expect(av.status).toBe('off');
  });

  it('grafik stały (FixedStartTimes) → fixedTimes wypełnione', () => {
    const av = resolveDayAvailability([fixedWeeklySchedule(['09:00', '12:00'])], [], [], monday);
    expect(av.status).toBe('work');
    expect(av.fixedTimes).toEqual(['09:00', '12:00']);
  });

  // Kalendarz kolegi: backend nie oddaje powodu nieobecności (art. 9 RODO). Dzień ma nadal być
  // oznaczony jako wolny, ale bez zgadywania typu — wcześniej `?? AbsenceType.Vacation` kazałoby
  // narysować cudze L4 jako „Urlop".
  it('zamaskowany powód → status=leave, type=null (bez podstawiania Urlopu)', () => {
    const av = resolveDayAvailability([weeklySchedule()], [], [leave(null, AbsenceStatus.Approved)], monday);
    expect(av.status).toBe('leave');
    expect(av.leave).toEqual({ type: null, status: AbsenceStatus.Approved });
  });
});

describe('prezentacja zamaskowanej nieobecności', () => {
  it('leaveLabel(null) → „Nieobecność", nie „Urlop"', () => {
    expect(leaveLabel(null)).toBe('Nieobecność');
    expect(leaveLabel(AbsenceType.Vacation)).toBe('Urlop');
    expect(leaveLabel(AbsenceType.SickLeave)).toBe('Chorobowe');
  });

  it('leaveBadgeClass(null) nie używa palety urlopu ani chorobowego', () => {
    const cls = leaveBadgeClass(null, AbsenceStatus.Approved);
    expect(cls).not.toContain('amber');
    expect(cls).not.toContain('rose');
  });

  it('zamaskowana nieobecność nadal daje akcent dnia nieroboczego', () => {
    const av = resolveDayAvailability([weeklySchedule()], [], [leave(null, AbsenceStatus.Approved)], monday);
    expect(dayAvailabilityAccentClass(av)).toBe('bg-amber-500');
  });
});

describe('computeFixedSlots', () => {
  it('null gdy brak stałych godzin', () => {
    expect(computeFixedSlots(null, [])).toBeNull();
  });

  it('oznacza slot zajęty gdy realna wizyta obejmuje jego start', () => {
    const slots = computeFixedSlots(['09:00', '12:00'], [appt('09:00:00', '10:00:00', 'Booked')]);
    expect(slots).toEqual([
      { time: '09:00', taken: true },
      { time: '12:00', taken: false },
    ]);
  });

  it('anulowana wizyta nie zajmuje slotu', () => {
    const slots = computeFixedSlots(['09:00'], [appt('09:00:00', '10:00:00', 'Canceled')]);
    expect(slots).toEqual([{ time: '09:00', taken: false }]);
  });
});
