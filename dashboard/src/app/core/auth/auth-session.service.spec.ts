import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defer, firstValueFrom, Observable, of, throwError } from 'rxjs';
import { AuthClient, AuthSessionDto, DemoClient } from '@core/api/api-client';
import { NavigationService } from '@core/services/NavigationService';
import { MessageService } from 'primeng/api';
import { CsrfTokenService } from './csrf-token.service';
import { AuthSessionService } from './auth-session.service';
import { SessionExpiryService } from './session-expiry.service';

/**
 * Regresja produkcyjna: „aplikacja wylogowuje mnie cały czas mimo zapamiętaj mnie" (PWA).
 *
 * `hydrate()` łapało KAŻDY błąd `/api/auth/me` i kasowało sesję, więc chwilowy brak sieci przy zimnym
 * starcie PWA (service worker serwuje shell szybciej, niż telefon wybudzi radio) wyglądał jak
 * wylogowanie — mimo ważnego 30-dniowego cookie. Te testy pilnują, że TYLKO 401 kasuje sesję.
 */
describe('AuthSessionService.hydrate', () => {
  const session = { userId: 'u1', roles: ['Owner'] } as AuthSessionDto;

  /** Klient NSwag zamienia każdy błąd na `ApiException` z polem `status` (transport → `status: 0`). */
  const apiError = (status: number) => ({ status, message: `HTTP ${status}` });

  let me: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;
  let addToast: ReturnType<typeof vi.fn>;
  let invalidateCsrf: ReturnType<typeof vi.fn>;
  let sessionExpiry: SessionExpiryService;

  const setup = () => {
    me = vi.fn();
    navigate = vi.fn();
    addToast = vi.fn();
    invalidateCsrf = vi.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        AuthSessionService,
        SessionExpiryService,
        { provide: AuthClient, useValue: { me } },
        { provide: DemoClient, useValue: {} },
        { provide: CsrfTokenService, useValue: { refresh: () => of('t'), invalidate: invalidateCsrf } },
        { provide: Router, useValue: { navigate } },
        { provide: MessageService, useValue: { add: addToast } },
        { provide: NavigationService, useValue: { syncRoleFromSession: vi.fn(), setUserRole: vi.fn() } },
      ],
    });
    sessionExpiry = TestBed.inject(SessionExpiryService);
    return TestBed.inject(AuthSessionService);
  };

  /** Podmienia `navigator.onLine` na czas jednego testu (jsdom domyślnie raportuje `true`). */
  const setOnLine = (value: boolean) => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(value);
  };

  /** Rozwiązuje strumień, przewijając w międzyczasie zegary (ponowienia używają `timer`). */
  const settle = async <T>(source: Observable<T>): Promise<T> => {
    const promise = firstValueFrom(source);
    await vi.advanceTimersByTimeAsync(30_000);
    return promise;
  };

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('zwraca sesję, gdy /api/auth/me odpowiada', async () => {
    const service = setup();
    me.mockReturnValue(of(session));

    await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'authenticated', session });
    expect(service.session()).toEqual(session);
    expect(service.isHydrated()).toBe(true);
  });

  it('401 = jedyny dowód wylogowania: czyści sesję i zatrzaskuje stan', async () => {
    const service = setup();
    me.mockReturnValue(throwError(() => apiError(401)));

    await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'anonymous' });
    expect(service.session()).toBeNull();
    expect(service.isHydrated()).toBe(true);
    // 401 to odpowiedź autorytatywna — ponawianie jej nie ma sensu.
    expect(me).toHaveBeenCalledOnce();
  });

  it('błąd sieci NIE wylogowuje — zwraca `unavailable` i nie zatrzaskuje stanu', async () => {
    const service = setup();
    me.mockReturnValue(throwError(() => apiError(0)));

    await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'unavailable' });
    expect(service.session()).toBeNull();
    // Kluczowe: „nie wiemy" ≠ „wylogowany". Gdyby to było `true`, każda kolejna nawigacja
    // dostawałaby z cache’u „wylogowany" aż do pełnego przeładowania — sedno zgłoszenia.
    expect(service.isHydrated()).toBe(false);
  });

  it('5xx (np. deploy API) też nie wylogowuje', async () => {
    const service = setup();
    me.mockReturnValue(throwError(() => apiError(503)));

    await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'unavailable' });
    expect(service.isHydrated()).toBe(false);
  });

  it('ponawia błąd przejściowy i odzyskuje sesję — zimny start PWA z usypionym radiem', async () => {
    const service = setup();
    let attempt = 0;
    me.mockImplementation(() =>
      defer(() => (++attempt === 1 ? throwError(() => apiError(0)) : of(session))),
    );

    await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'authenticated', session });
    expect(attempt).toBe(2);
    expect(service.session()).toEqual(session);
  });

  it('po nieudanej sondzie kolejna nawigacja próbuje ponownie (brak zatrzasku)', async () => {
    const service = setup();
    me.mockReturnValue(throwError(() => apiError(0)));
    await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'unavailable' });

    me.mockReturnValue(of(session));
    await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'authenticated', session });
  });

  it('deduplikuje równoległe wywołania (kilka guardów na jednej nawigacji)', async () => {
    const service = setup();
    me.mockReturnValue(of(session));

    const [a, b] = await Promise.all([settle(service.hydrate()), settle(service.hydrate())]);

    expect(a).toEqual({ kind: 'authenticated', session });
    expect(b).toEqual({ kind: 'authenticated', session });
    expect(me).toHaveBeenCalledOnce();
  });

  it('po ustaleniu sesji nie odpytuje serwera ponownie', async () => {
    const service = setup();
    me.mockReturnValue(of(session));

    await settle(service.hydrate());
    await settle(service.hydrate());

    expect(me).toHaveBeenCalledOnce();
  });

  it('retryHydrate() wymusza świeżą sondę po ekranie offline', async () => {
    const service = setup();
    me.mockReturnValue(throwError(() => apiError(0)));
    await settle(service.hydrate());

    me.mockReturnValue(of(session));
    await expect(settle(service.retryHydrate())).resolves.toEqual({ kind: 'authenticated', session });
  });

  /**
   * Sedno pozostałego zgłoszenia: offline `fetch` pada natychmiast, więc bez bramki cała seria ponowień
   * spalała się w ~2 s — czyli zanim telefon zdążył wybudzić radio. Efekt: ekran `/offline` mignął
   * w scenariuszu, w którym sieć wracała chwilę później.
   */
  describe('bramka na navigator.onLine', () => {
    it('nie strzela w martwą sieć — czeka na zdarzenie `online`', async () => {
      const service = setup();
      setOnLine(false);
      me.mockReturnValue(of(session));

      const promise = firstValueFrom(service.hydrate());

      await vi.advanceTimersByTimeAsync(3_000);
      expect(me).not.toHaveBeenCalled();

      setOnLine(true);
      window.dispatchEvent(new Event('online'));
      await vi.advanceTimersByTimeAsync(100);

      await expect(promise).resolves.toEqual({ kind: 'authenticated', session });
      expect(me).toHaveBeenCalledOnce();
    });

    it('po capie próbuje mimo wszystko — `onLine === false` bywa uparte (VPN)', async () => {
      const service = setup();
      setOnLine(false);
      me.mockReturnValue(of(session));

      await expect(settle(service.hydrate())).resolves.toEqual({ kind: 'authenticated', session });
      expect(me).toHaveBeenCalledOnce();
    });

    it('gdy sieć jest, nie opóźnia bootstrapu ani o krok', async () => {
      const service = setup();
      setOnLine(true);
      me.mockReturnValue(of(session));

      await expect(firstValueFrom(service.hydrate())).resolves.toEqual({
        kind: 'authenticated',
        session,
      });
    });
  });

  it('retryHydrate() w trakcie sondy nie gubi deduplikacji', async () => {
    const service = setup();
    me.mockReturnValue(throwError(() => apiError(0)));

    // Sonda A startuje i zostaje porzucona, zanim zdąży się zakończyć.
    void firstValueFrom(service.hydrate());
    const fresh$ = service.retryHydrate();

    // Kontrakt: równoległy guard dostaje TĘ SAMĄ sondę, nie własną. Bez znacznika generacji
    // `finalize` porzuconej sondy A zerowało rejestrację jej następczyni i dedup się rozpadał.
    expect(service.hydrate()).toBe(fresh$);

    await expect(settle(fresh$)).resolves.toEqual({ kind: 'unavailable' });
  });

  /**
   * Druga połowa historii sesji: 401 z żądania domenowego oznacza, że ciasteczko naprawdę umarło
   * (30 dni bezczynności, rotacja kluczy DataProtection). Wcześniej panel zostawał otwarty ze stanem
   * martwej sesji i sypał toastem przy każdym kolejnym żądaniu.
   */
  describe('wygaśnięcie sesji w trakcie pracy', () => {
    const expireWithLiveSession = async (service: AuthSessionService) => {
      me.mockReturnValue(of(session));
      await settle(service.hydrate());
      sessionExpiry.notifyExpired();
      return service;
    };

    it('czyści sesję i odsyła na /login', async () => {
      const service = await expireWithLiveSession(setup());

      expect(service.session()).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
      // Sesja jest autorytatywnie martwa — guardy mają iść na `/login`, nie na `/offline`.
      expect(service.isHydrated()).toBe(true);
      expect(invalidateCsrf).toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/login'], { replaceUrl: true });
    });

    it('salwa równoległych 401 daje JEDEN toast i JEDNĄ nawigację', async () => {
      const service = await expireWithLiveSession(setup());

      sessionExpiry.notifyExpired();
      sessionExpiry.notifyExpired();

      expect(navigate).toHaveBeenCalledTimes(1);
      expect(addToast).toHaveBeenCalledTimes(1);
      expect(service.session()).toBeNull();
    });

    it('401 bez aktywnej sesji jest ignorowane — nie ma czego wygaszać', () => {
      setup();

      sessionExpiry.notifyExpired();

      // Inaczej 401 z żądania anonimowego (np. na ekranie logowania) przerzucałby użytkownika
      // na `/login` z komunikatem o wygasłej sesji, której nigdy nie było.
      expect(navigate).not.toHaveBeenCalled();
      expect(addToast).not.toHaveBeenCalled();
    });
  });
});
