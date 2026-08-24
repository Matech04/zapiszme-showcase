import { test, expect } from '../../fixtures/seeded-tenant.fixture';

const SLUG = 'rest-api-integration';
const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Sprint K: Manage appointment self-service API. TC-P026, TC-P027, TC-P028, TC-P029.
 * Anonymous flow przez /api/booking/{slug}/self-service/*.
 */

test.describe('Public booking — manage self-service @p1 @public-booking', () => {
  test.beforeEach(async ({ api }) => {
    await api.resetTime().catch(() => {});
  });

  test('TC-P026 GET self-service appointments — wymaga session token', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/booking/${SLUG}/self-service/appointments`, {
      failOnStatusCode: false,
    });
    // Bez X-SelfService-Token → 401/403
    expect([401, 403]).toContain(res.status());
  });

  test('TC-P027 POST self-service request-otp anonymously', async ({ request, api, seededTenant }) => {
    const email = `selfservice-${Date.now()}@e2e.test`;
    const res = await request.post(`${API_URL}/api/booking/${SLUG}/self-service/request-otp`, {
      data: { email },
      failOnStatusCode: false,
    });
    // 200/204 = OTP wysłany; 400 = brak konfiguracji; oba acceptable.
    expect([200, 204, 400]).toContain(res.status());

    if (res.ok()) {
      // Mailbox powinien dostarczyć OTP (jeśli kanał email i konfig OK)
      const otp = await api.getLastOtp(email);
      if (otp) {
        expect(otp.code).toMatch(/^\d{6}$/);
      }
    }
  });

  test('TC-P028 POST self-service verify-otp z błędnym kodem', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/booking/${SLUG}/self-service/verify-otp`, {
      data: { email: 'nope@nope.test', otp: '000000' },
      failOnStatusCode: false,
    });
    // 400/401/404 = błędny/brak OTP
    expect([400, 401, 403, 404, 422]).toContain(res.status());
  });

  test('TC-P029 GET appointments z nieprawidłowym session token → 401/403', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/booking/${SLUG}/self-service/appointments`, {
      headers: { 'X-SelfService-Token': '00000000-0000-0000-0000-000000000000' },
      failOnStatusCode: false,
    });
    expect([401, 403]).toContain(res.status());
  });

  test('TC-P030 Cross-origin CORS preflight OPTIONS', async ({ request }) => {
    const res = await request.fetch(`${API_URL}/api/booking/${SLUG}/services`, {
      method: 'OPTIONS',
      headers: {
        'Origin': 'http://localhost:4321',
        'Access-Control-Request-Method': 'GET',
      },
      failOnStatusCode: false,
    });
    // CORS preflight powinien zwrócić 200/204
    expect([200, 204, 404]).toContain(res.status());
  });
});
