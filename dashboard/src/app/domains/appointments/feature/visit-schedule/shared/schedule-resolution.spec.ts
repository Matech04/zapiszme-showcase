import { describe, expect, it } from 'vitest';
import {
  AbsenceStatus,
  AbsenceType,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  ScheduleOverrideDto,
  SlotGenerationMode,
} from '@core/api/api-client';
import {
  buildGridOverrideDto,
  canAddGridBreak,
  findBlockingLeaveForDate,
  gridDayMatchesWeeklySchedule,
  isAppointmentOutsideWorkingHours,
  isBreakWithinWorkRanges,
  normalizeTimeHms,
  pickOverrideForDate,
  resolveBreaksForDate,
  resolveFixedStartTimesForDate,
  resolveRawScheduleDayForDate,
  resolveWorkingRangesForDate,
  timeRangesOverlap,
} from './schedule-resolution';

/** Buduje tygodniowy grafik Pn–Pt 09:00–17:00 z opcjonalną przerwą 12:00–12:30 we środę. */
function weeklySchedule(opts: { withWedBreak?: boolean } = {}): EmployeeScheduleDto {
  // Dla cycleIndex używamy `dayOfWeek` Sun=0..Sat=6. Pn=1..Pt=5.
  const days = [1, 2, 3, 4, 5].map((cycleIndex) => ({
    cycleIndex,
    workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
    breaks:
      opts.withWedBreak && cycleIndex === 3
        ? [{ startTime: '12:00:00', endTime: '12:30:00' }]
        : [],
  }));
  return {
    activeFrom: new Date(2026, 0, 4), // niedziela 2026-01-04 (Sun=0)
    activeTo: new Date(2030, 0, 1),
    numberOfCycles: 1,
    days,
  };
}

function leave(opts: {
  start: Date;
  end: Date;
  type: AbsenceType | null;
  status: AbsenceStatus;
  blocksDay?: boolean;
}): EmployeeLeaveDto {
  return {
    id: 'leave-1',
    startDate: opts.start,
    endDate: opts.end,
    absenceType: opts.type ?? undefined,
    absenceStatus: opts.status,
    blocksDay: opts.blocksDay,
  };
}

const monday = new Date(2026, 4, 11); // 2026-05-11
const wednesday = new Date(2026, 4, 13); // 2026-05-13
const sunday = new Date(2026, 4, 10); // 2026-05-10 (poza grafikiem Pn-Pt)

