import { describe, it, expect, beforeEach, vi } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { API_BASE_URL } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { RealtimeNotificationsService } from '@core/services/realtime-notifications.service';
import {
  appointmentNotificationTarget,
  NotificationCenterService,
  type AppNotification,
} from './notification-center.service';

/**
 * Regresja: powiadomienia o wizycie MUSZĄ kierować do drawera w kalendarzu
 * (`/admin/schedule?appointment=<id>`), a NIE do usuniętej strony `/admin/appointment/:id`.
 * Wcześniej klik w powiadomienie lądował na pulpicie, bo trasa już nie istniała.
 */
describe('appointmentNotificationTarget', () => {
  it('buduje deep-link do kalendarza z query param appointment', () => {
    const target = appointmentNotificationTarget('apt-123');
    expect(target.routerLink).toEqual(['/admin/schedule']);
    expect(target.queryParams).toEqual({ appointment: 'apt-123' });
  });

  it('NIE używa starej trasy /admin/appointment/:id', () => {
    const target = appointmentNotificationTarget('apt-123');
    expect(target.routerLink).not.toContain('/admin/appointment');
    expect(JSON.stringify(target.routerLink)).not.toContain('appointment/');
  });

  it.each([null, undefined, ''])('bez id (%s) zwraca pusty cel (brak nawigacji)', (id) => {
    const target = appointmentNotificationTarget(id);
    expect(target.routerLink).toBeUndefined();
    expect(target.queryParams).toBeUndefined();
  });

  it('reveal:false → reveal=0 i ZERO danych o dacie (kalendarz ma stać w miejscu)', () => {
    // Wizyta do potwierdzenia: personel chce ją potwierdzić, nie zmieniać oglądanego dnia.
    const target = appointmentNotificationTarget('apt-1', { reveal: false, date: '2026-08-14' });
    expect(target.queryParams).toEqual({ appointment: 'apt-1', reveal: '0' });
    expect(target.queryParams).not.toHaveProperty('date');
    expect(target.queryParams).not.toHaveProperty('view');
  });

  it('reveal domyślny + date → dzień i widok w linku (kalendarz nie renderuje klatki „dziś")', () => {
    const target = appointmentNotificationTarget('apt-2', { date: '2026-08-14' });
    expect(target.queryParams).toEqual({
      appointment: 'apt-2',
      date: '2026-08-14',
      view: 'day',
    });
  });

  it('employeeId trafia do routerLink — omija redirect /admin/schedule → /admin/schedule/:id', () => {
    const target = appointmentNotificationTarget('apt-3', {
      date: '2026-08-14',
      employeeId: 'emp-9',
    });
    expect(target.routerLink).toEqual(['/admin/schedule', 'emp-9']);
  });

  it('bez date nie dokleja śmieciowych paramów (DTO anulowania nie niesie pracownika)', () => {
    const target = appointmentNotificationTarget('apt-4');
    expect(target.routerLink).toEqual(['/admin/schedule']);
    expect(target.queryParams).toEqual({ appointment: 'apt-4' });
  });
});

/**
 * Badge „do potwierdzenia" na kalendarzu liczy wyłącznie wizyty PRZYPISANE do zalogowanego
 * pracownika — ten sam model, co osobisty dzwonek na backendzie. Wyjątek: konto „Recepcja"
 * (kiosk) nie ma własnego kalendarza i liczy wizyty całego salonu.
 */
describe('NotificationCenterService — zakres badge „do potwierdzenia"', () => {
  let http: { get: ReturnType<typeof vi.fn> };
  let auth: {
    isHydrated: ReturnType<typeof vi.fn>;
    currentRole: ReturnType<typeof vi.fn>;
    currentEmployeeId: ReturnType<typeof vi.fn>;
    isImpersonating: ReturnType<typeof vi.fn>;
  };

  const pollPending = (service: NotificationCenterService) =>
    (service as unknown as { pollPending(): void }).pollPending();

  const paramsOfLastGet = () => http.get.mock.calls.at(-1)?.[1]?.params as Record<string, string>;

  beforeEach(() => {
    http = { get: vi.fn().mockReturnValue(of([])) };
    auth = {
      isHydrated: vi.fn().mockReturnValue(true),
      currentRole: vi.fn().mockReturnValue('employee'),
      currentEmployeeId: vi.fn().mockReturnValue('emp-1'),
      isImpersonating: vi.fn().mockReturnValue(false),
    };

    TestBed.configureTestingModule({
      providers: [
        NotificationCenterService,
        { provide: HttpClient, useValue: http },
        { provide: API_BASE_URL, useValue: 'http://localhost' },
        { provide: AuthSessionService, useValue: auth },
        { provide: RealtimeNotificationsService, useValue: { start: vi.fn(), stop: vi.fn(), persisted: () => [], markRead: vi.fn() } },
      ],
    });
  });

  it('pracownik: pyta tylko o własne wizyty', () => {
    pollPending(TestBed.inject(NotificationCenterService));
    expect(paramsOfLastGet()['employeeId']).toBe('emp-1');
  });

  it('właścicielka też widzi tylko swoje (dzwonek jest osobisty)', () => {
    auth.currentRole.mockReturnValue('owner');
    auth.currentEmployeeId.mockReturnValue('owner-emp');
    pollPending(TestBed.inject(NotificationCenterService));
    expect(paramsOfLastGet()['employeeId']).toBe('owner-emp');
  });

  it('kiosk: cały salon (bez employeeId)', () => {
    auth.currentRole.mockReturnValue('kiosk');
    auth.currentEmployeeId.mockReturnValue('kiosk-emp');
    pollPending(TestBed.inject(NotificationCenterService));
    expect(paramsOfLastGet()['employeeId']).toBeUndefined();
  });

  it('tryb wsparcia (Admin w impersonacji): cały salon (bez employeeId)', () => {
    // Admin nie ma rekordu Employee — dzwonek osobisty byłby pusty; w trybie wsparcia widzi całość.
    auth.currentRole.mockReturnValue('owner'); // me zwraca „Owner" w trybie wsparcia
    auth.currentEmployeeId.mockReturnValue(null);
    auth.isImpersonating.mockReturnValue(true);
    pollPending(TestBed.inject(NotificationCenterService));
    expect(paramsOfLastGet()['employeeId']).toBeUndefined();
  });

  it('przed hydratacją sesji nie pyta wcale (fail-closed, nie liczy cudzych wizyt)', () => {
    auth.isHydrated.mockReturnValue(false);
    pollPending(TestBed.inject(NotificationCenterService));
    expect(http.get).not.toHaveBeenCalled();
  });

  it('konto bez powiązanego pracownika nie pyta wcale', () => {
    auth.currentEmployeeId.mockReturnValue(null);
    pollPending(TestBed.inject(NotificationCenterService));
    expect(http.get).not.toHaveBeenCalled();
  });
});

