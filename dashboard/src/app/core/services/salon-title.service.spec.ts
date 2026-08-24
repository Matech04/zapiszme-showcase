import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SalonSettingsClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { SalonTitleService } from './salon-title.service';

/**
 * Tytuł strony ciągnie nazwę salonu z `GET /api/SalonSettings`. Pułapka: właściciel loguje się
 * do kreatora ZANIM salon istnieje — wtedy endpoint zwraca 400 `tenant.missing`, a errorInterceptor
 * (wyżej w łańcuchu niż lokalny catchError) wypluwa toast „Nie udało się ustalić kontekstu salonu".
 */
describe('SalonTitleService', () => {
  const setup = (tenantId: string | null) => {
    const session = signal(tenantId ? { userId: 'u1', tenantId } : null);
    const get = vi.fn(() => of({ name: 'Salon Zofia' }));
    const setTitle = vi.fn();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: { session } },
        { provide: SalonSettingsClient, useValue: { get } },
        { provide: Title, useValue: { setTitle } },
      ],
    });

    TestBed.inject(SalonTitleService);
    TestBed.tick();
    return { session, get, setTitle };
  };

  beforeEach(() => TestBed.resetTestingModule());

  it('nie odpytuje o salon, gdy sesja nie ma tenanta (kreator / SystemAdmin)', () => {
    const { get, setTitle } = setup(null);

    expect(get).not.toHaveBeenCalled();
    expect(setTitle).toHaveBeenCalledWith('zapisz.me');
  });

  it('dokleja nazwę salonu, gdy tenant jest znany', () => {
    const { get, setTitle } = setup('11111111-1111-1111-1111-111111111111');

    expect(get).toHaveBeenCalledTimes(1);
    expect(setTitle).toHaveBeenCalledWith('zapisz.me – Salon Zofia');
  });

  it('odświeża tytuł, gdy tenant pojawia się po ukończeniu kreatora', () => {
    const { session, get, setTitle } = setup(null);

    session.set({ userId: 'u1', tenantId: '22222222-2222-2222-2222-222222222222' });
    TestBed.tick();

    expect(get).toHaveBeenCalledTimes(1);
    expect(setTitle).toHaveBeenLastCalledWith('zapisz.me – Salon Zofia');
  });

  it('nie wysadza tytułu, gdy zapytanie o salon padnie', () => {
    TestBed.resetTestingModule();
    const session = signal({ userId: 'u1', tenantId: '33333333-3333-3333-3333-333333333333' });
    const setTitle = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: { session } },
        { provide: SalonSettingsClient, useValue: { get: () => throwError(() => new Error('boom')) } },
        { provide: Title, useValue: { setTitle } },
      ],
    });

    TestBed.inject(SalonTitleService);
    TestBed.tick();

    expect(setTitle).toHaveBeenCalledWith('zapisz.me');
  });
});
