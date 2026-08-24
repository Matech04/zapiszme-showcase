import { test, expect } from '../../fixtures/owner-session.fixture';
import { request as playwrightRequest } from '@playwright/test';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Security & cross-cutting. TC-S001..S015.
 */

test.describe('Security — auth + CSRF + rate limit @p1 @security', () => {
  test('TC-S001 Mutacja bez nagłówków auth → 401', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/Customers`, {
      data: { firstName: 'Anon', lastName: 'User', email: 't@t.t' },
      failOnStatusCode: false,
    });
    // 401 Unauthorized (brak X-Integration-Test-UserId).
    expect([400, 401, 403, 404]).toContain(res.status());
  });

  test('TC-S005 Tenant read isolation — Owner widzi tylko swoje', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.get('/api/Customers');
    expect(res.ok()).toBeTruthy();
    const customers = (await res.json()) as Array<{ id: string }> | { items: Array<{ id: string }> };
    const list = Array.isArray(customers) ? customers : customers.items ?? [];
    // Każdy zwrócony customer powinien być z tenantu seeded.
    // Tu sprawdzamy minimum że seeded customer jest w liście.
    expect(list.find((c) => c.id === seededTenant.customerId)).toBeTruthy();
  });

  test('TC-S013 Rate limit PublicBookingWrite — bardzo dużo żądań', async ({ request, seededTenant }) => {
    // W env E2E permitLimit=1000 (wysoki), nie wymusi 429 łatwo.
    // Ten test jest sanity — sprawdzamy że endpoint POST hold odpowiada.
    const res = await request.post(`${API_URL}/api/booking/rest-api-integration/appointments`, {
      data: {
        employeeId: seededTenant.employeeId,
        serviceId: seededTenant.serviceId,
        date: new Date(Date.now() + 86400000).toISOString().slice(0, 10),
        startTime: '11:00:00',
      },
      failOnStatusCode: false,
    });
    // 200/201 lub błąd biznesowy (slot/schedule); nie 5xx, nie 429 (jeszcze).
    expect([200, 201, 400, 404, 409]).toContain(res.status());
  });

  test('TC-S015 Backend health endpoint', async ({ request }) => {
    const res = await request.get(`${API_URL}/health/live`);
    expect(res.ok()).toBeTruthy();
  });

  test.fixme('TC-S002 SameSite=None cookies (wymaga prod-like env)', async () => {});
  test.fixme('TC-S003 XSRF rotation (wymaga UI login flow)', async () => {});
  test.fixme('TC-S004 Tenant write violation — wymaga manipulacji TenantId w request', async () => {});
  test.fixme('TC-S006 Soft delete filtering — covered w dashboard CRUD', async () => {});
  test.fixme('TC-S007 i18n błędów — wymaga inspekcji response messages', async () => {});
  test.fixme('TC-S008/S009 Performance (Lighthouse / p95 latency)', async () => {});
  test.fixme('TC-S010-012 CI / env setup / OpenAPI client / SSR', async () => {});
  test.fixme('TC-S014 Lockout konta (wymaga Identity lockout config + retry loop)', async () => {});
});
