import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { CalendarStateService } from './calendar-state.service';

const STORAGE_KEY = 'zapisz.calendar.prefs.v1';

function makeService(queryParams: Record<string, string> = {}): CalendarStateService {
  const routerMock = { navigate: vi.fn().mockResolvedValue(true) };
  TestBed.configureTestingModule({
    providers: [
      CalendarStateService,
      { provide: Router, useValue: routerMock },
      {
        provide: ActivatedRoute,
        useValue: {
          queryParams: of(queryParams),
          snapshot: { queryParams, queryParamMap: convertToParamMap(queryParams) },
        },
      },
    ],
  });
  return TestBed.inject(CalendarStateService);
}

describe('CalendarStateService (F3.5: localStorage persistence)', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
    globalThis.localStorage?.clear();
  });

  it('domyślnie view=day przy pustym storage i pustym URL', () => {
    const svc = makeService();
    expect(svc.view()).toBe('day');
    expect(svc.statuses()).toEqual([]);
  });

  it('zapis do localStorage po zmianie view', () => {
    const svc = makeService();
    svc.setView('week');
    TestBed.tick();
    const raw = globalThis.localStorage?.getItem(STORAGE_KEY);
    expect(raw).toBeTruthy();
    const parsed = JSON.parse(raw!);
    expect(parsed.view).toBe('week');
  });

  it('odczyt z localStorage przy starcie', () => {
    globalThis.localStorage?.setItem(
      STORAGE_KEY,
      JSON.stringify({ view: 'month', statuses: ['pending'] }),
    );
    const svc = makeService();
    expect(svc.view()).toBe('month');
    expect(svc.statuses()).toEqual(['pending']);
  });

  it('URL nadpisuje wartość ze storage', () => {
    globalThis.localStorage?.setItem(STORAGE_KEY, JSON.stringify({ view: 'month' }));
    const svc = makeService({ view: 'week' });
    expect(svc.view()).toBe('week');
  });

  it('ignoruje niepoprawne wartości w storage', () => {
    globalThis.localStorage?.setItem(STORAGE_KEY, JSON.stringify({ view: 'invalid' }));
    const svc = makeService();
    expect(svc.view()).toBe('day');
  });

  it('zapis statuses', () => {
    const svc = makeService();
    svc.setStatuses(['pending', 'booked']);
    TestBed.tick();
    const parsed = JSON.parse(globalThis.localStorage?.getItem(STORAGE_KEY) ?? '{}');
    expect(parsed.statuses).toEqual(['booked', 'pending']);
  });

  it('uszkodzony JSON w storage nie psuje startu', () => {
    globalThis.localStorage?.setItem(STORAGE_KEY, '{ invalid');
    const svc = makeService();
    expect(svc.view()).toBe('day');
  });
});
