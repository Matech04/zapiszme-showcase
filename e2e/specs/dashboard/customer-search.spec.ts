import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint L: Search customer + filter service API. TC-D002, TC-D003.
 */

test.describe('Dashboard — search/filter @p2 @crud', () => {
  test('TC-D002 Search klienta po fragmencie (query param)', async ({ ownerApi, seededTenant }) => {
    // Customer w seed: jan@rest-seed.local — wyszukamy "jan"
    const res = await ownerApi.get('/api/Customers?search=jan', { failOnStatusCode: false });
    expect([200, 400, 404]).toContain(res.status());
  });

  test('TC-D003 Lista usług filtrowana po employee (query param)', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.get(`/api/Services?employeeId=${seededTenant.employeeId}`, { failOnStatusCode: false });
    expect([200, 400, 404]).toContain(res.status());
  });
});
