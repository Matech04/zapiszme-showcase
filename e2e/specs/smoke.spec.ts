import { expect, test } from '@playwright/test';
import { E2eApi } from '../helpers/api-client';

test.describe('smoke @smoke @p0', () => {
  test('backdoor /api/_e2e/seed returns tenant data', async ({ request }) => {
    const api = new E2eApi(request);
    const seed = await api.seed();

    expect(seed.tenantSlug).toBe('rest-api-integration');
    expect(seed.ownerEmail).toBe('owner@rest-seed.local');
    expect(seed.tenantId).toMatch(/^[0-9a-f-]{36}$/);
    expect(seed.employeeId).toMatch(/^[0-9a-f-]{36}$/);
    expect(seed.serviceId).toMatch(/^[0-9a-f-]{36}$/);
  });

  test('backdoor /api/_e2e/time/advance moves FakeTimeProvider', async ({ request }) => {
    const api = new E2eApi(request);
    await api.advanceTime(120);
    // sukces = brak rzutu; assert pełniejszy w spec używającym czasu (np. hold lease).
  });

  // TC-A011 jest dokładnie pokryty przez specs/auth/login.spec.ts — pominięto duplikat.
});
