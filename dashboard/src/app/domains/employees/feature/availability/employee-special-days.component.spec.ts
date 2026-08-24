import { Location } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { API_BASE_URL, EmployeesClient, ShiftTemplatesClient, SlotGenerationMode } from '@core/api/api-client';
import { ScheduleOverridesApiService } from '@core/api/schedule-overrides-api.service';
import { EmployeeSpecialDaysComponent } from './employee-special-days.component';
import { AuthSessionService } from '@core/auth/auth-session.service';

describe('EmployeeSpecialDaysComponent — tryb per-dzień', () => {
  let component: EmployeeSpecialDaysComponent;
  let fixture: ComponentFixture<EmployeeSpecialDaysComponent>;
  let setOverride: ReturnType<typeof vi.fn>;

  async function setup(employeeMode: SlotGenerationMode, templates: unknown[] = []) {
    setOverride = vi.fn().mockReturnValue(of({ appointmentsOutsideSchedule: [] }));
    await TestBed.configureTestingModule({
      imports: [EmployeeSpecialDaysComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        // Realny AuthSessionService wciąga AuthClient/DemoClient/NavigationService (NG0201).
        // Komponent używa tylko currentRole() — do rozstrzygnięcia, czy pobierać szablony zmian.
        { provide: AuthSessionService, useValue: { currentRole: () => 'owner' } },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployee: vi.fn().mockReturnValue(
              of({ id: 'e1', firstName: 'Ala', lastName: 'N', email: 'a@n.pl', slotGenerationMode: employeeMode }),
            ),
            getScheduleOverrides: vi.fn().mockReturnValue(of([])),
            getEmployeeSchedules: vi.fn().mockReturnValue(of([])),
            setScheduleOverride: setOverride,
          },
        },
        { provide: ShiftTemplatesClient, useValue: { getShiftTemplates: vi.fn().mockReturnValue(of(templates)) } },
        { provide: ScheduleOverridesApiService, useValue: { removeOverride: vi.fn().mockReturnValue(of({})) } },
        { provide: HttpClient, useValue: { get: vi.fn().mockReturnValue(of([])) } },
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: ConfirmationService, useValue: { confirm: vi.fn() } },
        { provide: MessageService, useValue: { add: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap({})),
            snapshot: { queryParamMap: convertToParamMap({}) },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeSpecialDaysComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'e1');
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('zapis w trybie stałym wysyła fixedStartTimes, puste workRanges', async () => {
    await setup(SlotGenerationMode.FixedStartTimes);

    component.setOverrideMode(SlotGenerationMode.FixedStartTimes);
    component.draftDate.set('2026-06-15');
    component.draftIsWorking.set(true);
    component.draftFixedTimes.set(['12:00', '09:00']);
    component.saveDraft();

    expect(setOverride).toHaveBeenCalledTimes(1);
    const body = setOverride.mock.calls[0][1] as {
      slotGenerationMode: number;
      fixedStartTimes: string[];
      workRanges: unknown[];
    };
    expect(body.slotGenerationMode).toBe(SlotGenerationMode.FixedStartTimes);
    expect(body.fixedStartTimes).toEqual(['09:00:00', '12:00:00']); // sort + ISO
    expect(body.workRanges).toEqual([]);
  });

  it('zapis w trybie siatki wysyła workRanges, puste fixedStartTimes', async () => {
    await setup(SlotGenerationMode.Grid);

    component.setOverrideMode(SlotGenerationMode.Grid);
    component.draftDate.set('2026-06-16');
    component.draftIsWorking.set(true);
    component.draftWorkRanges.set([{ startTime: '09:00', endTime: '17:00' }]);
    component.draftFixedTimes.set([]);
    component.saveDraft();

    expect(setOverride).toHaveBeenCalledTimes(1);
    const body = setOverride.mock.calls[0][1] as {
      slotGenerationMode: number;
      fixedStartTimes: unknown[];
      workRanges: { startTime: string; endTime: string }[];
    };
    expect(body.slotGenerationMode).toBe(SlotGenerationMode.Grid);
    expect(body.workRanges).toEqual([{ startTime: '09:00:00', endTime: '17:00:00' }]);
    expect(body.fixedStartTimes).toEqual([]);
  });

  it('isFixedMode podąża za overrideMode (per dzień), nie za trybem globalnym', async () => {
    await setup(SlotGenerationMode.Grid); // pracownik globalnie siatkowy
    component.setOverrideMode(SlotGenerationMode.FixedStartTimes);
    expect(component.isFixedMode()).toBe(true);
    component.setOverrideMode(SlotGenerationMode.Grid);
    expect(component.isFixedMode()).toBe(false);
  });

  it('loadIntoDraft wczytuje istniejący wyjątek z JEGO trybem (override stały)', async () => {
    await setup(SlotGenerationMode.Grid); // pracownik globalnie siatkowy
    component.loadIntoDraft({
      date: '2026-06-20' as unknown as Date,
      slotGenerationMode: SlotGenerationMode.FixedStartTimes,
      workRanges: [],
      breaks: [],
      fixedStartTimes: ['10:00:00', '13:00:00'],
    });
    expect(component.isFixedMode()).toBe(true);
    expect(component.draftIsWorking()).toBe(true);
    expect(component.draftFixedTimes()).toEqual(['10:00', '13:00']);
  });

  it('chipy szablonów filtrują wg trybu wyjątku', async () => {
    await setup(SlotGenerationMode.Grid, [
      { id: 'g', name: 'Grid', slotGenerationMode: SlotGenerationMode.Grid, workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }], breaks: [], fixedStartTimes: [] },
      { id: 'f', name: 'Fixed', slotGenerationMode: SlotGenerationMode.FixedStartTimes, workRanges: [], breaks: [], fixedStartTimes: ['09:00:00'] },
    ]);
    // tryb wyjątku = siatka → tylko szablon grid jako chip
    component.setOverrideMode(SlotGenerationMode.Grid);
    let names = component.templateChips().map((t) => t.name);
    expect(names).toEqual(['Grid']);
    // przełącz na stały → tylko fixed
    component.setOverrideMode(SlotGenerationMode.FixedStartTimes);
    names = component.templateChips().map((t) => t.name);
    expect(names).toEqual(['Fixed']);
  });

  it('applyTemplate podświetla zastosowany chip, zmiana trybu czyści podświetlenie', async () => {
    await setup(SlotGenerationMode.Grid, [
      { id: 'g', name: 'Grid', slotGenerationMode: SlotGenerationMode.Grid, workRanges: [{ startTime: '08:00:00', endTime: '12:00:00' }], breaks: [], fixedStartTimes: [] },
    ]);
    component.setOverrideMode(SlotGenerationMode.Grid);
    component.applyTemplate(component.templateChips()[0]);
    expect(component.appliedTemplateId()).toBe('g');
    expect(component.draftWorkRanges()).toEqual([{ startTime: '08:00', endTime: '12:00' }]);
    // zmiana trybu = inny zestaw chipów → podświetlenie znika
    component.setOverrideMode(SlotGenerationMode.FixedStartTimes);
    expect(component.appliedTemplateId()).toBeNull();
  });
});
