import { HttpErrorResponse, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { firstValueFrom, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { errorInterceptor } from './error.interceptor';
import { SessionExpiryService } from '@core/auth/session-expiry.service';
import { CsrfTokenService } from '@core/auth/csrf-token.service';

describe('errorInterceptor', () => {
  let messages: { add: ReturnType<typeof vi.fn> };
  let notifyExpired: ReturnType<typeof vi.fn>;
  let csrfRefresh: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    messages = { add: vi.fn() };
    notifyExpired = vi.fn();
    csrfRefresh = vi.fn().mockReturnValue(of('swiezy-token'));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MessageService, useValue: messages },
        { provide: SessionExpiryService, useValue: { notifyExpired } },
        { provide: CsrfTokenService, useValue: { refresh: csrfRefresh } },
      ],
    });
  });

  function run(req: HttpRequest<unknown>, error: HttpErrorResponse) {
    return TestBed.runInInjectionContext(() =>
      errorInterceptor(req, () => throwError(() => error)),
    );
  }

  /** Wariant z kontrolą kolejnych odpowiedzi — do ścieżki „ponów po odświeżeniu tokenu". */
  function runSekwencja(req: HttpRequest<unknown>, odpowiedzi: (HttpErrorResponse | HttpResponse<unknown>)[]) {
    const wywolania: HttpRequest<unknown>[] = [];
    let i = 0;
    const strumien = TestBed.runInInjectionContext(() =>
      errorInterceptor(req, (r) => {
        wywolania.push(r);
        const odp = odpowiedzi[i++];
        return odp instanceof HttpResponse ? of(odp) : throwError(() => odp);
      }),
    );
    return { strumien, wywolania };
  }

  const antiforgery400 = (url: string) =>
    new HttpErrorResponse({
      error: { errorCode: 'auth.antiforgery_invalid', detail: 'Odśwież stronę i spróbuj ponownie.' },
      status: 400,
      url,
    });

  // Regresja: normalizacja blob→obiekt psuła `blobToText` w wygenerowanym kliencie NSwag,
  // przez co login gubił errorCode (PhoneNotConfirmed) i pokazywał zły komunikat. Dla
  // formularzy auth błąd musi przejść NIETKNIĘTY (blob zachowany) i bez toasta.
  it('passes login errors through untouched (blob preserved, no toast)', async () => {
    const blob = new Blob([JSON.stringify({ errorCode: 'auth.phone_not_confirmed' })], {
      type: 'application/problem+json',
    });
    const original = new HttpErrorResponse({
      error: blob,
      status: 401,
      url: 'http://localhost/api/auth/login',
    });
    const req = new HttpRequest('POST', 'http://localhost/api/auth/login', {});

    await expect(firstValueFrom(run(req, original))).rejects.toBe(original);
    expect(messages.add).not.toHaveBeenCalled();
  });

  // Kalendarz pobiera konfigurację grafiku best-effort i połyka 403 przez `catchError`.
  // Toast „Odmowa dostępu" byłby szumem — pracownik tylko otworzył widok zespołu.
  it.each(['employee-schedules', 'leaves', 'schedule-overrides'])(
    'nie toastuje 403 przy GET /api/Employees/{id}/%s',
    async (segment) => {
      const url = `http://localhost/api/Employees/11111111-1111-1111-1111-111111111111/${segment}`;
      const original = new HttpErrorResponse({ error: {}, status: 403, url });
      const req = new HttpRequest('GET', url);

      await expect(firstValueFrom(run(req, original))).rejects.toBe(original);
      expect(messages.add).not.toHaveBeenCalled();
    },
  );

  it('nadal toastuje 403 przy MUTACJI grafiku (POST schedule-overrides)', async () => {
    const url = 'http://localhost/api/Employees/11111111-1111-1111-1111-111111111111/schedule-overrides';
    const original = new HttpErrorResponse({ error: {}, status: 403, url });
    const req = new HttpRequest('POST', url, {});

    await expect(firstValueFrom(run(req, original))).rejects.toBeTruthy();
    expect(messages.add).toHaveBeenCalled();
  });

  /**
   * 401 z żądania domenowego = serwer odrzucił ciasteczko. Dotąd leciał tylko toast, a panel zostawał
   * otwarty ze stanem martwej sesji i sypał nim przy każdym kolejnym żądaniu.
   */
  describe('sygnał wygaśnięcia sesji', () => {
    it('401 z żądania domenowego zgłasza wygaśnięcie i NIE toastuje sam', async () => {
      const url = 'http://localhost/api/appointments';
      const original = new HttpErrorResponse({ error: {}, status: 401, url });
      const req = new HttpRequest('GET', url);

      await expect(firstValueFrom(run(req, original))).rejects.toBeTruthy();
      expect(notifyExpired).toHaveBeenCalledOnce();
      // Toast pokazuje `AuthSessionService` — dokładnie raz na salwę, nie raz na żądanie.
      expect(messages.add).not.toHaveBeenCalled();
    });

    it('401 z logowania NIE jest wygaśnięciem sesji — to złe hasło', async () => {
      const url = 'http://localhost/api/auth/login';
      const original = new HttpErrorResponse({ error: new Blob(['{}']), status: 401, url });
      const req = new HttpRequest('POST', url, {});

      await expect(firstValueFrom(run(req, original))).rejects.toBe(original);
      expect(notifyExpired).not.toHaveBeenCalled();
    });

    it('401 z sondy /api/auth/me obsługuje hydrate(), nie ta ścieżka', async () => {
      const url = 'http://localhost/api/auth/me';
      const original = new HttpErrorResponse({ error: {}, status: 401, url });
      const req = new HttpRequest('GET', url);

      await expect(firstValueFrom(run(req, original))).rejects.toBeTruthy();
      // Inaczej zimny start wylogowanego użytkownika przerzucałby go przez toast „Sesja wygasła".
      expect(notifyExpired).not.toHaveBeenCalled();
      expect(messages.add).not.toHaveBeenCalled();
    });

    it('403 nie jest wygaśnięciem — użytkownik jest zalogowany, brak mu uprawnień', async () => {
      const url = 'http://localhost/api/appointments';
      const original = new HttpErrorResponse({ error: {}, status: 403, url });
      const req = new HttpRequest('POST', url, {});

      await expect(firstValueFrom(run(req, original))).rejects.toBeTruthy();
      expect(notifyExpired).not.toHaveBeenCalled();
      expect(messages.add).toHaveBeenCalled();
    });
  });

  /**
   * Wygasła sesja NIE przychodzi jako 401 — antiforgery jest sprawdzane przed autoryzacją, więc
   * serwer odpowiada 400. Bez tej ścieżki użytkownik dostawał „Sprawdź dane i spróbuj ponownie"
   * i utykał w kreatorze salonu, mimo że jedyne, co mu zostało, to zalogować się ponownie.
   */
  describe('nieaktualny token antiforgery (400)', () => {
    it('odświeża token i ponawia żądanie — sesja mogła przeżyć sam token', async () => {
      const req = new HttpRequest('POST', 'http://localhost/api/onboarding/industry', {});
      const { strumien, wywolania } = runSekwencja(req, [
        antiforgery400('http://localhost/api/onboarding/industry'),
        new HttpResponse({ status: 200 }),
      ]);

      await expect(firstValueFrom(strumien)).resolves.toBeInstanceOf(HttpResponse);
      expect(csrfRefresh).toHaveBeenCalledOnce();
      expect(wywolania[1].headers.get('X-XSRF-TOKEN')).toBe('swiezy-token');
      // Odzyskane po cichu — użytkownik nie ma powodu widzieć błędu ani być wylogowanym.
      expect(notifyExpired).not.toHaveBeenCalled();
      expect(messages.add).not.toHaveBeenCalled();
    });

    it('gdy ponowienie też padnie, uznaje sesję za martwą', async () => {
      const req = new HttpRequest('POST', 'http://localhost/api/onboarding/industry', {});
      const { strumien } = runSekwencja(req, [
        antiforgery400('http://localhost/api/onboarding/industry'),
        antiforgery400('http://localhost/api/onboarding/industry'),
      ]);

      await expect(firstValueFrom(strumien)).rejects.toBeTruthy();
      // Stąd bierze się toast „Sesja wygasła" i przekierowanie na /login.
      expect(notifyExpired).toHaveBeenCalledOnce();
    });

    it('nie zapętla odświeżania — ponowione żądanie nie próbuje po raz trzeci', async () => {
      const req = new HttpRequest('POST', 'http://localhost/api/onboarding/industry', {});
      const { strumien, wywolania } = runSekwencja(req, [
        antiforgery400('http://localhost/api/onboarding/industry'),
        antiforgery400('http://localhost/api/onboarding/industry'),
      ]);

      await expect(firstValueFrom(strumien)).rejects.toBeTruthy();
      expect(wywolania).toHaveLength(2);
      expect(csrfRefresh).toHaveBeenCalledOnce();
    });
  });

  it('still surfaces a toast for non-auth errors', async () => {
    const original = new HttpErrorResponse({
      error: { detail: 'boom' },
      status: 500,
      url: 'http://localhost/api/appointments',
    });
    const req = new HttpRequest('GET', 'http://localhost/api/appointments');

    await expect(firstValueFrom(run(req, original))).rejects.toBeTruthy();
    expect(messages.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'error' }),
    );
  });
});
