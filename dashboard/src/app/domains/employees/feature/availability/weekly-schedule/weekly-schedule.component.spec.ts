import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { MessageService } from 'primeng/api';
import { Menu } from 'primeng/menu';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { EmployeesClient, ShiftTemplatesClient, SlotGenerationMode } from '@core/api/api-client';
import { WeeklyScheduleComponent } from './weekly-schedule.component';
import { AuthSessionService } from '@core/auth/auth-session.service';

function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

describe('WeeklyScheduleComponent', () => {
  let component: WeeklyScheduleComponent;
  let fixture: ComponentFixture<WeeklyScheduleComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WeeklyScheduleComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        // Realny AuthSessionService wciąga AuthClient/DemoClient/NavigationService (NG0201).
        // Komponent używa tylko currentRole() — do rozstrzygnięcia, czy pobierać szablony zmian.
        { provide: AuthSessionService, useValue: { currentRole: () => 'owner' } },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployee: vi
              .fn()
              .mockReturnValue(
                of({ id: 'e1', firstName: 'Jan', lastName: 'Kowalski', email: 'jan@test.pl' }),
              ),
            getEmployeeSchedules: vi.fn().mockReturnValue(of([])),
            setEmployeeSchedule: vi.fn().mockReturnValue(of({})),
          },
        },
        {
          provide: ShiftTemplatesClient,
          useValue: {
            getShiftTemplates: vi.fn().mockReturnValue(of([])),
          },
        },
        { provide: MessageService, useValue: { add: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(WeeklyScheduleComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'e1');
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('owner pobiera szablony zmian', () => {
    const templates = TestBed.inject(ShiftTemplatesClient);
    expect(templates.getShiftTemplates).toHaveBeenCalled();
  });

  it('nowy grafik: data startowa = dziś', () => {
    expect(component.activeFrom()).toBe(todayIso());
  });

  it('włączenie daty końcowej ustawia koniec bieżącego roku', () => {
    component.onIndefiniteToggle(false);
    expect(component.activeTo()).toBe(`${new Date().getFullYear()}-12-31`);
  });

  it('zapis w trybie stałych godzin wysyła fixedStartTimes', () => {
    const client = TestBed.inject(EmployeesClient);
    component.onChangeSlotMode(SlotGenerationMode.FixedStartTimes);
    component.daysFlat.set([
      {
        dayKey: 'Monday',
        dayName: 'Poniedziałek',
        weekIndex: 0,
        isWorking: true,
        workRanges: [],
        breaks: [],
        fixedStartTimes: ['09:00'],
      },
    ]);

    component.updateWeeklySchedule();

    const setSpy = client.setEmployeeSchedule as unknown as { mock: { calls: unknown[][] } };
    expect(setSpy.mock.calls.length).toBe(1);
    const dto = setSpy.mock.calls[0][1] as { slotGenerationMode: number; days: { fixedStartTimes?: string[] }[] };
    expect(dto.slotGenerationMode).toBe(SlotGenerationMode.FixedStartTimes);
    expect(dto.days[0].fixedStartTimes).toEqual(['09:00:00']);
  });

  it('hasUnsavedChanges: false na starcie, true po edycji, false po zapisie', () => {
    expect(component.hasUnsavedChanges()).toBe(false);
    component.updateDay({
      dayKey: 'Monday', dayName: 'Poniedziałek', weekIndex: 0, isWorking: true,
      workRanges: [{ startTime: '09:00', endTime: '17:00' }], breaks: [], fixedStartTimes: [],
    });
    expect(component.hasUnsavedChanges()).toBe(true);
    component.updateWeeklySchedule();
    expect(component.hasUnsavedChanges()).toBe(false);
  });

  it('onActiveFromChange: no-op (ta sama wartość, jak spurious emit date-pickera) nie ustawia dirty', () => {
    expect(component.hasUnsavedChanges()).toBe(false);
    component.onActiveFromChange(component.activeFrom());
    expect(component.hasUnsavedChanges()).toBe(false);
    component.onActiveFromChange('2030-01-01');
    expect(component.hasUnsavedChanges()).toBe(true);
  });

  it('copyDayToOthers: kopiuje do innych dni roboczych w tygodniu, pomija wolne i dzień źródłowy', () => {
    component.daysFlat.set([
      { dayKey: 'Monday', dayName: 'Pn', weekIndex: 0, isWorking: true, workRanges: [{ startTime: '08:00', endTime: '12:00' }], breaks: [], fixedStartTimes: [] },
      { dayKey: 'Tuesday', dayName: 'Wt', weekIndex: 0, isWorking: true, workRanges: [{ startTime: '09:00', endTime: '17:00' }], breaks: [], fixedStartTimes: [] },
      { dayKey: 'Sunday', dayName: 'Nd', weekIndex: 0, isWorking: false, workRanges: [], breaks: [], fixedStartTimes: [] },
    ]);
    const monday = component.daysFlat().find((d) => d.dayKey === 'Monday')!;

    component.copyDayToOthers(monday);

    const days = component.daysFlat();
    expect(days.find((d) => d.dayKey === 'Tuesday')!.workRanges).toEqual([{ startTime: '08:00', endTime: '12:00' }]);
    expect(days.find((d) => d.dayKey === 'Sunday')!.workRanges).toEqual([]);
    expect(days.find((d) => d.dayKey === 'Monday')!.workRanges).toEqual([{ startTime: '08:00', endTime: '12:00' }]);
    expect(component.hasUnsavedChanges()).toBe(true);
  });
});

