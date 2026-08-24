import { test, expect } from '../../fixtures/seeded-tenant.fixture';
import type { APIRequestContext } from '@playwright/test';
import type { E2eSeedResult } from '../../helpers/api-client';

/**
 * Salon z włączonym wymaganiem imienia i nazwiska (Tenant.RequireCustomerName).
 * request-otp bez imienia musi zostać odrzucony PRZED wysłaniem kodu; pełen flow
 * z imieniem i nazwiskiem przechodzi. Seedowany tenant ma kanał Email + grafik Mon-Fri.
 */

const SLUG = 'rest-api-integration';
const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

function isoIn(daysAhead: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysAhead);
  return d.toISOString().slice(0, 10);
}

interface Hold {
  appointmentId: string;
  lease: { reservationToken: string };
}

/** Pierwszy realnie dostępny (date, slot) szukany od `fromDaysAhead` w przód (omija weekendy/zajęte). */
async function findSlot(
  request: APIRequestContext,
  seededTenant: E2eSeedResult,
  fromDaysAhead: number,
): Promise<{ date: string; slot: string } | null> {
  for (let d = fromDaysAhead; d < fromDaysAhead + 12; d++) {
    const date = isoIn(d);
    const res = await request.get(
      `${API_URL}/api/booking/${SLUG}/appointments/available-slots` +
        `?date=${date}&employeeId=${seededTenant.employeeId}&serviceId=${seededTenant.serviceId}`,
      { failOnStatusCode: false },
    );
    if (!res.ok()) continue;
    const slots = (await res.json()) as Array<{ slot?: string }>;
    const first = slots.find((s) => s.slot);
    if (first?.slot) return { date, slot: first.slot };
  }
  return null;
}

async function createHold(
  request: APIRequestContext,
  seededTenant: E2eSeedResult,
  date: string,
  slot: string,
): Promise<Hold | null> {
  // Hold idzie przez /public-appointment/hold — serwer wystawia AnonSessionId w cookie,
  // Turnstile w env E2E przepuszcza. (Stary endpoint /appointments został usunięty.)
  const res = await request.post(`${API_URL}/api/booking/${SLUG}/public-appointment/hold`, {
    data: {
      serviceId: seededTenant.serviceId,
      employeeId: seededTenant.employeeId,
      date,
      startTime: slot,
    },
    failOnStatusCode: false,
  });
  if (!res.ok()) return null;
  return (await res.json()) as Hold;
}

test.describe('Public booking — wymagane imię i nazwisko @p1 @public-booking', () => {
  // Tenant jest współdzielony między testami — po teście zdejmujemy wymóg nazwiska,
  // żeby nie zepsuć pozostałych scenariuszy bookingu (które działają na samym kontakcie).
  test.afterEach(async ({ api }) => {
    await api.setTenantSettings({ requireCustomerName: false }).catch(() => {});
  });

  test('request-otp bez imienia → 400 missing_name; z imieniem → OTP + verify OK', async ({
    request,
    api,
    seededTenant,
  }) => {
    await api.resetTime().catch(() => {});
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    await api.setTenantSettings({ requireCustomerName: true });

    // 1) Brak imienia/nazwiska — odrzucone zanim poleci jakikolwiek kod.
    const pick1 = await findSlot(request, seededTenant, 8);
    expect(pick1, 'powinien istnieć wolny slot (#1)').not.toBeNull();
    const hold1 = await createHold(request, seededTenant, pick1!.date, pick1!.slot);
    expect(hold1, 'hold #1 powinien się udać').not.toBeNull();

    const noName = await request.post(
      `${API_URL}/api/booking/${SLUG}/public-appointment/${hold1!.appointmentId}/request-otp`,
      {
        data: { token: hold1!.lease.reservationToken, email: `noname-${Date.now()}@e2e.test` },
        failOnStatusCode: false,
      },
    );
    expect(noName.status()).toBe(400);
    expect(await noName.text()).toContain('missing_name');

    // 2) Z imieniem i nazwiskiem — pełen flow przechodzi (inna data → brak kolizji ze slotem #1).
    const pick2 = await findSlot(request, seededTenant, 20);
    expect(pick2, 'powinien istnieć wolny slot (#2)').not.toBeNull();
    const hold2 = await createHold(request, seededTenant, pick2!.date, pick2!.slot);
    expect(hold2, 'hold #2 powinien się udać').not.toBeNull();

    const email = `named-${Date.now()}@e2e.test`;
    const withName = await request.post(
      `${API_URL}/api/booking/${SLUG}/public-appointment/${hold2!.appointmentId}/request-otp`,
      {
        data: {
          token: hold2!.lease.reservationToken,
          email,
          firstName: 'Anna',
          lastName: 'Kowalska',
        },
        failOnStatusCode: false,
      },
    );
    expect([200, 204]).toContain(withName.status());

    const otp = await api.getLastOtp(email);
    expect(otp?.code).toMatch(/^\d{6}$/);

    const verify = await request.post(
      `${API_URL}/api/booking/${SLUG}/public-appointment/${hold2!.appointmentId}/verify-otp`,
      {
        data: {
          token: hold2!.lease.reservationToken,
          otp: otp!.code,
          firstName: 'Anna',
          lastName: 'Kowalska',
        },
        failOnStatusCode: false,
      },
    );
    expect([200, 204]).toContain(verify.status());
  });
});
