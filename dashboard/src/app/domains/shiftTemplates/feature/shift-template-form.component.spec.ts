import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ShiftTemplatesClient, SlotGenerationMode } from '@core/api/api-client';
import { ShiftTemplateFormComponent } from './shift-template-form.component';

describe('ShiftTemplateFormComponent — zapis wg trybu', () => {
  let component: ShiftTemplateFormComponent;
  let fixture: ComponentFixture<ShiftTemplateFormComponent>;
  let create: ReturnType<typeof vi.fn>;

  async function setup() {
    create = vi.fn().mockReturnValue(of('new-id'));
    await TestBed.configureTestingModule({
      imports: [ShiftTemplateFormComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        {
          provide: ShiftTemplatesClient,
          useValue: {
            createShiftTemplate: create,
            updateShiftTemplate: vi.fn().mockReturnValue(of({})),
            getShiftTemplateById: vi.fn().mockReturnValue(of(undefined)),
          },
        },
        { provide: MessageService, useValue: { add: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ShiftTemplateFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('tryb stały: createShiftTemplate z fixedStartTimes (sort+ISO), puste workRanges', async () => {
    await setup();
    component.name.set('Stałe poranne');
    component.mode.set(SlotGenerationMode.FixedStartTimes);
    component.fixedStartTimes.set(['12:00', '09:00']);

    component.save();

    expect(create).toHaveBeenCalledTimes(1);
    const payload = create.mock.calls[0][0] as {
      slotGenerationMode: number;
      fixedStartTimes?: string[];
      workRanges: unknown[];
    };
    expect(payload.slotGenerationMode).toBe(SlotGenerationMode.FixedStartTimes);
    expect(payload.fixedStartTimes).toEqual(['09:00:00', '12:00:00']);
    expect(payload.workRanges).toEqual([]);
  });

  it('tryb siatki: createShiftTemplate z workRanges, bez fixedStartTimes', async () => {
    await setup();
    component.name.set('Pełny dzień');
    component.mode.set(SlotGenerationMode.Grid);
    component.workRanges.set([{ startTime: '09:00', endTime: '17:00' }]);
    component.breaks.set([]);

    component.save();

    expect(create).toHaveBeenCalledTimes(1);
    const payload = create.mock.calls[0][0] as {
      slotGenerationMode: number;
      fixedStartTimes?: string[];
      workRanges: { startTime: string; endTime: string }[];
    };
    expect(payload.slotGenerationMode).toBe(SlotGenerationMode.Grid);
    expect(payload.workRanges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
    expect(payload.fixedStartTimes).toBeUndefined();
  });
});

describe('ShiftTemplateFormComponent — edycja (load + update)', () => {
  let component: ShiftTemplateFormComponent;
  let fixture: ComponentFixture<ShiftTemplateFormComponent>;
  let update: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    update = vi.fn().mockReturnValue(of({}));
    await TestBed.configureTestingModule({
      imports: [ShiftTemplateFormComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        {
          provide: ShiftTemplatesClient,
          useValue: {
            createShiftTemplate: vi.fn().mockReturnValue(of('x')),
            updateShiftTemplate: update,
            getShiftTemplateById: vi.fn().mockReturnValue(
              of({
                id: 'tpl1', name: 'Stałe', slotGenerationMode: SlotGenerationMode.FixedStartTimes,
                workRanges: [], breaks: [], fixedStartTimes: ['09:00:00', '12:00:00'],
              }),
            ),
          },
        },
        { provide: MessageService, useValue: { add: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ShiftTemplateFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'tpl1');
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('wczytuje istniejący szablon (nazwa/tryb/godziny)', () => {
    expect(component.name()).toBe('Stałe');
    expect(component.mode()).toBe(SlotGenerationMode.FixedStartTimes);
    expect(component.fixedStartTimes()).toEqual(['09:00', '12:00']);
  });

  it('zapis w trybie edycji woła updateShiftTemplate z payloadem', () => {
    component.save();
    expect(update).toHaveBeenCalledTimes(1);
    expect(update.mock.calls[0][0]).toBe('tpl1');
    const payload = update.mock.calls[0][1] as { slotGenerationMode: number; fixedStartTimes?: string[] };
    expect(payload.slotGenerationMode).toBe(SlotGenerationMode.FixedStartTimes);
    expect(payload.fixedStartTimes).toEqual(['09:00:00', '12:00:00']);
  });
});
