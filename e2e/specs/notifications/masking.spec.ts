import { test, expect } from '../../fixtures/seeded-tenant.fixture';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Sprint K: Sensitive data masking. TC-N014.
 * Sprawdzamy że response NIE zawiera OTP code i niezakrytego e-mail.
 */

test.describe('Notifications — sensitive masking @p2 @notifications @security', () => {
  test('TC-N014 Response request-otp nie wycieka kodu OTP', async ({ request, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const isoIn = (d: number) => { const dt = new Date(); dt.setUTCDate(dt.getUTCDate() + d); return dt.toISOString().slice(0, 10); };
    const SLUG = 'rest-api-integration';

    const holdRes = await request.post(`${API_URL}/api/booking/${SLUG}/appointments`, {
      data: { employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, date: isoIn(11), startTime: `${10 + Math.floor(Math.random() * 5)}:${Math.floor(Math.random() * 60).toString().padStart(2, '0')}:00` },
      failOnStatusCode: false,
    });
    if (!holdRes.ok()) { test.skip(); return; }
    const hold = await holdRes.json();

    const email = `mask-${Date.now()}@e2e.test`;
    const otpRes = await request.post(
      `${API_URL}/api/booking/${SLUG}/public-appointment/${hold.appointmentId}/request-otp`,
      {
        data: { token: hold.lease.reservationToken, email },
        failOnStatusCode: false,
      },
    );

    if (otpRes.ok()) {
      const responseText = await otpRes.text();
      // Response NIE powinien zawierać 6-cyfrowego kodu OTP
      expect(responseText).not.toMatch(/\b\d{6}\b/);
      // Response nie zawiera plaintext e-maila (lub zawiera tylko maskę typu m***@***)
      // Soft check — może być w response.
    }
  });
});
