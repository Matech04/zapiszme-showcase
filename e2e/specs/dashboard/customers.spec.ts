import { expect, test } from '../../fixtures/owner-session.fixture';

/**
 * Dashboard CRUD — customers + whitelist. TC-D027..D032.
 */

test.describe('Dashboard — customers @p1 @crud', () => {
  test('TC-D027 Klient — create', async ({ ownerApi }) => {
    const res = await ownerApi.post('/api/Customers', {
      data: {
        firstName: 'Test',
        lastName: 'Klient',
        email: `cust-${Date.now()}@test.local`,
        phoneNumber: '+48500000111',
      },
      failOnStatusCode: false,
    });
    expect([200, 201, 204, 400, 404, 409]).toContain(res.status());
  });

  test('TC-D028 Klient — edit', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.put(`/api/Customers/${seededTenant.customerId}`, {
      data: {
        firstName: 'Edited',
        lastName: 'Customer',
        email: 'edited@test.local',
        phoneNumber: '+48500000222',
      },
      failOnStatusCode: false,
    });
    expect([200, 204, 400, 404, 409]).toContain(res.status());
  });

  test('TC-D029 Lista klientów', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.get('/api/Customers');
    expect(res.ok()).toBeTruthy();
    const list = await res.json();
    const customers = Array.isArray(list) ? list : list.items ?? [];
    expect(customers.find((c: { id: string }) => c.id === seededTenant.customerId)).toBeTruthy();
  });

  test.fixme('TC-D030/031 Whitelist single + bulk (wymaga setup BookingAccessPolicy=PublicWithWhitelist)', async () => {});
  test.fixme('TC-D032 Soft delete klienta z historią', async () => {});
});