/**
 * Deduplikacja: to samo zdarzenie (anulowanie/przełożenie przez klienta, oczekująca wizyta)
 * trafia do dzwonka DWOMA kanałami naraz — trwałym powiadomieniem in-app (sekcja „Ostatnie
 * zdarzenia") ORAZ żywym pollerem stanu wizyty (sekcja „Wymaga uwagi"). Kopia z pollera wygrywa;
 * trwały log tej samej wizyty+kategorii nie może się pokazać drugi raz.
 */
describe('NotificationCenterService — sklejanie kopii tego samego zdarzenia', () => {
  let http: { get: ReturnType<typeof vi.fn> };
  let persisted: ReturnType<typeof signal<AppNotification[]>>;

  const build = () => {
    persisted = signal<AppNotification[]>([]);
    TestBed.configureTestingModule({
      providers: [
        NotificationCenterService,
        { provide: HttpClient, useValue: http },
        { provide: API_BASE_URL, useValue: 'http://localhost' },
        {
          provide: AuthSessionService,
          useValue: {
            isHydrated: () => true,
            currentRole: () => 'employee',
            currentEmployeeId: () => 'emp-1',
          },
        },
        {
          provide: RealtimeNotificationsService,
          useValue: { start: vi.fn(), stop: vi.fn(), persisted, markRead: vi.fn() },
        },
      ],
    });
    return TestBed.inject(NotificationCenterService);
  };

  const pollCustomerChanges = (s: NotificationCenterService) =>
    (s as unknown as { pollCustomerChanges(): void }).pollCustomerChanges();

  const persistedCancel = (referenceId: string): AppNotification => ({
    id: `persisted:n-${referenceId}`,
    kind: 'system',
    category: 'cancelled',
    referenceId,
    title: 'Wizyta anulowana',
    description: '',
    occurredAt: new Date(),
    read: false,
  });

  beforeEach(() => {
    // Poller customer-changes: jedna wizyta anulowana przez klienta (changeKind === 1).
    http = {
      get: vi.fn((url: string) =>
        String(url).includes('customer-changes')
          ? of([{ appointmentId: 'apt-1', changeKind: 1, date: '2026-07-20', startTime: '10:00' }])
          : of([]),
      ),
    };
  });

  it('ta sama wizyta+kategoria: żywy poller wygrywa, trwały log znika', () => {
    const s = build();
    persisted.set([persistedCancel('apt-1')]);
    pollCustomerChanges(s);

    expect(s.actionItems().map((n) => n.referenceId)).toEqual(['apt-1']);
    expect(s.activityItems()).toHaveLength(0); // trwała kopia odfiltrowana
    // Pełna lista bez duplikatu wizyty apt-1.
    expect(s.items().filter((n) => n.referenceId === 'apt-1')).toHaveLength(1);
  });

  it('inna wizyta: trwały log zostaje (nie ma czego sklejać)', () => {
    const s = build();
    persisted.set([persistedCancel('apt-INNA')]);
    pollCustomerChanges(s);

    expect(s.activityItems().map((n) => n.referenceId)).toEqual(['apt-INNA']);
    expect(s.items().filter((n) => n.referenceId === 'apt-INNA')).toHaveLength(1);
  });

  it('ta sama wizyta, inna kategoria: NIE sklejamy (to różne zdarzenia)', () => {
    const s = build();
    // Trwały wpis „przełożono" dla apt-1; poller daje „anulowano" dla apt-1 — różne kategorie.
    persisted.set([{ ...persistedCancel('apt-1'), category: 'rescheduled', title: 'Przełożono' }]);
    pollCustomerChanges(s);

    expect(s.activityItems()).toHaveLength(1);
    expect(s.items().filter((n) => n.referenceId === 'apt-1')).toHaveLength(2);
  });
});
