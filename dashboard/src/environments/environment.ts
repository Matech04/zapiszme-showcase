/** Środowisko developerskie (`ng serve` / build `development`). Produkcja: `environment.production.ts` przez fileReplacements w `angular.json`. */
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5171',
  bookingBaseUrl: 'http://localhost:4321',
  // Cloudflare Turnstile public TEST site key — zawsze passes, działa na każdym hostname.
  // Prod build podmienia ten plik na environment.production.ts (fileReplacements w angular.json).
  turnstileSiteKey: '1x00000000000000000000AA'
};
