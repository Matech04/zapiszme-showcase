import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export const CLIENT_MODE_HEADER = 'X-Client-Mode';

/**
 * Dokleja do żądań informację, czy panel działa jako ZAINSTALOWANA PWA, czy w zwykłej przeglądarce.
 *
 * Serwer nie ma jak tego wywnioskować: na iOS User-Agent aplikacji z ekranu głównego jest identyczny
 * jak Safari. Bez tego nagłówka log produkcyjny pokazywał 23 utracone sesje w 8 dni i ani jednej
 * przesłanki, w którym kontekście zniknęły — a to właśnie kontekst jest podejrzany, bo zainstalowana
 * PWA na iOS ma osobny magazyn ciasteczek niż Safari.
 *
 * Wartość jest niewrażliwa (dwa stałe napisy), więc nie podlega maskowaniu i nie rozszerza zakresu
 * danych osobowych w logach.
 */
export const clientModeInterceptor: HttpInterceptorFn = (req, next) => {
  const url = req.url.startsWith('http') ? req.url : `${environment.apiBaseUrl}${req.url}`;
  if (!url.startsWith(environment.apiBaseUrl)) {
    return next(req);
  }

  return next(req.clone({ headers: req.headers.set(CLIENT_MODE_HEADER, detectClientMode()) }));
};

/**
 * `display-mode: standalone` łapie iOS/Android/desktop po instalacji, `navigator.standalone` to
 * starszy wariant wyłącznie iOS-owy — trzymamy oba, bo Safari nadal raportuje przez ten drugi.
 */
function detectClientMode(): 'standalone' | 'browser' {
  if (typeof window === 'undefined') {
    return 'browser';
  }

  const nav = window.navigator as Navigator & { standalone?: boolean };
  const standalone =
    window.matchMedia?.('(display-mode: standalone)').matches === true || nav.standalone === true;

  return standalone ? 'standalone' : 'browser';
}
