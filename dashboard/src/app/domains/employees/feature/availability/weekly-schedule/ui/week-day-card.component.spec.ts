import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Menu } from 'primeng/menu';
import { ShiftTemplateDto, SlotGenerationMode } from '@core/api/api-client';
import { WeekDayCardComponent } from './week-day-card.component';
import type { DayScheduleUi } from '../weekly-schedule.component';

function day(partial: Partial<DayScheduleUi> = {}): DayScheduleUi {
  return {
    dayKey: 'Monday',
    dayName: 'Poniedziałek',
    weekIndex: 0,
    isWorking: true,
    workRanges: [],
    breaks: [],
    fixedStartTimes: [],
    ...partial,
  };
}

describe('WeekDayCardComponent — applyTemplate (Użyj szablonu)', () => {
  let fixture: ComponentFixture<WeekDayCardComponent>;
  let component: WeekDayCardComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [WeekDayCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(WeekDayCardComponent);
    component = fixture.componentInstance;
  });

  function setInputs(mode: SlotGenerationMode, templates: ShiftTemplateDto[], d = day()) {
    fixture.componentRef.setInput('day', d);
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('templates', templates);
    fixture.detectChanges();
  }

  it('tryb stały: wstawienie szablonu ustawia fixedStartTimes (HH:mm)', () => {
    let emitted: DayScheduleUi | undefined;
    component.changedDay.subscribe((d) => (emitted = d));

    setInputs(SlotGenerationMode.FixedStartTimes, [
      { id: 't1', name: 'Stałe', slotGenerationMode: SlotGenerationMode.FixedStartTimes,
        workRanges: [], breaks: [], fixedStartTimes: ['09:00:00', '12:00:00'] },
    ]);

    const items = component.templateMenuItems();
    expect(items.length).toBe(1);
    items[0].command!({} as never);

    expect(emitted).toBeDefined();
    expect(emitted!.isWorking).toBe(true);
    expect(emitted!.fixedStartTimes).toEqual(['09:00', '12:00']);
  });

  it('tryb siatki: wstawienie szablonu ustawia workRanges + breaks (HH:mm)', () => {
    let emitted: DayScheduleUi | undefined;
    component.changedDay.subscribe((d) => (emitted = d));

    setInputs(SlotGenerationMode.Grid, [
      { id: 't2', name: 'Pełny', slotGenerationMode: SlotGenerationMode.Grid,
        workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
        breaks: [{ startTime: '12:00:00', endTime: '12:30:00' }], fixedStartTimes: [] },
    ]);

    component.templateMenuItems()[0].command!({} as never);

    expect(emitted!.workRanges).toEqual([{ startTime: '09:00', endTime: '17:00' }]);
    expect(emitted!.breaks).toEqual([{ startTime: '12:00', endTime: '12:30' }]);
  });

  it('brak szablonów → puste menu', () => {
    setInputs(SlotGenerationMode.Grid, []);
    expect(component.templateMenuItems()).toEqual([]);
  });

  it('overlay menu szablonów ma dedykowaną klasę szerokości (shift-template-menu)', () => {
    setInputs(SlotGenerationMode.Grid, [
      {
        id: 't1',
        name: 'Pełny',
        slotGenerationMode: SlotGenerationMode.Grid,
        workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
        breaks: [],
        fixedStartTimes: [],
      },
    ]);

    // Overlay popupu jest dołączany do body asynchronicznie; sprawdzamy deterministycznie
    // binding styleClass na instancji p-menu (= dedykowana, szersza klasa overlayu).
    const menu = fixture.debugElement.query(By.directive(Menu)).componentInstance as Menu;
    expect(menu.styleClass).toContain('shift-template-menu');
  });

  it('canCopy=true gdy dzień ma godziny; dayMenuItems emituje copyToOthers z bieżącym dniem', () => {
    let emitted: DayScheduleUi | undefined;
    component.copyToOthers.subscribe((d) => (emitted = d));

    setInputs(SlotGenerationMode.Grid, [], day({ workRanges: [{ startTime: '09:00', endTime: '17:00' }] }));

    expect(component.canCopy()).toBe(true);
    const items = component.dayMenuItems();
    expect(items.length).toBe(1);
    items[0].command!({} as never);

    expect(emitted).toBeDefined();
    expect(emitted!.dayKey).toBe('Monday');
  });

  it('canCopy=false gdy dzień nie ma jeszcze godzin', () => {
    setInputs(SlotGenerationMode.Grid, [], day({ workRanges: [] }));
    expect(component.canCopy()).toBe(false);
  });
});