describe('resolveWorkingRangesForDate', () => {
  it('zwraca [] gdy brak grafiku, dnia specjalnego i urlopu', () => {
    expect(resolveWorkingRangesForDate(undefined, undefined, undefined, monday)).toEqual([]);
  });

  it('grafik tygodniowy: poniedziałek → 09:00–17:00', () => {
    const ranges = resolveWorkingRangesForDate([weeklySchedule()], undefined, undefined, monday);
    expect(ranges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
  });

  it('grafik tygodniowy: weekend (niedziela) → [] (brak dnia w cyklu)', () => {
    const ranges = resolveWorkingRangesForDate([weeklySchedule()], undefined, undefined, sunday);
    expect(ranges).toEqual([]);
  });

  it('przerwa w grafiku tygodniowym: środa → 09:00–12:00 i 12:30–17:00', () => {
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule({ withWedBreak: true })],
      undefined,
      undefined,
      wednesday,
    );
    expect(ranges).toEqual([
      { startTime: '09:00:00', endTime: '12:00:00' },
      { startTime: '12:30:00', endTime: '17:00:00' },
    ]);
  });

  it('dzień specjalny: inne godziny nadpisują grafik tygodniowy', () => {
    const override: ScheduleOverrideDto = {
      date: monday,
      workRanges: [{ startTime: '14:00:00', endTime: '18:00:00' }],
      breaks: [],
    };
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule()],
      [override],
      undefined,
      monday,
    );
    expect(ranges).toEqual([{ startTime: '14:00:00', endTime: '18:00:00' }]);
  });

  it('dzień specjalny: pusta lista workRanges = dzień wolny', () => {
    const override: ScheduleOverrideDto = { date: monday, workRanges: [], breaks: [] };
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule()],
      [override],
      undefined,
      monday,
    );
    expect(ranges).toEqual([]);
  });

  it('zatwierdzony urlop Vacation → brak pasa (priorytet nad override i grafikiem)', () => {
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule()],
      [{ date: monday, workRanges: [{ startTime: '10:00:00', endTime: '14:00:00' }], breaks: [] }],
      [leave({ start: monday, end: monday, type: AbsenceType.Vacation, status: AbsenceStatus.Approved })],
      monday,
    );
    expect(ranges).toEqual([]);
  });

  it('zatwierdzony urlop SickLeave → brak pasa', () => {
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule()],
      undefined,
      [leave({ start: monday, end: monday, type: AbsenceType.SickLeave, status: AbsenceStatus.Approved })],
      monday,
    );
    expect(ranges).toEqual([]);
  });

  it('Pending urlop NIE blokuje — pas pracy z grafiku tygodniowego widoczny', () => {
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule()],
      undefined,
      [leave({ start: monday, end: monday, type: AbsenceType.Vacation, status: AbsenceStatus.Pending })],
      monday,
    );
    expect(ranges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
  });

  it('Rejected urlop ignorowany', () => {
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule()],
      undefined,
      [leave({ start: monday, end: monday, type: AbsenceType.Vacation, status: AbsenceStatus.Rejected })],
      monday,
    );
    expect(ranges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
  });

  it('urlop typu SpecialDay nie blokuje (oddzielna domena ScheduleOverride)', () => {
    const ranges = resolveWorkingRangesForDate(
      [weeklySchedule()],
      undefined,
      [leave({ start: monday, end: monday, type: AbsenceType.SpecialDay, status: AbsenceStatus.Approved })],
      monday,
    );
    expect(ranges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
  });

  it('urlop wielodniowy obejmuje każdy dzień zakresu', () => {
    const start = new Date(2026, 4, 11);
    const end = new Date(2026, 4, 15);
    const leaves = [leave({ start, end, type: AbsenceType.Vacation, status: AbsenceStatus.Approved })];
    expect(resolveWorkingRangesForDate([weeklySchedule()], undefined, leaves, new Date(2026, 4, 12)))
      .toEqual([]);
    expect(resolveWorkingRangesForDate([weeklySchedule()], undefined, leaves, new Date(2026, 4, 15)))
      .toEqual([]);
    // dzień po zakończeniu — wraca grafik tygodniowy (poniedziałek 2026-05-18)
    expect(
      resolveWorkingRangesForDate(
        [weeklySchedule()],
        undefined,
        leaves,
        new Date(2026, 4, 18),
      ),
    ).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
  });
});

/** Tygodniowy grafik STATYCZNY (stałe godziny startu) Pn–Pt z listą `fixedStartTimes`. */
function weeklyFixedSchedule(times: string[]): EmployeeScheduleDto {
  const days = [1, 2, 3, 4, 5].map((cycleIndex) => ({
    cycleIndex,
    workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
    breaks: [],
    fixedStartTimes: times,
  }));
  return {
    activeFrom: new Date(2026, 0, 4),
    activeTo: new Date(2030, 0, 1),
    numberOfCycles: 1,
    days,
    slotGenerationMode: SlotGenerationMode.FixedStartTimes,
  };
}

