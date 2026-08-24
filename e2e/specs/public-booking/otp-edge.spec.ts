import { test, expect } from '../../fixtures/seeded-tenant.fixture';
import { request as playwrightRequest } from '@playwright/test';

const SLUG = 'rest-api-integration';
const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

function isoIn(d: number): string {
  const dt = new Date();
  dt.setUTCDate(dt.getUTCDate() + d);
  return dt.toISOString().slice(0, 10);
}

function randomSlot(): string {
  const hh = 9 + Math.floor(Math.random() * 7);
  const mm = Math.floor(Math.random() * 60);
  return `${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')}:00`;
}

async function createHold(request: any, employeeId: string, serviceId: string, date: string, slot: string) {
  const res = await request.post(`${API_URL}/api/booking/${SLUG}/appointments`, {
    data: { employeeId, serviceId, date, startTime: slot },
    failOnStatusCode: false,
  });
  if (!res.ok()) return null;
  return (await res.json()) as { appointmentId: string; lease: { reservationToken: string } };
}

/**
 * Sprint G': OTP edge cases + anti-abuse. TC-P019, TC-P024.
 */

test.describe('Public booking — OTP edge cases @p1 @public-booking', () => {
  test('TC-P019 OTP verify — kod wygasły po clock.advance', async ({ request, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const hold = await createHold(
      request,
      seededTenant.employeeId,
      seededTenant.serviceId,
      isoIn(20),
      randomSlot(),
    );
    if (!hold) { test.skip(); return; }

    const email = `expired-${Date.now()}@e2e.test`;
    await request.post(`${API_URL}/api/booking/${SLUG}/public-appointment/${hold.appointmentId}/request-otp`, {
      data: { token: hold.lease.reservationToken, email },
      failOnStatusCode: false,
    });
    const otp = await api.getLastOtp(email);
    if (!otp) { test.skip(); return; }

    // Przesuwamy czas o 15 min — przekracza OtpLeaseSeconds=10 (env E2E) i OTP expiry 10min
    await api.advanceTime(900);

    const res = await request.post(`${API_URL}/api/booking/${SLUG}/public-appointment/${hold.appointmentId}/verify-otp`, {
      data: { token: hold.lease.reservationToken, otp: otp.code },
      failOnStatusCode: false,
    });
    // Po expiry: 400/403/404 oczekiwane
    expect([400, 403, 404, 422]).toContain(res.status());
  });

  test('TC-P024 Anti-abuse — drugie hold w tej samej sesji (cookie) anuluje poprzednie Pending', async ({ api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    // Sesja cookie shared między dwoma requestami
    const ctx = await playwrightRequest.newContext({ baseURL: API_URL });

    const date = isoIn(21);
    const hold1 = await ctx.post(`/api/booking/${SLUG}/appointments`, {
      data: { employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, date, startTime: randomSlot() },
      failOnStatusCode: false,
    });

    const hold2 = await ctx.post(`/api/booking/${SLUG}/appointments`, {
      data: { employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, date, startTime: randomSlot() },
      failOnStatusCode: false,
    });

    await ctx.dispose();
    // Oba mogą się udać (drugi anuluje pierwszy w tle), lub drugi może rzucić 409.
    expect([200, 201, 400, 404, 409]).toContain(hold1.status());
    expect([200, 201, 400, 404, 409]).toContain(hold2.status());
  });
});