describe('WeeklyScheduleComponent — szablony (filtr + zastosuj do wszystkich)', () => {
  let component: WeeklyScheduleComponent;
  let fixture: ComponentFixture<WeeklyScheduleComponent>;

  const gridTpl = {
    id: 'g', name: 'Grid', slotGenerationMode: SlotGenerationMode.Grid,
    workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }], breaks: [], fixedStartTimes: [],
  };
  const fixedTpl = {
    id: 'f', name: 'Fixed', slotGenerationMode: SlotGenerationMode.FixedStartTimes,
    workRanges: [], breaks: [], fixedStartTimes: ['09:00:00', '12:00:00'],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WeeklyScheduleComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        // Realny AuthSessionService wciąga AuthClient/DemoClient/NavigationService (NG0201).
        // Komponent używa tylko currentRole() — do rozstrzygnięcia, czy pobierać szablony zmian.
        { provide: AuthSessionService, useValue: { currentRole: () => 'owner' } },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployee: vi.fn().mockReturnValue(of({ id: 'e1', firstName: 'Jan', lastName: 'K', email: 'j@k.pl' })),
            getEmployeeSchedules: vi.fn().mockReturnValue(of([])),
            setEmployeeSchedule: vi.fn().mockReturnValue(of({})),
          },
        },
        { provide: ShiftTemplatesClient, useValue: { getShiftTemplates: vi.fn().mockReturnValue(of([gridTpl, fixedTpl])) } },
        { provide: MessageService, useValue: { add: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(WeeklyScheduleComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'e1');
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('templatesForMode filtruje szablony do trybu grafiku', () => {
    component.onChangeSlotMode(SlotGenerationMode.FixedStartTimes);
    expect(component.templatesForMode().map((t) => t.id)).toEqual(['f']);
    component.onChangeSlotMode(SlotGenerationMode.Grid);
    expect(component.templatesForMode().map((t) => t.id)).toEqual(['g']);
  });

  it('menu „Zastosuj do wszystkich dni roboczych" ma dedykowaną, szerszą klasę overlayu', () => {
    // Tryb Grid ma pasujący szablon → karta z menu się renderuje.
    component.onChangeSlotMode(SlotGenerationMode.Grid);
    fixture.detectChanges();
    const menu = fixture.debugElement.query(By.directive(Menu)).componentInstance as Menu;
    expect(menu.styleClass).toContain('shift-template-menu');
  });

  it('applyTemplateToAll ustawia godziny na wszystkich dniach roboczych (tryb stały)', () => {
    component.onChangeSlotMode(SlotGenerationMode.FixedStartTimes);
    component.daysFlat.set([
      { dayKey: 'Monday', dayName: 'Pn', weekIndex: 0, isWorking: true, workRanges: [], breaks: [], fixedStartTimes: [] },
      { dayKey: 'Sunday', dayName: 'Nd', weekIndex: 0, isWorking: false, workRanges: [], breaks: [], fixedStartTimes: [] },
    ]);

    component.applyTemplateToAll(fixedTpl);

    const days = component.daysFlat();
    expect(days.find((d) => d.dayKey === 'Monday')!.fixedStartTimes).toEqual(['09:00', '12:00']);
    // dzień wolny nietknięty
    expect(days.find((d) => d.dayKey === 'Sunday')!.fixedStartTimes).toEqual([]);
  });
});

describe('WeeklyScheduleComponent — rola Employee', () => {
  it('pracownik nie pobiera szablonów zmian (API = StaffManagement) i nie widzi menu „zastosuj"', async () => {
    const getShiftTemplates = vi.fn().mockReturnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [WeeklyScheduleComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        { provide: AuthSessionService, useValue: { currentRole: () => 'employee' } },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployee: vi
              .fn()
              .mockReturnValue(of({ id: 'e1', firstName: 'Jan', lastName: 'K', email: 'j@k.pl' })),
            getEmployeeSchedules: vi.fn().mockReturnValue(of([])),
            setEmployeeSchedule: vi.fn().mockReturnValue(of({})),
          },
        },
        { provide: ShiftTemplatesClient, useValue: { getShiftTemplates } },
        { provide: MessageService, useValue: { add: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(WeeklyScheduleComponent);
    fixture.componentRef.setInput('id', 'e1');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(getShiftTemplates).not.toHaveBeenCalled();
    expect(fixture.componentInstance.templatesForMode()).toEqual([]);
    expect(fixture.debugElement.query(By.css('[data-testid="apply-template-all"]'))).toBeNull();
  });
});
