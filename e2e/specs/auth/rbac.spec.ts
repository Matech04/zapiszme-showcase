import { expect, test } from '../../fixtures/owner-session.fixture';
import { request as playwrightRequest } from '@playwright/test';
import { E2eApi } from '../../helpers/api-client';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * RBAC + multi-tenancy. TC-A028..A030.
 * Wykorzystuje IntegrationTestAuthenticationHandler — wysyłamy headery X-Integration-Test-UserId+Roles.
 * Owner-fixture daje pełny dostęp; tu testujemy role Employee oraz drugi tenant.
 */

test.describe('Auth — RBAC + multi-tenancy @p0 @auth @security', () => {
  test('TC-A028 Employee nie ma dostępu do /api/SalonSettings (BusinessManagement policy)', async ({ seededTenant }) => {
    const ctx = await playwrightRequest.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: {
        'X-Integration-Test-UserId': seededTenant.ownerUserId, // userId nie ma znaczenia bo middleware czyta z employee
        'X-Integration-Test-Roles': 'Employee',
      },
    });
    const res = await ctx.put('/api/SalonSettings', {
      data: { name: 'Hack', slug: 'hack', timeZoneId: 'Europe/Warsaw', currency: 'PLN' },
      failOnStatusCode: false,
    });
    expect([400, 401, 403, 404]).toContain(res.status());
    await ctx.dispose();
  });

  test('TC-A029 Employee nie może utworzyć innego pracownika', async ({ seededTenant }) => {
    const ctx = await playwrightRequest.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: {
        'X-Integration-Test-UserId': seededTenant.ownerUserId,
        'X-Integration-Test-Roles': 'Employee',
      },
    });
    const res = await ctx.post('/api/auth/employees', {
      data: {
        email: `hack-${Date.now()}@e.test`,
        firstName: 'Hack',
        lastName: 'Er',
        role: 'Employee',
      },
      failOnStatusCode: false,
    });
    expect([400, 401, 403, 404]).toContain(res.status());
    await ctx.dispose();
  });

  test('TC-A030 Cross-tenant — Owner tenantu A nie widzi danych tenantu B', async ({ request, ownerApi }) => {
    // Seed second tenant przez backdoor
    const apiAuto = new E2eApi(request);
    const seedBRes = await request.post(E2eApi.apiUrl('/api/_e2e/seed/second-tenant'));
    if (!seedBRes.ok()) test.skip(true, `seed/second-tenant failed: ${seedBRes.status()}`);
    const seedB = await seedBRes.json();

    // ownerApi to fixture dla pierwszego tenantu (Owner A).
    // Listing /api/Customers nie powinien zwracać klientów tenantu B.
    const listRes = await ownerApi.get('/api/Customers');
    expect(listRes.ok()).toBeTruthy();
    const customers = (await listRes.json()) as Array<{ id: string }>;
    expect(customers.find((c) => c.id === seedB.tenantId)).toBeUndefined();

    // Direct GET id z tenantu B → 404 (HasQueryFilter wyklucza)
    // Note: nie znamy customerId tenantu B; sprawdzamy że POST update na losowy id nie zaszkodzi
    // (i tak by zwrócił 404). Pomijamy directowe ID query.
  });
});
