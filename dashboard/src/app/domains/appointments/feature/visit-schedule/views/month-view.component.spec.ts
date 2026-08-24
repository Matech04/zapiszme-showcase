import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import {
  AppointmentPreviewDto,
  AppointmentStatus,
  EmployeeScheduleDto,
  ScheduleOverrideDto,
  SlotGenerationMode,
} from '@core/api/api-client';
import { MonthViewComponent, type MonthStaticSlots } from './month-view.component';

function appt(
  partial: Partial<AppointmentPreviewDto> & { statusName?: string },
): AppointmentPreviewDto {
  const { statusName, ...rest } = partial;
  const status = statusName ? ({ name: statusName } as AppointmentStatus) : undefined;
  return { ...rest, status } as AppointmentPreviewDto;
}

function yyyyMmDd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const anchor = new Date(2026, 5, 1); // czerwiec 2026
const target = new Date(2026, 5, 12); // 2026-06-12 — dzień w bieżącym miesiącu
const dow = target.getDay(); // cycleIndex dla cyklu 1-tygodniowego

/** Grafik tygodniowy STATYCZNY z podanymi godzinami startu dla dnia tygodnia `dow`. */
function staticSchedule(times: string[]): EmployeeScheduleDto[] {
  return [
    {
      activeFrom: new Date(2026, 0, 1),
      activeTo: new Date(2026, 11, 31),
      numberOfCycles: 1,
      slotGenerationMode: SlotGenerationMode.FixedStartTimes,
      days: [{ cycleIndex: dow, workRanges: [], breaks: [], fixedStartTimes: times }],
    } as unknown as EmployeeScheduleDto,
  ];
}

/** Grafik tygodniowy DYNAMICZNY (Grid) — bloki pracy zamiast stałych godzin. */
function gridSchedule(): EmployeeScheduleDto[] {
  return [
    {
      activeFrom: new Date(2026, 0, 1),
      activeTo: new Date(2026, 11, 31),
      numberOfCycles: 1,
      slotGenerationMode: SlotGenerationMode.Grid,
      days: [
        {
          cycleIndex: dow,
          workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
          breaks: [],
          fixedStartTimes: [],
        },
      ],
    } as unknown as EmployeeScheduleDto,
  ];
}

function staticSlotsCtx(
  sched: EmployeeScheduleDto[],
  busy: Record<string, { s: number; e: number }[]> = {},
  overrides: ScheduleOverrideDto[] = [],
): MonthStaticSlots {
  return {
    cfg: { sched, overrides, leaves: [] },
    busy: new Map(Object.entries(busy)),
  };
}

describe('MonthViewComponent — sloty grafiku statycznego', () => {
  let fixture: ComponentFixture<MonthViewComponent>;
  let component: MonthViewComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [MonthViewComponent] }).compileComponents();
    fixture = TestBed.createComponent(MonthViewComponent);
    component = fixture.componentInstance;
  });

  function setInputs(opts: {
    appointments?: AppointmentPreviewDto[];
    staticSlots?: MonthStaticSlots | null;
  }): void {
    fixture.componentRef.setInput('anchor', anchor);
    fixture.componentRef.setInput('selected', target);
    fixture.componentRef.setInput('appointments', opts.appointments ?? []);
    fixture.componentRef.setInput('staticSlots', opts.staticSlots ?? null);
    fixture.detectChanges();
  }

  function cellFor(d: Date) {
    return component
      .cells()
      .find(
        (c) =>
          c.date.getFullYear() === d.getFullYear() &&
          c.date.getMonth() === d.getMonth() &&
          c.date.getDate() === d.getDate(),
      );
  }

  it('dzień statyczny → fixedSlots z oznaczeniem zajętości; liczba wolnych poprawna', () => {
    setInputs({
      staticSlots: staticSlotsCtx(
        staticSchedule(['09:00:00', '10:00:00', '11:00:00', '12:00:00']),
        { [yyyyMmDd(target)]: [{ s: 11 * 60, e: 11 * 60 + 30 }] }, // 11:00 zajęty
      ),
    });

    const cell = cellFor(target);
    expect(cell?.fixedSlots).toEqual([
      { time: '09:00', taken: false },
      { time: '10:00', taken: false },
      { time: '11:00', taken: true },
      { time: '12:00', taken: false },
    ]);
    expect(cell?.fixedSlots?.filter((s) => !s.taken).length).toBe(3);
  });

  it('nadpis statyczny (override) ma priorytet nad grafikiem tygodniowym', () => {
    setInputs({
      staticSlots: staticSlotsCtx(
        staticSchedule(['09:00:00', '10:00:00']),
        {},
        [
          {
            date: target,
            slotGenerationMode: SlotGenerationMode.FixedStartTimes,
            fixedStartTimes: ['15:00:00'],
            workRanges: [],
            breaks: [],
          } as unknown as ScheduleOverrideDto,
        ],
      ),
    });

    const cell = cellFor(target);
    expect(cell?.fixedSlots).toEqual([{ time: '15:00', taken: false }]);
  });

  it('dzień dynamiczny (Grid) → fixedSlots null, chipy wizyt jak dotąd', () => {
    setInputs({
      appointments: [appt({ id: 'a', date: target, startTime: '09:00:00', serviceName: 'Strzyżenie', statusName: 'Booked' })],
      staticSlots: staticSlotsCtx(gridSchedule()),
    });

    const cell = cellFor(target);
    expect(cell?.fixedSlots).toBeNull();
    expect(cell?.total).toBe(1);
    expect(cell?.items[0]?.serviceName).toBe('Strzyżenie');
  });

  it('brak staticSlots (wielu pracowników) → fixedSlots null, chipy wizyt (regresja)', () => {
    setInputs({
      appointments: [appt({ id: 'a', date: target, startTime: '09:00:00', serviceName: 'Manicure', statusName: 'Booked' })],
      staticSlots: null,
    });

    const cell = cellFor(target);
    expect(cell?.fixedSlots).toBeNull();
    expect(cell?.total).toBe(1);
  });
});