describe('resolveFixedStartTimesForDate', () => {
  it('grafik statyczny (FixedStartTimes): zwraca stałe godziny startu dnia z cyklu', () => {
    const times = ['09:00:00', '10:30:00', '12:00:00'];
    expect(
      resolveFixedStartTimesForDate([weeklyFixedSchedule(times)], undefined, undefined, monday),
    ).toEqual(times);
  });

  it('grafik dynamiczny (Grid) → null', () => {
    expect(resolveFixedStartTimesForDate([weeklySchedule()], undefined, undefined, monday)).toBeNull();
  });

  it('brak grafiku dla daty (weekend) → null', () => {
    expect(
      resolveFixedStartTimesForDate([weeklyFixedSchedule(['09:00:00'])], undefined, undefined, sunday),
    ).toBeNull();
  });

  it('dzień specjalny w trybie FixedStartTimes nadpisuje grafik tygodniowy', () => {
    const override: ScheduleOverrideDto = {
      date: monday,
      slotGenerationMode: SlotGenerationMode.FixedStartTimes,
      fixedStartTimes: ['14:00:00', '15:00:00'],
    };
    expect(
      resolveFixedStartTimesForDate([weeklySchedule()], [override], undefined, monday),
    ).toEqual(['14:00:00', '15:00:00']);
  });

  it('dzień specjalny w trybie Grid → null mimo statycznego grafiku tygodniowego', () => {
    const override: ScheduleOverrideDto = {
      date: monday,
      slotGenerationMode: SlotGenerationMode.Grid,
      workRanges: [{ startTime: '10:00:00', endTime: '14:00:00' }],
      breaks: [],
    };
    expect(
      resolveFixedStartTimesForDate([weeklyFixedSchedule(['09:00:00'])], [override], undefined, monday),
    ).toBeNull();
  });

  it('zatwierdzony urlop → null (priorytet nad grafikiem statycznym)', () => {
    expect(
      resolveFixedStartTimesForDate(
        [weeklyFixedSchedule(['09:00:00'])],
        undefined,
        [leave({ start: monday, end: monday, type: AbsenceType.Vacation, status: AbsenceStatus.Approved })],
        monday,
      ),
    ).toBeNull();
  });

  it('brak jakiegokolwiek grafiku → null', () => {
    expect(resolveFixedStartTimesForDate(undefined, undefined, undefined, monday)).toBeNull();
  });
});

describe('resolveBreaksForDate', () => {
  it('zwraca przerwy z grafiku tygodniowego', () => {
    const breaks = resolveBreaksForDate(
      [weeklySchedule({ withWedBreak: true })],
      undefined,
      undefined,
      wednesday,
    );
    expect(breaks).toEqual([{ startTime: '12:00:00', endTime: '12:30:00' }]);
  });

  it('nadpisanie dnia specjalnego → przerwy z override', () => {
    const override: ScheduleOverrideDto = {
      date: monday,
      workRanges: [{ startTime: '10:00:00', endTime: '16:00:00' }],
      breaks: [{ startTime: '13:00:00', endTime: '13:30:00' }],
    };
    const breaks = resolveBreaksForDate(
      [weeklySchedule({ withWedBreak: true })],
      [override],
      undefined,
      monday,
    );
    expect(breaks).toEqual([{ startTime: '13:00:00', endTime: '13:30:00' }]);
  });

  it('zatwierdzony urlop → brak przerw', () => {
    const breaks = resolveBreaksForDate(
      [weeklySchedule({ withWedBreak: true })],
      undefined,
      [leave({ start: wednesday, end: wednesday, type: AbsenceType.SickLeave, status: AbsenceStatus.Approved })],
      wednesday,
    );
    expect(breaks).toEqual([]);
  });
});

describe('pickOverrideForDate', () => {
  it('matchuje po dniu kalendarzowym (różne godziny / strefy ignorowane)', () => {
    const override: ScheduleOverrideDto = {
      date: new Date(2026, 4, 11, 8, 30), // 08:30 lokalny czas
      workRanges: [],
      breaks: [],
    };
    expect(pickOverrideForDate([override], new Date(2026, 4, 11, 14, 0))).toBe(override);
  });

  it('zwraca undefined dla daty bez dopasowania', () => {
    const override: ScheduleOverrideDto = { date: monday, workRanges: [], breaks: [] };
    expect(pickOverrideForDate([override], wednesday)).toBeUndefined();
  });
});

