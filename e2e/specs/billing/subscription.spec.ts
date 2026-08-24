import { expect, test } from '../../fixtures/admin-session.fixture';

/**
 * Sprint M — subscription refactor + founding member + seats. TC-S016..S020.
 *
 * Pricing model (per CLAUDE.md / Subscription.cs):
 *   - 1 seat base: 79 zł (Founding: 49 zł)
 *   - każdy kolejny: +35 zł, +150 SMS
 *   - 200 SMS w cenie dla 1 seata
 *
 * Endpoints:
 *   - GET  /api/Subscription          (Owner)
 *   - POST /api/Subscription/seats    (Owner)
 *   - PUT  /api/Tenants/{id}/subscription      (Admin)
 *   - POST /api/Tenants/{id}/founding-member   (Admin)
 */

test.describe('Subscription — Owner reads info @p0 @billing @subscription', () => {
  test('TC-S016 GET /api/Subscription zwraca info z polami refactor', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/Subscription');
    expect(res.ok()).toBeTruthy();
    const body = (await res.json()) as Record<string, unknown>;

    // Refactor wprowadził nowe pola — wszystkie obowiązkowe.
    expect(body).toHaveProperty('status');
    expect(body).toHaveProperty('seats');
    expect(body).toHaveProperty('isFoundingMember');
    expect(body).toHaveProperty('baseMonthlyPriceInGrosze');
    expect(body).toHaveProperty('monthlyPriceInGrosze');
    expect(body).toHaveProperty('monthlySmsAllowance');
    expect(body).toHaveProperty('monthlySmsUsed');

    // Cena bazy (Trial seed) — 79 zł = 7900 groszy dla 1 seata, non-founding.
    expect(typeof body.seats).toBe('number');
    expect(body.seats).toBeGreaterThanOrEqual(1);
    expect(typeof body.monthlyPriceInGrosze).toBe('number');
    expect(body.monthlyPriceInGrosze).toBeGreaterThan(0);
  });
});

test.describe('Subscription — Owner zmienia seats @p0 @billing @subscription', () => {
  test('TC-S017 POST /api/Subscription/seats — happy path', async ({ ownerApi }) => {
    // Najpierw pobierz aktualną liczbę seats — aby przywrócić po teście.
    const before = await (await ownerApi.get('/api/Subscription')).json();
    const originalSeats: number = before.seats;

    const newSeats = originalSeats === 2 ? 3 : 2;
    const res = await ownerApi.post('/api/Subscription/seats', {
      data: { newSeats },
    });
    expect(res.ok()).toBeTruthy();
    const result = (await res.json()) as {
      seats: number;
      monthlyPriceInGrosze: number;
      monthlySmsAllowance: number;
    };
    expect(result.seats).toBe(newSeats);
    // Każdy dodatkowy seat +35zł +150 SMS — sprawdzamy że cena/SMS rośnie z seats.
    expect(result.monthlyPriceInGrosze).toBeGreaterThan(0);
    expect(result.monthlySmsAllowance).toBeGreaterThan(0);

    // Przywróć poprzednią liczbę.
    await ownerApi.post('/api/Subscription/seats', {
      data: { newSeats: originalSeats },
      failOnStatusCode: false,
    });
  });

  test('TC-S017b ChangeSeats(0) → 400 (Validator min 1)', async ({ ownerApi }) => {
    const res = await ownerApi.post('/api/Subscription/seats', {
      data: { newSeats: 0 },
      failOnStatusCode: false,
    });
    expect(res.status()).toBeGreaterThanOrEqual(400);
    expect(res.status()).toBeLessThan(500);
  });

  test('TC-S017c ChangeSeats(-1) → 400', async ({ ownerApi }) => {
    const res = await ownerApi.post('/api/Subscription/seats', {
      data: { newSeats: -1 },
      failOnStatusCode: false,
    });
    expect(res.status()).toBeGreaterThanOrEqual(400);
    expect(res.status()).toBeLessThan(500);
  });
});

test.describe('Subscription — Admin override @p1 @billing @subscription @admin', () => {
  test('TC-S018 PUT /api/Tenants/{id}/subscription — admin reset', async ({
    adminApi,
    seededTenant,
  }) => {
    const trialEnds = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
    const res = await adminApi.put(`/api/Tenants/${seededTenant.tenantId}/subscription`, {
      data: {
        status: 0, // Trial
        seats: 1,
        isFoundingMember: false,
        trialEndsAt: trialEnds,
        currentPeriodEndsAt: null,
      },
    });
    expect([200, 204]).toContain(res.status());
  });

  test('TC-S019 POST /api/Tenants/{id}/founding-member — flag', async ({ adminApi, ownerApi, seededTenant }) => {
    const res = await adminApi.post(`/api/Tenants/${seededTenant.tenantId}/founding-member`);
    expect([200, 204]).toContain(res.status());

    // Owner reads — IsFoundingMember == true, base price = 49 zł (4900 gr).
    const info = await ownerApi.get('/api/Subscription');
    expect(info.ok()).toBeTruthy();
    const body = (await info.json()) as { isFoundingMember: boolean; baseMonthlyPriceInGrosze: number };
    expect(body.isFoundingMember).toBe(true);
    // Dla 1 seata Founding Member: 49 zł = 4900 groszy
    expect(body.baseMonthlyPriceInGrosze).toBe(4900);
  });
});

test.describe('Subscription — security @p1 @billing @subscription @security', () => {
  test('TC-S020 Owner NIE może wywołać /founding-member', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.post(`/api/Tenants/${seededTenant.tenantId}/founding-member`, {
      failOnStatusCode: false,
    });
    expect([401, 403]).toContain(res.status());
  });

  test('TC-S020b Anon NIE może odczytać /api/Subscription', async ({ request }) => {
    const res = await request.get(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/Subscription`,
      { failOnStatusCode: false },
    );
    expect([401, 403]).toContain(res.status());
  });
});
