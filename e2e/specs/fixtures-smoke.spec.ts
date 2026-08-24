import { test, expect } from '../fixtures/seeded-tenant.fixture';

test.describe('fixtures smoke @smoke @p1', () => {
  test('seededTenant fixture wstrzykuje dane tenantu', async ({ seededTenant }) => {
    expect(seededTenant.tenantSlug).toBe('rest-api-integration');
    expect(seededTenant.ownerEmail).toBe('owner@rest-seed.local');
    expect(seededTenant.employeeId).toMatch(/^[0-9a-f-]{36}$/);
    expect(seededTenant.serviceId).toMatch(/^[0-9a-f-]{36}$/);
  });

  test('api fixture zwraca działający E2eApi', async ({ api }) => {
    await api.advanceTime(10);
    // brak rzutu = sukces
  });
});
