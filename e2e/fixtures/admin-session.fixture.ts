import { request as playwrightRequest, type APIRequestContext } from '@playwright/test';
import { test as ownerSessionTest } from './owner-session.fixture';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/** SystemAdmin (Admin role) — globalny user wykorzystywany do CRUD'a kodów promo. */
const SYSTEM_ADMIN_USER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

/**
 * Authenticated API context dla SystemAdmin'a (rola "Admin").
 * Wymagany do endpointów `[Authorize(Policy = "SystemAdminOnly")]` (np. /api/admin/promocodes,
 * /api/Tenants/*). Rozszerza owner-session.fixture — dzięki temu można w jednym teście
 * mieć i `ownerApi`, i `adminApi` (cross-role checks).
 */
export const test = ownerSessionTest.extend<{
  adminApi: APIRequestContext;
}>({
  // Depend na `seededTenant` żeby SeedIdentity dodało użytkownika SystemAdmin
  // do bazy — bez tego admin route'y rzucają 500 (foreign key na Identity user).
  adminApi: async ({ seededTenant }, use) => {
    void seededTenant; // force fixture init
    const ctx = await playwrightRequest.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: {
        'X-Integration-Test-UserId': SYSTEM_ADMIN_USER_ID,
        'X-Integration-Test-Roles': 'Admin',
      },
    });
    await use(ctx);
    await ctx.dispose();
  },
});

export { expect } from '@playwright/test';
