import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ShiftTemplateDto, ShiftTemplatesClient, SlotGenerationMode } from '@core/api/api-client';
import { ShiftTemplatesListComponent } from './shift-templates-list.component';

describe('ShiftTemplatesListComponent — tryb + edycja', () => {
  let component: ShiftTemplatesListComponent;
  let fixture: ComponentFixture<ShiftTemplatesListComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ShiftTemplatesListComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        { provide: ShiftTemplatesClient, useValue: { getShiftTemplates: vi.fn().mockReturnValue(of([])), deleteShiftTemplate: vi.fn().mockReturnValue(of({})) } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: ConfirmationService, useValue: { confirm: vi.fn() } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ShiftTemplatesListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('modeLabel zwraca etykietę trybu', () => {
    expect(component.modeLabel({ slotGenerationMode: SlotGenerationMode.FixedStartTimes } as ShiftTemplateDto)).toBe('Ustalone godziny');
    expect(component.modeLabel({ slotGenerationMode: SlotGenerationMode.Grid } as ShiftTemplateDto)).toBe('Elastyczne godziny');
  });

  it('formatDetail: tryb stały pokazuje godziny startu', () => {
    const detail = component.formatDetail({
      slotGenerationMode: SlotGenerationMode.FixedStartTimes,
      fixedStartTimes: ['12:00:00', '09:00:00'],
    } as ShiftTemplateDto);
    expect(detail).toBe('09:00, 12:00');
  });

  it('formatDetail: tryb siatki pokazuje przedziały', () => {
    const detail = component.formatDetail({
      slotGenerationMode: SlotGenerationMode.Grid,
      workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
      breaks: [],
    } as ShiftTemplateDto);
    expect(detail).toContain('09:00–17:00');
  });

  it('goToEdit nawiguje do trasy edycji szablonu', () => {
    const router = TestBed.inject(Router);
    const nav = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.goToEdit({ id: 'abc' } as ShiftTemplateDto);
    expect(nav).toHaveBeenCalledWith(['/admin/resources/shift-templates', 'abc', 'edit']);
  });
});
