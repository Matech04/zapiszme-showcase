import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { describe, it, expect, beforeEach } from 'vitest';
import {
  CalendarFiltersComponent,
  CalendarFiltersValue,
} from './calendar-filters.component';

const emptyValue: CalendarFiltersValue = {
  employeeIds: [],
  statuses: [],
  searchQuery: '',
};

describe('CalendarFiltersComponent', () => {
  let fixture: ComponentFixture<CalendarFiltersComponent>;
  let component: CalendarFiltersComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CalendarFiltersComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(CalendarFiltersComponent);
    component = fixture.componentInstance;
  });

  function setInputs(partial: {
    viewMode?: 'day' | 'week' | 'month';
    value?: CalendarFiltersValue;
    employees?: { id: string; label: string }[];
    isDesktop?: boolean;
    showEmployeeFilter?: boolean;
  }): void {
    fixture.componentRef.setInput('viewMode', partial.viewMode ?? 'day');
    fixture.componentRef.setInput('value', partial.value ?? emptyValue);
    fixture.componentRef.setInput('employees', partial.employees ?? []);
    if ('isDesktop' in partial) {
      fixture.componentRef.setInput('isDesktop', !!partial.isDesktop);
    }
    if ('showEmployeeFilter' in partial) {
      fixture.componentRef.setInput('showEmployeeFilter', !!partial.showEmployeeFilter);
    }
    fixture.detectChanges();
  }

  it('hasActiveFilters jest false dla pustego value', () => {
    setInputs({});
    expect(component.hasActiveFilters()).toBe(false);
  });

  it('hasActiveFilters jest true gdy wybrano statusy', () => {
    setInputs({ value: { ...emptyValue, statuses: ['pending'] } });
    expect(component.hasActiveFilters()).toBe(true);
  });

  it('activeCount zlicza grupy filtrów (employee + status + query)', () => {
    setInputs({
      value: {
        employeeIds: ['e1'],
        statuses: ['pending', 'booked'],
        searchQuery: 'Anna',
      },
    });
    expect(
      (component as unknown as { activeCount: () => number }).activeCount(),
    ).toBe(3);
  });

  it('activeCount jest 0 gdy searchQuery to whitespace', () => {
    setInputs({ value: { ...emptyValue, searchQuery: '   ' } });
    expect(
      (component as unknown as { activeCount: () => number }).activeCount(),
    ).toBe(0);
  });

  it('toggleStatus dodaje status jeśli go nie ma', () => {
    let emitted: CalendarFiltersValue | null = null;
    component.valueChange.subscribe((v) => (emitted = v));
    setInputs({});
    component.toggleStatus('pending');
    expect(emitted!.statuses).toEqual(['pending']);
  });

  it('toggleStatus usuwa status jeśli już zaznaczony', () => {
    let emitted: CalendarFiltersValue | null = null;
    component.valueChange.subscribe((v) => (emitted = v));
    setInputs({ value: { ...emptyValue, statuses: ['pending', 'booked'] } });
    component.toggleStatus('pending');
    expect(emitted!.statuses).toEqual(['booked']);
  });

  it('clearAll resetuje wszystkie pola', () => {
    let emitted: CalendarFiltersValue | null = null;
    component.valueChange.subscribe((v) => (emitted = v));
    setInputs({
      value: {
        employeeIds: ['e1'],
        statuses: ['pending'],
        searchQuery: 'x',
      },
    });
    component.clearAll();
    expect(emitted).toEqual(emptyValue);
  });

  it('sheet domyślnie zamknięty; openSheet/closeSheet przełączają stan', () => {
    setInputs({ isDesktop: false });
    const sheetOpen = (component as unknown as { sheetOpen: () => boolean }).sheetOpen;
    expect(sheetOpen()).toBe(false);
    (component as unknown as { openSheet: () => void }).openSheet();
    expect(sheetOpen()).toBe(true);
    (component as unknown as { closeSheet: () => void }).closeSheet();
    expect(sheetOpen()).toBe(false);
  });

  it('przełączenie na desktop zamyka otwarty sheet (auto-cleanup po resize)', () => {
    setInputs({ isDesktop: false });
    (component as unknown as { openSheet: () => void }).openSheet();
    expect(
      (component as unknown as { sheetOpen: () => boolean }).sheetOpen(),
    ).toBe(true);
    setInputs({ isDesktop: true });
    expect(
      (component as unknown as { sheetOpen: () => boolean }).sheetOpen(),
    ).toBe(false);
  });

  describe('wyróżnienie aktywnego trybu kalendarza', () => {
    const classesFor = (mode: 'day' | 'week' | 'month') =>
      (component as unknown as { viewButtonClasses(id: string): string }).viewButtonClasses(mode);

    it('aktywny tryb dostaje akcent marki (primary), nie samo białe tło', () => {
      setInputs({ isDesktop: true, viewMode: 'week' });
      expect(classesFor('week')).toContain('bg-primary');
      expect(classesFor('week')).toContain('text-primary-contrast');
    });

    it('nieaktywne tryby nie mają akcentu', () => {
      setInputs({ isDesktop: true, viewMode: 'week' });
      expect(classesFor('day')).not.toContain('bg-primary');
      expect(classesFor('month')).not.toContain('bg-primary');
    });

    it('aktywny przycisk niesie aria-pressed=true', () => {
      setInputs({ isDesktop: true, viewMode: 'month' });
      fixture.detectChanges();
      const pressed = fixture.debugElement
        .queryAll(By.css('button[aria-pressed]'))
        .filter((b) => (b.nativeElement as HTMLElement).getAttribute('aria-pressed') === 'true');
      expect(pressed.length).toBeGreaterThan(0);
      expect((pressed[0].nativeElement as HTMLElement).textContent?.trim().toUpperCase()).toContain('MIESI');
    });
  });
});