describe('findBlockingLeaveForDate', () => {
  it('zatwierdzony Vacation w zakresie → zwraca leave', () => {
    const l = leave({
      start: new Date(2026, 4, 10),
      end: new Date(2026, 4, 14),
      type: AbsenceType.Vacation,
      status: AbsenceStatus.Approved,
    });
    expect(findBlockingLeaveForDate([l], monday)).toBe(l);
  });

  it('zatwierdzony, ale data poza zakresem → undefined', () => {
    const l = leave({
      start: new Date(2026, 4, 10),
      end: new Date(2026, 4, 14),
      type: AbsenceType.Vacation,
      status: AbsenceStatus.Approved,
    });
    expect(findBlockingLeaveForDate([l], new Date(2026, 4, 20))).toBeUndefined();
  });

  it('inne statusy niż Approved → undefined', () => {
    for (const status of [AbsenceStatus.Pending, AbsenceStatus.Rejected]) {
      const l = leave({
        start: monday,
        end: monday,
        type: AbsenceType.Vacation,
        status,
      });
      expect(findBlockingLeaveForDate([l], monday)).toBeUndefined();
    }
  });

  // Kolega z zespołu i Recepcja nie dostają `absenceType` (art. 9 RODO). Gdyby blokada dnia
  // nadal wisiała na typie, kalendarz przestałby chronić czyjś urlop i pozwolił umówić na niego
  // klientkę — a to była PRZYCZYNA maskowania, nie jego dopuszczalny koszt.
  it('zamaskowany powód, ale blocksDay=true → nadal blokuje dzień', () => {
    const l = leave({ start: monday, end: monday, type: null, status: AbsenceStatus.Approved, blocksDay: true });
    expect(findBlockingLeaveForDate([l], monday)).toBe(l);
  });

  it('zamaskowany powód i blocksDay=false (dzień specjalny) → nie blokuje', () => {
    const l = leave({ start: monday, end: monday, type: null, status: AbsenceStatus.Approved, blocksDay: false });
    expect(findBlockingLeaveForDate([l], monday)).toBeUndefined();
  });

  // Rolling deploy: dashboard nowy, API jeszcze bez `blocksDay`. Dopóki typ jest widoczny,
  // stara reguła musi działać — inaczej w oknie wdrożenia znikają wszystkie blokady urlopowe.
  it('brak blocksDay (stare API) → rozstrzyga typ', () => {
    const urlop = leave({ start: monday, end: monday, type: AbsenceType.Vacation, status: AbsenceStatus.Approved });
    const dzienSpecjalny = leave({ start: monday, end: monday, type: AbsenceType.SpecialDay, status: AbsenceStatus.Approved });
    expect(findBlockingLeaveForDate([urlop], monday)).toBe(urlop);
    expect(findBlockingLeaveForDate([dzienSpecjalny], monday)).toBeUndefined();
  });
});

