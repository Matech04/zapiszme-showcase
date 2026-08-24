import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import { AppointmentPreviewDto, AppointmentStatus } from '@core/api/api-client';
import { MonthDaySheetComponent } from './month-day-sheet.component';

function appt(
  partial: Partial<AppointmentPreviewDto> & {
    statusName?: string;
    statusId?: number;
  },
): AppointmentPreviewDto {
  const { statusName, statusId, ...rest } = partial;
  const status: AppointmentStatus | undefined =
    statusName || statusId
      ? ({ id: statusId, name: statusName } as AppointmentStatus)
      : undefined;
  return { ...rest, status } as AppointmentPreviewDto;
}

describe('MonthDaySheetComponent', () => {
  let fixture: ComponentFixture<MonthDaySheetComponent>;
  let component: MonthDaySheetComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MonthDaySheetComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(MonthDaySheetComponent);
    component = fixture.componentInstance;
  });

  function setInputs(partial: {
    date?: Date | null;
    appointments?: AppointmentPreviewDto[];
    isDesktop?: boolean;
    canCreate?: boolean;
  }): void {
    if ('date' in partial) {
      fixture.componentRef.setInput('date', partial.date ?? null);
    }
    if ('appointments' in partial) {
      fixture.componentRef.setInput('appointments', partial.appointments ?? []);
    }
    if ('isDesktop' in partial) {
      fixture.componentRef.setInput('isDesktop', !!partial.isDesktop);
    }
    if ('canCreate' in partial) {
      fixture.componentRef.setInput('canCreate', !!partial.canCreate);
    }
    fixture.detectChanges();
  }

  it('startowo niewidoczny gdy date null', () => {
    setInputs({ date: null });
    expect((component as unknown as { isVisible: () => boolean }).isVisible()).toBe(false);
  });

  it('po ustawieniu date isVisible przechodzi na true', () => {
    setInputs({ date: new Date(2026, 4, 15) });
    expect((component as unknown as { isVisible: () => boolean }).isVisible()).toBe(true);
  });

  it('items sortuje po startTime rosnąco', () => {
    setInputs({
      date: new Date(2026, 4, 15),
      appointments: [
        appt({ id: 'a', startTime: '14:00:00', endTime: '15:00:00' }),
        appt({ id: 'b', startTime: '09:00:00', endTime: '10:00:00' }),
        appt({ id: 'c', startTime: '11:30:00', endTime: '12:00:00' }),
      ],
    });
    const items = (
      component as unknown as { items: () => Array<{ raw: AppointmentPreviewDto }> }
    ).items();
    expect(items.map((i) => i.raw.id)).toEqual(['b', 'c', 'a']);
  });

  it('summaryLabel oznacza pending wizyty osobno', () => {
    setInputs({
      date: new Date(2026, 4, 15),
      appointments: [
        appt({ id: 'a', startTime: '09:00:00', statusName: 'Pending' }),
        appt({ id: 'b', startTime: '10:00:00', statusName: 'Booked' }),
      ],
    });
    const label = (component as unknown as { summaryLabel: () => string }).summaryLabel();
    expect(label).toContain('2');
    expect(label).toContain('1 do zatwierdzenia');
  });

  it('summaryLabel bez pending nie dokleja podsumowania', () => {
    setInputs({
      date: new Date(2026, 4, 15),
      appointments: [appt({ id: 'a', startTime: '09:00:00', statusName: 'Booked' })],
    });
    const label = (component as unknown as { summaryLabel: () => string }).summaryLabel();
    expect(label).not.toContain('do zatwierdzenia');
  });

  it('emituje openDayView z otrzymaną datą', () => {
    const d = new Date(2026, 4, 15);
    let emitted: Date | null = null;
    component.openDayView.subscribe((x) => (emitted = x));
    setInputs({ date: d });
    component.openDayView.emit(d);
    expect(emitted).toEqual(d);
  });

  it('emituje appointmentSelected po tapie w wizytę', () => {
    let picked: AppointmentPreviewDto | null = null;
    component.appointmentSelected.subscribe((a) => (picked = a));
    const a = appt({ id: 'x', startTime: '12:00:00' });
    setInputs({ date: new Date(2026, 4, 15), appointments: [a] });
    (
      component as unknown as { onPick: (a: AppointmentPreviewDto) => void }
    ).onPick(a);
    expect(picked!.id).toBe('x');
  });

  it('position desktop=right, mobile=bottom', () => {
    setInputs({ date: new Date(2026, 4, 15), isDesktop: true });
    expect(
      (component as unknown as { position: () => 'right' | 'bottom' }).position(),
    ).toBe('right');
    setInputs({ isDesktop: false });
    expect(
      (component as unknown as { position: () => 'right' | 'bottom' }).position(),
    ).toBe('bottom');
  });

  it('zamknięcie drawera emituje closeRequested gdy date był ustawiony', () => {
    let closed = false;
    component.closeRequested.subscribe(() => (closed = true));
    setInputs({ date: new Date(2026, 4, 15) });
    (component as unknown as { onVisibleChange: (v: boolean) => void }).onVisibleChange(false);
    expect(closed).toBe(true);
  });
});
