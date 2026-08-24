import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright E2E config — booking_saas
 *
 * Web servery uruchamiane przez `webServer` array:
 *   1. backend .NET (ASPNETCORE_ENVIRONMENT=E2E) → http://localhost:5199
 *   2. dashboard Angular (ng serve) → http://localhost:4201
 *   3. web Astro/Svelte (astro dev) → http://localhost:4321
 *
 * Backend musi mieć w env `ConnectionStrings:DefaultConnection` wskazujący
 * na bazę `booking_e2e` (skonfigurowane w backend/src/App.Api/appsettings.E2E.json).
 * Lokalnie: `docker compose -f docker-compose.dev.yml up -d` + ręcznie utworzona DB
 * `CREATE DATABASE booking_e2e;` (jeśli jeszcze nie istnieje).
 *
 * Tagi:
 *   @p0    — krytyczne ścieżki biznesowe (PR/blocker tests, ~15 min)
 *   @smoke — szybki sanity check (sub-30s, dymny test)
 *   @p1, @p2 — rozszerzony zakres, nightly
 */
const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';
const DASHBOARD_URL = process.env.E2E_DASHBOARD_URL ?? 'http://localhost:4201';
const WEB_URL = process.env.E2E_WEB_URL ?? 'http://localhost:4321';

export default defineConfig({
  testDir: './specs',
  fullyParallel: false, // shared DB seed — sekwencyjnie do czasu izolacji per tenant
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 1,
  workers: process.env.CI ? 1 : 1,
  reporter: process.env.CI
    ? [['html', { open: 'never' }], ['github']]
    : [['html', { open: 'on-failure' }], ['list']],
  use: {
    baseURL: DASHBOARD_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 10_000,
    navigationTimeout: 30_000,
  },
  expect: {
    timeout: 5_000,
  },
  projects: [
    {
      name: 'chromium-dashboard',
      use: { ...devices['Desktop Chrome'], baseURL: DASHBOARD_URL },
    },
    {
      name: 'chromium-web',
      use: { ...devices['Desktop Chrome'], baseURL: WEB_URL },
      testMatch: /public-booking\/.*\.spec\.ts/,
    },
  ],
  webServer: [
    {
      name: 'backend (E2E)',
      command:
        'cd ../backend/src/App.Api && ASPNETCORE_ENVIRONMENT=E2E ASPNETCORE_URLS=http://localhost:5199 dotnet run --no-launch-profile',
      url: `${API_URL}/health/live`,
      reuseExistingServer: !process.env.CI,
      timeout: 90_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      name: 'dashboard',
      // start:e2e używa environment.e2e.ts (Turnstile OFF, apiBaseUrl=5199)
      // — bez tego widget Cloudflare blokuje submit w headless Chromium.
      command: 'cd ../dashboard && npm run start:e2e',
      url: DASHBOARD_URL,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      name: 'web',
      // Web MUSI wołać backend E2E (5199), nie dev-port z web/.env (worktree-sync ustawia tam
      // lokalny port). Zmienna środowiskowa ma w Vite/Astro priorytet nad plikiem .env.
      command: `cd ../web && PUBLIC_API_BASE_URL=${API_URL} npm run dev`,
      url: WEB_URL,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