describe('isAppointmentOutsideWorkingHours', () => {
  // Wizyta 10:00–11:00 (minuty od północy).
  const appt = { startMin: 600, endMin: 660 };
  const workday = [{ startMin: 540, endMin: 1020 }]; // 09:00–17:00

  it('NIE flaguje wizyt z przeszłości nawet bez aktywnego grafiku (regresja: dzień miniony)', () => {
    // Sedno błędu: dla minionych dni grafik bywa nieaktywny → ranges puste. Wcześniej każda
    // miniona wizyta dostawała czerwoną kropkę „poza godzinami pracy".
    expect(
      isAppointmentOutsideWorkingHours({
        ...appt,
        ranges: [],
        hasSchedule: true,
        isPastDay: true,
      }),
    ).toBe(false);
  });

  it('dzień przeszły wygrywa nawet gdy wizyta realnie wypada poza pasami', () => {
    expect(
      isAppointmentOutsideWorkingHours({
        startMin: 1200, // 20:00 — poza 09:00–17:00
        endMin: 1260,
        ranges: workday,
        hasSchedule: true,
        isPastDay: true,
      }),
    ).toBe(false);
  });

  it('bez kontekstu grafiku (hasSchedule=false) nie flaguje', () => {
    expect(
      isAppointmentOutsideWorkingHours({
        ...appt,
        ranges: [],
        hasSchedule: false,
        isPastDay: false,
      }),
    ).toBe(false);
  });

  it('dziś/przyszłość: brak pasów pracy przy aktywnym grafiku = poza godzinami', () => {
    expect(
      isAppointmentOutsideWorkingHours({
        ...appt,
        ranges: [],
        hasSchedule: true,
        isPastDay: false,
      }),
    ).toBe(true);
  });

  it('dziś/przyszłość: wizyta w obrębie pasa NIE jest flagowana', () => {
    expect(
      isAppointmentOutsideWorkingHours({
        ...appt,
        ranges: workday,
        hasSchedule: true,
        isPastDay: false,
      }),
    ).toBe(false);
  });

  it('dziś/przyszłość: wizyta wystająca poza pas JEST flagowana', () => {
    expect(
      isAppointmentOutsideWorkingHours({
        startMin: 960, // 16:00
        endMin: 1080, // 18:00 — wystaje poza 17:00
        ranges: workday,
        hasSchedule: true,
        isPastDay: false,
      }),
    ).toBe(true);
  });
});

