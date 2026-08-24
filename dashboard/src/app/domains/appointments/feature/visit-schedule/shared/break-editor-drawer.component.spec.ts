import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import {
  AppointmentPreviewDto,
  EmployeesClient,
  EmployeeScheduleDto,
  ScheduleOverrideDto,
  SlotGenerationMode,
} from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { ScheduleOverridesApiService } from '@core/api/schedule-overrides-api.service';
import { BreakEditorDrawerComponent } from './break-editor-drawer.component';

/** Grid 09:00–17:00 dla każdego dnia tygodnia (cycleIndex 0..6) — dzień daty nieistotny. */
function gridSchedule(): EmployeeScheduleDto {
  return {
    activeFrom: new Date(2026, 0, 1),
    activeTo: new Date(2031, 0, 1),
    numberOfCycles: 1,
    slotGenerationMode: SlotGenerationMode.Grid,
    days: [0, 1, 2, 3, 4, 5, 6].map((cycleIndex) => ({
      cycleIndex,
      workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
      breaks: [],
    })),
  };
}

function fixedSchedule(): EmployeeScheduleDto {
  return {
    activeFrom: new Date(2026, 0, 1),
    activeTo: new Date(2031, 0, 1),
    numberOfCycles: 1,
    slotGenerationMode: SlotGenerationMode.FixedStartTimes,
    days: [0, 1, 2, 3, 4, 5, 6].map((cycleIndex) => ({
      cycleIndex,
      workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
      breaks: [],
      fixedStartTimes: ['09:00:00', '10:30:00'],
    })),
  };
}

// Data w przyszłości (po „dziś" = 2026-06-18) i w zakresie grafiku.
const FUTURE_DATE = '2027-03-15';

function timeAt(h: number, m: number): Date {
  const d = new Date(2027, 2, 15);
  d.setHours(h, m, 0, 0);
  return d;
}

