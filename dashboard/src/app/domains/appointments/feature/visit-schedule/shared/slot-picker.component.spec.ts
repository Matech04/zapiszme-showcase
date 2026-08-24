import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import { AppointmentSlotDto } from '@core/api/api-client';
import { SlotPickerComponent } from './slot-picker.component';

describe('SlotPickerComponent', () => {
  let fixture: ComponentFixture<SlotPickerComponent>;
  let component: SlotPickerComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SlotPickerComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(SlotPickerComponent);
    component = fixture.componentInstance;
  });

  function setInputs(p: {
    slots?: AppointmentSlotDto[];
    value?: string | null;
    loading?: boolean;
    loadError?: boolean;
    disabled?: boolean;
  }): void {
    if ('slots' in p) fixture.componentRef.setInput('slots', p.slots ?? []);
    if ('value' in p) fixture.componentRef.setInput('value', p.value ?? null);
    if ('loading' in p) fixture.componentRef.setInput('loading', !!p.loading);
    if ('loadError' in p) fixture.componentRef.setInput('loadError', !!p.loadError);
    if ('disabled' in p) fixture.componentRef.setInput('disabled', !!p.disabled);
    fixture.detectChanges();
  }

  it('rozdziela sloty na preferred i other', () => {
    setInputs({
      slots: [
        { slot: '09:00:00', isPreferred: false },
        { slot: '10:00:00', isPreferred: true },
        { slot: '11:00:00' }, // brak isPreferred = traktowany jak other
      ] as AppointmentSlotDto[],
    });
    const preferred = (
      component as unknown as { preferredSlots: () => AppointmentSlotDto[] }
    ).preferredSlots();
    const other = (
      component as unknown as { otherSlots: () => AppointmentSlotDto[] }
    ).otherSlots();
    expect(preferred.map((s) => s.slot)).toEqual(['10:00:00']);
    expect(other.map((s) => s.slot)).toEqual(['09:00:00', '11:00:00']);
  });

  it('emituje valueChange po kliknięciu w slot', () => {
    let emitted: string | null = null;
    component.valueChange.subscribe((v) => (emitted = v));
    setInputs({
      slots: [{ slot: '10:30:00', isPreferred: true }] as AppointmentSlotDto[],
    });
    (component as unknown as { select: (s: AppointmentSlotDto) => void }).select({
      slot: '10:30:00',
    } as AppointmentSlotDto);
    expect(emitted).toBe('10:30:00');
  });

  it('isSelected porównuje z input.value', () => {
    setInputs({
      slots: [{ slot: '12:00:00' }] as AppointmentSlotDto[],
      value: '12:00:00',
    });
    expect(
      (component as unknown as { isSelected: (s: AppointmentSlotDto) => boolean }).isSelected({
        slot: '12:00:00',
      } as AppointmentSlotDto),
    ).toBe(true);
    expect(
      (component as unknown as { isSelected: (s: AppointmentSlotDto) => boolean }).isSelected({
        slot: '13:00:00',
      } as AppointmentSlotDto),
    ).toBe(false);
  });

  it('shortTime obcina sekundy', () => {
    setInputs({});
    const fn = (component as unknown as { shortTime: (t: string) => string }).shortTime;
    expect(fn('12:34:56')).toBe('12:34');
    expect(fn('09:00')).toBe('09:00');
  });
});
