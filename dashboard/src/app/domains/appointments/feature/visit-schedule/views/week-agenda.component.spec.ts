import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import {
  AppointmentPreviewDto,
  AppointmentStatus,
  EmployeeScheduleDto,
  SlotGenerationMode,
} from '@core/api/api-client';
import { WeekAgendaComponent } from './week-agenda.component';

function appt(
  partial: Partial<AppointmentPreviewDto> & { statusName?: string; statusId?: number },
): AppointmentPreviewDto {
  const { statusName, statusId, ...rest } = partial;
  const status: AppointmentStatus | undefined =
    statusName || statusId
      ? ({ id: statusId, name: statusName } as AppointmentStatus)
      : undefined;
  return { ...rest, status } as AppointmentPreviewDto;
}

/** Grafik tygodniowy DYNAMICZNY (Grid) Pn–Pt 10–16. */
function gridWeekly(
  workRanges: { startTime: string; endTime: string }[] = [
    { startTime: '10:00:00', endTime: '16:00:00' },
  ],
): EmployeeScheduleDto {
  return {
    activeFrom: new Date(2026, 0, 4),
    activeTo: new Date(2030, 0, 1),
    numberOfCycles: 1,
    slotGenerationMode: SlotGenerationMode.Grid,
    days: [1, 2, 3, 4, 5].map((cycleIndex) => ({ cycleIndex, workRanges, breaks: [] })),
  } as unknown as EmployeeScheduleDto;
}

/** Grafik STAŁY (FixedStartTimes) Pn–Pt z listą godzin startu. */
function fixedWeekly(
  fixedStartTimes: string[] = ['09:00:00', '10:00:00', '11:00:00', '12:00:00'],
): EmployeeScheduleDto {
  return {
    activeFrom: new Date(2026, 0, 4),
    activeTo: new Date(2030, 0, 1),
    numberOfCycles: 1,
    slotGenerationMode: SlotGenerationMode.FixedStartTimes,
    days: [1, 2, 3, 4, 5].map((cycleIndex) => ({
      cycleIndex,
      fixedStartTimes,
      workRanges: [],
      breaks: [],
    })),
  } as unknown as EmployeeScheduleDto;
}

const monday = new Date(2026, 4, 11); // 2026-05-11 (poniedziałek)

describe('WeekAgendaComponent', () => {
  let fixture: ComponentFixture<WeekAgendaComponent>;
  let component: WeekAgendaComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WeekAgendaComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(WeekAgendaComponent);
    component = fixture.componentInstance;
  });

  function setInputs(
    appointments: AppointmentPreviewDto[],
    schedules?: EmployeeScheduleDto[],
  ): void {
    fixture.componentRef.setInput('anchor', monday);
    fixture.componentRef.setInput('appointments', appointments);
    if (schedules) fixture.componentRef.setInput('schedules', schedules);
    fixture.detectChanges();
  }

  it('renderuje 7 dni z poniedziałkiem jako pierwszym', () => {
    setInputs([]);
    const days = component.days();
    expect(days).toHaveLength(7);
    expect(days[0].weekday).toBe('Poniedziałek');
    expect(days[6].weekday).toBe('Niedziela');
  });

  it('liczy wizyty per dzień (bez szczegółów)', () => {
    setInputs([
      appt({ id: 'a', date: monday, startTime: '14:00:00', statusName: 'Booked' }),
      appt({ id: 'b', date: monday, startTime: '09:00:00', statusName: 'Booked' }),
      appt({ id: 'c', date: new Date(2026, 4, 13), startTime: '10:00:00', statusName: 'Booked' }),
    ]);
    const days = component.days();
    expect(days[0].count).toBe(2); // poniedziałek
    expect(days[1].count).toBe(0); // wtorek
    expect(days[2].count).toBe(1); // środa
  });

  it('licznik pomija canceled, uwzględnia pending (filterAppointments)', () => {
    setInputs([
      appt({ id: 'ok', date: monday, startTime: '09:00:00', statusName: 'Booked' }),
      appt({ id: 'pend', date: monday, startTime: '10:00:00', statusName: 'Pending' }),
      appt({ id: 'cnx', date: monday, startTime: '11:00:00', statusName: 'Canceled' }),
    ]);
    expect(component.days()[0].count).toBe(2);
  });

  it('czas pracy: dzień z grafiku Grid → „HH:MM–HH:MM", dzień poza grafikiem → „Dzień wolny"', () => {
    setInputs([], [gridWeekly()]);
    const days = component.days();
    expect(days[0].workLabel).toBe('10:00–16:00'); // poniedziałek (grafik Pn–Pt)
    expect(days[0].isDayOff).toBe(false);
    expect(days[5].workLabel).toBe('Dzień wolny'); // sobota — brak dnia w cyklu
    expect(days[5].isDayOff).toBe(true);
  });

  it('czas pracy: grafik STAŁY → liczba terminów + start (14:00 to ostatni START, nie koniec)', () => {
    setInputs([], [fixedWeekly(['09:00:00', '10:00:00', '11:00:00', '12:00:00'])]);
    const days = component.days();
    expect(days[0].workLabel).toBe('4 terminy · od 09:00'); // poniedziałek
    expect(days[0].workIcon).toBe('pi pi-calendar');
    expect(days[0].isDayOff).toBe(false);
    expect(days[6].workLabel).toBe('Dzień wolny'); // niedziela — brak dnia w cyklu
  });

  it('grafik stały z jednym terminem → „1 termin · od HH:MM"', () => {
    setInputs([], [fixedWeekly(['09:00:00'])]);
    expect(component.days()[0].workLabel).toBe('1 termin · od 09:00');
  });

  it('bez kontekstu grafiku workLabel = null (nie twierdzimy „Dzień wolny")', () => {
    setInputs([]); // brak schedules/overrides/leaves
    expect(component.days().every((d) => d.workLabel === null)).toBe(true);
  });

  it('rangeLabel pokazuje zakres pn–nd', () => {
    setInputs([]);
    expect(component.rangeLabel()).toMatch(/–/);
  });
});
