import { request as playwrightRequest, type APIRequestContext } from '@playwright/test';
import { test as seededTenantTest } from './seeded-tenant.fixture';
import { E2eApi } from '../helpers/api-client';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Authenticated API context dla ownera tenantu testowego.
 *
 * Używa IntegrationTestAuthenticationHandler (aktywny w env E2E) — wysyła nagłówki
 * X-Integration-Test-UserId i X-Integration-Test-Roles, omijając cookie Identity.
 * Najszybsza ścieżka do API requestów które normalnie wymagają zalogowania, bez UI.
 *
 * Headery są zdefiniowane w backend/src/App.Api/Authentication/IntegrationTestAuthHeaders.cs.
 */
export const test = seededTenantTest.extend<{
  ownerApi: APIRequestContext;
}>({
  ownerApi: async ({ seededTenant }, use) => {
    const ctx = await playwrightRequest.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: {
        'X-Integration-Test-UserId': seededTenant.ownerUserId,
        'X-Integration-Test-Roles': 'Owner',
      },
    });
    await use(ctx);
    await ctx.dispose();
  },
});

export { expect } from '@playwright/test';