describe('BreakEditorDrawerComponent', () => {
  let fixture: ComponentFixture<BreakEditorDrawerComponent>;
  let component: BreakEditorDrawerComponent;
  let setOverride: ReturnType<typeof vi.fn>;
  let removeOverride: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    setOverride = vi.fn().mockReturnValue(of({ appointmentsOutsideSchedule: [] }));
    removeOverride = vi.fn().mockReturnValue(of(null));

    await TestBed.configureTestingModule({
      imports: [BreakEditorDrawerComponent],
      providers: [
        provideRouter([]),
        { provide: EmployeesClient, useValue: { setScheduleOverride: setOverride } },
        { provide: ScheduleOverridesApiService, useValue: { removeOverride } },
        { provide: AuthSessionService, useValue: { currentEmployeeId: () => 'emp-1' } },
        { provide: MessageService, useValue: { add: vi.fn() } },
        // confirm() w deleteBreak — mock auto-akceptuje (wywołuje accept).
        { provide: ConfirmationService, useValue: { confirm: vi.fn((c: { accept?: () => void }) => c.accept?.()) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BreakEditorDrawerComponent);
    component = fixture.componentInstance;
  });

  function open(opts: {
    schedule?: EmployeeScheduleDto;
    overrides?: ScheduleOverrideDto[];
    appts?: AppointmentPreviewDto[];
    step?: number;
  } = {}): void {
    fixture.componentRef.setInput('schedules', [opts.schedule ?? gridSchedule()]);
    fixture.componentRef.setInput('overrides', opts.overrides ?? []);
    fixture.componentRef.setInput('leaves', []);
    fixture.componentRef.setInput('dayAppointments', opts.appts ?? []);
    fixture.componentRef.setInput('slotStepMinutes', opts.step ?? 15);
    fixture.componentRef.setInput('context', { employeeId: 'emp-1', date: FUTURE_DATE });
    fixture.detectChanges();
  }

  it('grafik dynamiczny: canAddBreak=true, domyślny czas = start grafiku + krok salonu', () => {
    open({ step: 30 });
    expect(component.canAddBreak()).toBe(true);
    // dzień nie jest dziś → start = początek pasa pracy (09:00), długość = krok salonu (30)
    expect(component['startTime']()?.getHours()).toBe(9);
    expect(component['startTime']()?.getMinutes()).toBe(0);
    expect(component['durationMinutes']()).toBe(30);
    expect(component.canSubmit()).toBe(true);
    expect(component['validationError']()).toBeNull();
  });

  it('grafik statyczny: canAddBreak=false (przerwa niedostępna)', () => {
    open({ schedule: fixedSchedule() });
    expect(component.canAddBreak()).toBe(false);
    expect(component.canSubmit()).toBe(false);
  });

  it('przerwa poza godzinami pracy → błąd walidacji', () => {
    open();
    component['startTime'].set(timeAt(16, 45));
    component['endTime'].set(timeAt(17, 30)); // poza 17:00
    expect(component['validationError']()).toBe('Przerwa musi mieścić się w godzinach pracy.');
    expect(component.canSubmit()).toBe(false);
  });

  it('koniec przed początkiem → błąd', () => {
    open();
    component['startTime'].set(timeAt(12, 0));
    component['endTime'].set(timeAt(11, 30));
    expect(component['validationError']()).toBe('Koniec przerwy musi być po jej początku.');
  });

  it('kolizja z wizytą (nieanulowaną) → błąd; anulowana NIE blokuje', () => {
    open({
      appts: [
        {
          startTime: '12:00:00',
          endTime: '12:45:00',
          status: { id: 2, name: 'Booked' },
        } as AppointmentPreviewDto,
      ],
    });
    component['startTime'].set(timeAt(12, 15));
    component['endTime'].set(timeAt(12, 30));
    expect(component['validationError']()).toBe('W tym czasie jest już wizyta — wybierz inny termin.');

    // ta sama wizyta, ale anulowana (status 5) — nie blokuje
    fixture.componentRef.setInput('dayAppointments', [
      { startTime: '12:00:00', endTime: '12:45:00', status: { id: 5, name: 'Canceled' } } as AppointmentPreviewDto,
    ]);
    fixture.detectChanges();
    expect(component['validationError']()).toBeNull();
  });

  it('submit wysyła override Grid z surowym pasem + nową przerwą i datą yyyy-MM-dd', () => {
    open({ step: 15 });
    component['startTime'].set(timeAt(13, 0));
    component['endTime'].set(timeAt(13, 30));
    expect(component.canSubmit()).toBe(true);

    const successSpy = vi.fn();
    component.success.subscribe(successSpy);
    component.submit();

    expect(setOverride).toHaveBeenCalledTimes(1);
    const [empId, dto] = setOverride.mock.calls[0];
    expect(empId).toBe('emp-1');
    expect(dto.date).toBe(FUTURE_DATE);
    expect(dto.slotGenerationMode).toBe(SlotGenerationMode.Grid);
    expect(dto.workRanges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
    expect(dto.breaks).toEqual([{ startTime: '13:00:00', endTime: '13:30:00' }]);
    expect(successSpy).toHaveBeenCalledTimes(1);
  });

  it('przerwa nachodząca na istniejącą przerwę → błąd', () => {
    open({
      overrides: [
        {
          date: new Date(2027, 2, 15),
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
          breaks: [{ startTime: '11:00:00', endTime: '11:30:00' }],
        },
      ],
    });
    component['startTime'].set(timeAt(11, 15));
    component['endTime'].set(timeAt(11, 45));
    expect(component['validationError']()).toBe('Ta przerwa nachodzi na inną przerwę.');
    expect(component.canSubmit()).toBe(false);
  });

  it('prefill startTime (tap w slot) ustawia początek na klikniętą godzinę', () => {
    fixture.componentRef.setInput('schedules', [gridSchedule()]);
    fixture.componentRef.setInput('overrides', []);
    fixture.componentRef.setInput('leaves', []);
    fixture.componentRef.setInput('dayAppointments', []);
    fixture.componentRef.setInput('slotStepMinutes', 15);
    fixture.componentRef.setInput('context', {
      employeeId: 'emp-1',
      date: FUTURE_DATE,
      startTime: '15:00',
    });
    fixture.detectChanges();
    expect(component['startTime']()?.getHours()).toBe(15);
    expect(component['startTime']()?.getMinutes()).toBe(0);
    expect(component['durationMinutes']()).toBe(15);
  });

  it('domyślny start omija istniejącą przerwę (nie ląduje na zajętym oknie)', () => {
    // przerwa 09:00–09:30 → domyślny start powinien wypaść po niej (09:30), nie na 09:00
    open({
      overrides: [
        {
          date: new Date(2027, 2, 15),
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
          breaks: [{ startTime: '09:00:00', endTime: '09:30:00' }],
        },
      ],
    });
    expect(component['startTime']()?.getHours()).toBe(9);
    expect(component['startTime']()?.getMinutes()).toBe(30);
    expect(component['validationError']()).toBeNull();
  });

  it('submit dokłada przerwę do istniejących przerw dnia specjalnego', () => {
    open({
      overrides: [
        {
          date: new Date(2027, 2, 15),
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
          breaks: [{ startTime: '11:00:00', endTime: '11:15:00' }],
        },
      ],
    });
    component['startTime'].set(timeAt(14, 0));
    component['endTime'].set(timeAt(14, 30));
    component.submit();

    const dto = setOverride.mock.calls[0][1];
    expect(dto.breaks).toEqual([
      { startTime: '11:00:00', endTime: '11:15:00' },
      { startTime: '14:00:00', endTime: '14:30:00' },
    ]);
  });

  // ── Tryb edycji / usuwania ────────────────────────────────────────────────

  function openEdit(editBreak: { startTime: string; endTime: string }, existing: { startTime: string; endTime: string }[]): void {
    fixture.componentRef.setInput('schedules', [gridSchedule()]);
    fixture.componentRef.setInput('overrides', [
      {
        date: new Date(2027, 2, 15),
        slotGenerationMode: SlotGenerationMode.Grid,
        workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
        breaks: existing,
      },
    ]);
    fixture.componentRef.setInput('leaves', []);
    fixture.componentRef.setInput('dayAppointments', []);
    fixture.componentRef.setInput('slotStepMinutes', 15);
    fixture.componentRef.setInput('context', { employeeId: 'emp-1', date: FUTURE_DATE, editBreak });
    fixture.detectChanges();
  }

  it('edycja: prefill z edytowanej przerwy + isEditMode', () => {
    openEdit({ startTime: '11:00:00', endTime: '11:30:00' }, [{ startTime: '11:00:00', endTime: '11:30:00' }]);
    expect(component.isEditMode()).toBe(true);
    expect(component['startTime']()?.getHours()).toBe(11);
    expect(component['startTime']()?.getMinutes()).toBe(0);
    expect(component['endTime']()?.getMinutes()).toBe(30);
  });

  it('edycja: zapis ZASTĘPUJE przerwę (nie dubluje)', () => {
    openEdit({ startTime: '11:00:00', endTime: '11:30:00' }, [{ startTime: '11:00:00', endTime: '11:30:00' }]);
    component['startTime'].set(timeAt(11, 15));
    component['endTime'].set(timeAt(11, 45));
    expect(component.canSubmit()).toBe(true);
    component.submit();
    const dto = setOverride.mock.calls[0][1];
    expect(dto.breaks).toEqual([{ startTime: '11:15:00', endTime: '11:45:00' }]);
  });

  it('edycja: overlap liczony bez samej siebie (rozszerzenie własnego zakresu OK)', () => {
    openEdit({ startTime: '11:00:00', endTime: '11:30:00' }, [{ startTime: '11:00:00', endTime: '11:30:00' }]);
    component['startTime'].set(timeAt(11, 0));
    component['endTime'].set(timeAt(11, 45));
    expect(component['validationError']()).toBeNull();
  });

  it('edycja: overlap z INNĄ przerwą → błąd', () => {
    openEdit(
      { startTime: '11:00:00', endTime: '11:30:00' },
      [
        { startTime: '11:00:00', endTime: '11:30:00' },
        { startTime: '13:00:00', endTime: '13:30:00' },
      ],
    );
    component['startTime'].set(timeAt(12, 45));
    component['endTime'].set(timeAt(13, 15));
    expect(component['validationError']()).toBe('Ta przerwa nachodzi na inną przerwę.');
  });

  it('usuwanie: deleteBreak → removeOverride (dzień wraca do grafiku bazowego)', () => {
    openEdit({ startTime: '11:00:00', endTime: '11:30:00' }, [{ startTime: '11:00:00', endTime: '11:30:00' }]);
    const successSpy = vi.fn();
    component.success.subscribe(successSpy);
    component.deleteBreak();
    expect(removeOverride).toHaveBeenCalledWith('emp-1', FUTURE_DATE);
    expect(setOverride).not.toHaveBeenCalled();
    expect(successSpy).toHaveBeenCalledTimes(1);
  });
});
