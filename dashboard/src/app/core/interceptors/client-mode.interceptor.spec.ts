import { HttpHandlerFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { environment } from '../../../environments/environment';
import { CLIENT_MODE_HEADER, clientModeInterceptor } from './client-mode.interceptor';

/**
 * Nagłówek istnieje wyłącznie po to, żeby log produkcyjny odróżnił zainstalowaną PWA od zwykłej
 * przeglądarki — na iOS User-Agent obu jest identyczny, więc serwer sam tego nie wywnioskuje.
 */
describe('clientModeInterceptor', () => {
  let matchMediaSpy: ReturnType<typeof vi.spyOn> | undefined;

  const run = (url: string): HttpRequest<unknown> => {
    let seen!: HttpRequest<unknown>;
    const next: HttpHandlerFn = (req) => {
      seen = req;
      // `HttpHandlerFn` musi zwrócić `Observable<HttpEvent<unknown>>`. Wcześniejsze
      // `of({} as never)` kompilator rozwiązywał do `Observable<null>` i wywracał kompilację
      // CAŁEJ suity vitest — czyli ani jeden test w panelu się nie uruchamiał.
      return of(new HttpResponse<unknown>());
    };
    TestBed.runInInjectionContext(() => {
      clientModeInterceptor(new HttpRequest('GET', url), next).subscribe();
    });
    return seen;
  };

  beforeEach(() => TestBed.configureTestingModule({}));

  afterEach(() => {
    matchMediaSpy?.mockRestore();
    matchMediaSpy = undefined;
    delete (window.navigator as Navigator & { standalone?: boolean }).standalone;
  });

  it('oznacza żądanie jako browser poza trybem standalone', () => {
    matchMediaSpy = vi
      .spyOn(window, 'matchMedia')
      .mockReturnValue({ matches: false } as MediaQueryList);

    expect(run(`${environment.apiBaseUrl}/api/auth/me`).headers.get(CLIENT_MODE_HEADER)).toBe(
      'browser',
    );
  });

  it('oznacza żądanie jako standalone, gdy display-mode to potwierdza', () => {
    matchMediaSpy = vi
      .spyOn(window, 'matchMedia')
      .mockReturnValue({ matches: true } as MediaQueryList);

    expect(run(`${environment.apiBaseUrl}/api/auth/me`).headers.get(CLIENT_MODE_HEADER)).toBe(
      'standalone',
    );
  });

  /** Starsze iOS-y nie raportują `display-mode`, tylko `navigator.standalone` — stąd oba warunki. */
  it('łapie iOS-owy navigator.standalone, gdy display-mode milczy', () => {
    matchMediaSpy = vi
      .spyOn(window, 'matchMedia')
      .mockReturnValue({ matches: false } as MediaQueryList);
    (window.navigator as Navigator & { standalone?: boolean }).standalone = true;

    expect(run(`${environment.apiBaseUrl}/api/auth/me`).headers.get(CLIENT_MODE_HEADER)).toBe(
      'standalone',
    );
  });

  /** Nagłówek jedzie wyłącznie do własnego API — nie wyciekamy go do obcych hostów. */
  it('nie dokleja nagłówka do żądań spoza API', () => {
    matchMediaSpy = vi
      .spyOn(window, 'matchMedia')
      .mockReturnValue({ matches: false } as MediaQueryList);

    expect(run('https://challenges.cloudflare.com/turnstile').headers.has(CLIENT_MODE_HEADER)).toBe(
      false,
    );
  });
});
