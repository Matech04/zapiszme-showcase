import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { SalonSettingsClient, TenantDto } from '@core/api/api-client';
import { BookingPauseStore } from './booking-pause.store';

describe('BookingPauseStore', () => {
  let getMock: ReturnType<typeof vi.fn>;

  function configure(): BookingPauseStore {
    TestBed.configureTestingModule({
      providers: [
        BookingPauseStore,
        { provide: SalonSettingsClient, useValue: { get: getMock } },
      ],
    });
    return TestBed.inject(BookingPauseStore);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    getMock = vi.fn();
  });

  it('startuje wyłączony', () => {
    getMock.mockReturnValue(of({ bookingPaused: false } as TenantDto));
    const store = configure();
    expect(store.paused()).toBe(false);
  });

  it('refresh() ustawia stan z API', () => {
    getMock.mockReturnValue(of({ bookingPaused: true } as TenantDto));
    const store = configure();
    store.refresh();
    expect(store.paused()).toBe(true);
  });

  it('refresh() woła API tylko raz na sesję (chyba że force)', () => {
    getMock.mockReturnValue(of({ bookingPaused: false } as TenantDto));
    const store = configure();
    store.refresh();
    store.refresh();
    expect(getMock).toHaveBeenCalledTimes(1);
    store.refresh(true);
    expect(getMock).toHaveBeenCalledTimes(2);
  });

  it('set() synchronizuje stan bez wołania API', () => {
    getMock.mockReturnValue(new Subject());
    const store = configure();
    store.set(true);
    expect(store.paused()).toBe(true);
    store.set(false);
    expect(store.paused()).toBe(false);
  });

  it('błąd API nie wywraca store (zostaje wyłączony)', () => {
    getMock.mockReturnValue(throwError(() => new Error('boom')));
    const store = configure();
    store.refresh();
    expect(store.paused()).toBe(false);
  });

  it('appointmentsBlocked = true gdy salon wstrzymał rezerwacje', () => {
    getMock.mockReturnValue(of({ bookingPaused: true, platformMaintenance: false } as TenantDto));
    const store = configure();
    store.refresh();
    expect(store.appointmentsBlocked()).toBe(true);
    expect(store.platformMaintenance()).toBe(false);
  });

  it('appointmentsBlocked = true gdy trwa globalny tryb serwisowy platformy', () => {
    getMock.mockReturnValue(of({ bookingPaused: false, platformMaintenance: true } as TenantDto));
    const store = configure();
    store.refresh();
    expect(store.platformMaintenance()).toBe(true);
    expect(store.appointmentsBlocked()).toBe(true);
  });

  it('appointmentsBlocked = false gdy oba wyłączone', () => {
    getMock.mockReturnValue(of({ bookingPaused: false, platformMaintenance: false } as TenantDto));
    const store = configure();
    store.refresh();
    expect(store.appointmentsBlocked()).toBe(false);
  });

  it('ensureLoaded() pobiera stan raz i rozwiązuje się', async () => {
    getMock.mockReturnValue(of({ bookingPaused: true, platformMaintenance: false } as TenantDto));
    const store = configure();
    await store.ensureLoaded();
    expect(getMock).toHaveBeenCalledTimes(1);
    expect(store.appointmentsBlocked()).toBe(true);
    await store.ensureLoaded();
    expect(getMock).toHaveBeenCalledTimes(1);
  });
});
