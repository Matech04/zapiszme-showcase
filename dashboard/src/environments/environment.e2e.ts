/**
 * Build configuration `e2e` — Playwright odpala dashboard via `npm run start:e2e`.
 * Backend w env ASPNETCORE_ENVIRONMENT=E2E jedzie na porcie 5199 (zob.
 * e2e/playwright.config.ts) i ma wyłączony Turnstile (appsettings.E2E.json).
 *
 * Pusty `turnstileSiteKey` wyłącza ładowanie skryptu Cloudflare i widoczność
 * widgetu w UI — bez tego Playwright (headless Chromium) jest blokowany przez
 * bot detection i przycisk Submit pozostaje disabled.
 */
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5199',
  bookingBaseUrl: 'http://localhost:4321',
  turnstileSiteKey: ''
};