describe('resolveRawScheduleDayForDate', () => {
  it('grafik tygodniowy Grid: surowe pasy pracy (NIE wycina przerw)', () => {
    const raw = resolveRawScheduleDayForDate(
      [weeklySchedule({ withWedBreak: true })],
      undefined,
      undefined,
      wednesday,
    );
    expect(raw).not.toBeNull();
    expect(raw!.mode).toBe(SlotGenerationMode.Grid);
    // surowy pas to pełne 09:00–17:00, a przerwa jest osobno (w przeciwieństwie do resolveWorkingRanges)
    expect(raw!.workRanges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
    expect(raw!.breaks).toEqual([{ startTime: '12:00:00', endTime: '12:30:00' }]);
  });

  it('zatwierdzony urlop → null', () => {
    expect(
      resolveRawScheduleDayForDate(
        [weeklySchedule()],
        undefined,
        [leave({ start: monday, end: monday, type: AbsenceType.Vacation, status: AbsenceStatus.Approved })],
        monday,
      ),
    ).toBeNull();
  });

  it('brak grafiku obowiązującego dla daty → null', () => {
    expect(resolveRawScheduleDayForDate([weeklySchedule()], undefined, undefined, sunday)).toBeNull();
  });

  it('dzień specjalny nadpisuje grafik (surowe pasy + przerwy z override)', () => {
    const override: ScheduleOverrideDto = {
      date: monday,
      slotGenerationMode: SlotGenerationMode.Grid,
      workRanges: [{ startTime: '10:00:00', endTime: '16:00:00' }],
      breaks: [{ startTime: '13:00:00', endTime: '13:30:00' }],
    };
    const raw = resolveRawScheduleDayForDate([weeklySchedule()], [override], undefined, monday);
    expect(raw!.workRanges).toEqual([{ startTime: '10:00:00', endTime: '16:00:00' }]);
    expect(raw!.breaks).toEqual([{ startTime: '13:00:00', endTime: '13:30:00' }]);
  });

  it('grafik statyczny → mode FixedStartTimes + lista godzin', () => {
    const raw = resolveRawScheduleDayForDate(
      [weeklyFixedSchedule(['09:00:00', '10:30:00'])],
      undefined,
      undefined,
      monday,
    );
    expect(raw!.mode).toBe(SlotGenerationMode.FixedStartTimes);
    expect(raw!.fixedStartTimes).toEqual(['09:00:00', '10:30:00']);
  });
});

describe('canAddGridBreak', () => {
  it('Grid z pasem pracy → true', () => {
    const raw = resolveRawScheduleDayForDate([weeklySchedule()], undefined, undefined, monday);
    expect(canAddGridBreak(raw)).toBe(true);
  });

  it('grafik statyczny (FixedStartTimes) → false', () => {
    const raw = resolveRawScheduleDayForDate(
      [weeklyFixedSchedule(['09:00:00'])],
      undefined,
      undefined,
      monday,
    );
    expect(canAddGridBreak(raw)).toBe(false);
  });

  it('null (urlop / brak grafiku) → false', () => {
    expect(canAddGridBreak(null)).toBe(false);
  });

  it('Grid z pustym pasem (dzień wolny w override) → false', () => {
    const override: ScheduleOverrideDto = {
      date: monday,
      slotGenerationMode: SlotGenerationMode.Grid,
      workRanges: [],
      breaks: [],
    };
    const raw = resolveRawScheduleDayForDate([weeklySchedule()], [override], undefined, monday);
    expect(canAddGridBreak(raw)).toBe(false);
  });
});

describe('isBreakWithinWorkRanges / timeRangesOverlap', () => {
  const work = [{ startTime: '09:00:00', endTime: '17:00:00' }];

  it('przerwa w pasie → true', () => {
    expect(isBreakWithinWorkRanges({ startTime: '12:00:00', endTime: '12:30:00' }, work)).toBe(true);
  });

  it('przerwa wystająca poza pas → false', () => {
    expect(isBreakWithinWorkRanges({ startTime: '16:30:00', endTime: '17:30:00' }, work)).toBe(false);
  });

  it('overlap: nakładające się zakresy', () => {
    expect(
      timeRangesOverlap(
        { startTime: '12:00:00', endTime: '12:30:00' },
        { startTime: '12:15:00', endTime: '12:45:00' },
      ),
    ).toBe(true);
  });

  it('overlap: stykanie się końcami NIE jest kolizją', () => {
    expect(
      timeRangesOverlap(
        { startTime: '12:00:00', endTime: '12:30:00' },
        { startTime: '12:30:00', endTime: '13:00:00' },
      ),
    ).toBe(false);
  });
});

describe('buildGridOverrideDto', () => {
  it('składa override w trybie Grid z surowymi pasami i posortowanymi przerwami', () => {
    const raw = resolveRawScheduleDayForDate([weeklySchedule()], undefined, undefined, monday)!;
    const dto = buildGridOverrideDto(monday, raw, [
      { startTime: '14:00:00', endTime: '14:30:00' },
      { startTime: '11:00:00', endTime: '11:15:00' },
    ]);
    expect(dto.slotGenerationMode).toBe(SlotGenerationMode.Grid);
    expect(dto.workRanges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
    expect(dto.breaks).toEqual([
      { startTime: '11:00:00', endTime: '11:15:00' },
      { startTime: '14:00:00', endTime: '14:30:00' },
    ]);
  });
});

describe('gridDayMatchesWeeklySchedule', () => {
  it('te same pasy + brak przerw = identyczny z grafikiem tygodniowym → true', () => {
    expect(
      gridDayMatchesWeeklySchedule(
        [weeklySchedule()],
        undefined,
        monday,
        [{ startTime: '09:00:00', endTime: '17:00:00' }],
        [],
      ),
    ).toBe(true);
  });

  it('dodatkowa przerwa → różni się od grafiku tygodniowego → false', () => {
    expect(
      gridDayMatchesWeeklySchedule(
        [weeklySchedule()],
        undefined,
        monday,
        [{ startTime: '09:00:00', endTime: '17:00:00' }],
        [{ startTime: '12:00:00', endTime: '12:30:00' }],
      ),
    ).toBe(false);
  });
});

describe('normalizeTimeHms', () => {
  it('uzupełnia do HH:mm:ss', () => {
    expect(normalizeTimeHms('9:5')).toBe('09:05:00');
    expect(normalizeTimeHms('12:30')).toBe('12:30:00');
    expect(normalizeTimeHms('08:00:00')).toBe('08:00:00');
  });
});
