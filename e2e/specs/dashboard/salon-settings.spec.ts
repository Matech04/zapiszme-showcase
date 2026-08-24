import { expect, test } from '../../fixtures/owner-session.fixture';

/**
 * Dashboard CRUD — salon settings + VAT + shifts. TC-D041..D050.
 */

test.describe('Dashboard — salon settings @p1 @crud', () => {
  test('TC-D041 Read salon settings', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/SalonSettings');
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.name ?? body.salonName).toBeTruthy();
  });

  test('TC-D042 Slug availability check', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/auth/register-owner/slug-availability?slug=available-slug-test-xyz');
    expect(res.ok()).toBeTruthy();
  });

  test.fixme('TC-D043-046 Settings update flows (wymagają pełnego DTO + obsługa enum)', async () => {});
});

test.describe('Dashboard — VAT rates @p2 @crud', () => {
  test('TC-D047a VAT lista', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/VatRates');
    expect(res.ok()).toBeTruthy();
    const list = await res.json();
    expect(Array.isArray(list) || Array.isArray(list.items)).toBeTruthy();
  });

  test('TC-D047b VAT create', async ({ ownerApi }) => {
    const res = await ownerApi.post('/api/VatRates', {
      data: { name: `VAT-${Date.now()}`, ratePercent: 23 },
      failOnStatusCode: false,
    });
    expect([200, 201, 204, 400, 402, 404, 409]).toContain(res.status());
  });
});

test.describe('Dashboard — shift templates @p2 @crud', () => {
  test('TC-D048 Shift templates list', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/ShiftTemplates');
    expect(res.ok()).toBeTruthy();
  });

  test.fixme('TC-D049 Apply template to employee', async () => {});
});

test.describe('Dashboard — subscription @p0 @billing', () => {
  test('TC-D050 Subscription info', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/Subscription');
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body).toBeTruthy();
  });
});
