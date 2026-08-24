import { test, expect } from '../../fixtures/seeded-tenant.fixture';
import { request as playwrightRequest, type APIRequestContext, type APIResponse } from '@playwright/test';
import type { E2eSeedResult } from '../../helpers/api-client';

/**
 * Sprint F: pełny booking flow + hold lease expiry + OTP.
 * TC-P011, TC-P015-019, TC-P024, TC-P025, TC-P012.
 *
 * Hold idzie przez /public-appointment/hold (serwer wystawia AnonSessionId w cookie,
 * Turnstile w env E2E przepuszcza). Sloty dobieramy z /available-slots — grafik to
 * Mon-Fri, więc losowa data potrafiłaby trafić w dzień bez dostępności.
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

function postHold(
  request: APIRequestContext,
  seededTenant: E2eSeedResult,
  date: string,
  slot: string,
): Promise<APIResponse> {
  return request.post(`${API_URL}/api/booking/${SLUG}/public-appointment/hold`, {
    data: {
      serviceId: seededTenant.serviceId,
      employeeId: seededTenant.employeeId,
      date,
      startTime: slot,
    },
    failOnStatusCode: false,
  });
}

test.describe('Public booking — full flow + TTL @p0 @public-booking', () => {
  // Reset FakeTimeProvider przed każdym testem — chroni przed sequence interference
  // (TC-P011 advance 10s pozostawiało wszystkie kolejne hold-y wygasłe).
  test.beforeEach(async ({ api }) => {
    await api.resetTime().catch(() => {});
  });

  test('TC-P009 Public POST appointment (hold) — happy path z seed schedule', async ({ request, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const pick = await findSlot(request, seededTenant, 2);
    expect(pick, 'powinien istnieć wolny slot').not.toBeNull();

    const res = await postHold(request, seededTenant, pick!.date, pick!.slot);
    expect([200, 201]).toContain(res.status());
    const hold = (await res.json()) as Hold;
    expect(hold.appointmentId).toMatch(/^[0-9a-f-]{36}$/);
    expect(hold.lease?.reservationToken).toBeTruthy();
  });

  test('TC-P015/P017 Pełen OTP flow: hold → request-otp → verify-otp → booking', async ({ request, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const pick = await findSlot(request, seededTenant, 30);
    expect(pick, 'powinien istnieć wolny slot').not.toBeNull();

    // 1) Public hold (serwer wystawia AnonSessionId w cookie)
    const holdRes = await postHold(request, seededTenant, pick!.date, pick!.slot);
    expect(holdRes.ok()).toBeTruthy();
    const hold = (await holdRes.json()) as Hold;
    expect(hold.appointmentId).toMatch(/^[0-9a-f-]{36}$/);
    expect(hold.lease?.reservationToken).toBeTruthy();

    // 2) Request OTP (seedowany tenant ma kanał Email)
    const email = `otpflow-${Date.now()}@e2e.test`;
    const otpReqRes = await request.post(
      `${API_URL}/api/booking/${SLUG}/public-appointment/${hold.appointmentId}/request-otp`,
      {
        data: { token: hold.lease.reservationToken, email },
        failOnStatusCode: false,
      },
    );
    expect([200, 204]).toContain(otpReqRes.status());

    // 3) Pobierz kod OTP z mailbox
    const otp = await api.getLastOtp(email);
    expect(otp?.code).toMatch(/^\d{6}$/);

    // 4) Verify OTP → wizyta potwierdzona
    const verifyRes = await request.post(
      `${API_URL}/api/booking/${SLUG}/public-appointment/${hold.appointmentId}/verify-otp`,
      {
        data: { token: hold.lease.reservationToken, otp: otp!.code },
        failOnStatusCode: false,
      },
    );
    expect([200, 204]).toContain(verifyRes.status());
  });

  test('TC-P018 OTP verify — błędny kod zwraca błąd', async ({ request, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const pick = await findSlot(request, seededTenant, 45);
    expect(pick, 'powinien istnieć wolny slot').not.toBeNull();

    const holdRes = await postHold(request, seededTenant, pick!.date, pick!.slot);
    expect(holdRes.ok()).toBeTruthy();
    const hold = (await holdRes.json()) as Hold;

    // Wymagamy najpierw request-otp żeby był aktywny token.
    await request.post(`${API_URL}/api/booking/${SLUG}/public-appointment/${hold.appointmentId}/request-otp`, {
      data: { token: hold.lease.reservationToken, email: `bad-${Date.now()}@e2e.test` },
      failOnStatusCode: false,
    });

    const badRes = await request.post(`${API_URL}/api/booking/${SLUG}/public-appointment/${hold.appointmentId}/verify-otp`, {
      data: { token: hold.lease.reservationToken, otp: '000000' },
      failOnStatusCode: false,
    });
    expect([400, 422]).toContain(badRes.status());
  });

  test('TC-P011 Hold lease expiry — slot zwolniony po przekroczeniu HoldTtl', async ({ request, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const pick = await findSlot(request, seededTenant, 60);
    expect(pick, 'powinien istnieć wolny slot').not.toBeNull();

    // Hold #1
    const hold1Res = await postHold(request, seededTenant, pick!.date, pick!.slot);
    expect(hold1Res.ok()).toBeTruthy();

    // Przesunięcie czasu o 10s — większe niż HoldTtlSeconds=5 z appsettings.E2E.json
    await api.advanceTime(10);

    // BG status-lifecycle usuwa Pending po expiry
    await api.runBgStatusLifecycle();

    // Hold #2 na ten sam slot — powinien się udać (slot zwolniony po expiry / auto-cancel sesji).
    const hold2Res = await postHold(request, seededTenant, pick!.date, pick!.slot);
    expect([200, 201]).toContain(hold2Res.status());
  });

  test('TC-P012 Slot squat — dwa hold tego samego slotu (drugi powinien zostać odrzucony)', async ({ request, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const pick = await findSlot(request, seededTenant, 75);
    expect(pick, 'powinien istnieć wolny slot').not.toBeNull();

    const hold1Res = await postHold(request, seededTenant, pick!.date, pick!.slot);
    expect(hold1Res.ok()).toBeTruthy();

    // Drugi hold tego samego slotu w osobnym kontekście (inna anonimowa sesja → brak auto-cancel).
    const ctx2 = await playwrightRequest.newContext();
    const hold2Res = await postHold(ctx2, seededTenant, pick!.date, pick!.slot);
    await ctx2.dispose();

    // Drugi hold powinien być odrzucony (konflikt slotu).
    expect([400, 409]).toContain(hold2Res.status());
  });

  test('TC-P025 Past-date booking blocked (API direct)', async ({ request, seededTenant }) => {
    const yesterday = new Date();
    yesterday.setUTCDate(yesterday.getUTCDate() - 1);
    const res = await postHold(
      request,
      seededTenant,
      yesterday.toISOString().slice(0, 10),
      '10:00:00',
    );
    expect([400, 409]).toContain(res.status());
  });

  test.fixme('TC-P016 OTP request rate-limit (wymaga zliczania w env E2E)', async () => {});
  test.fixme('TC-P019 OTP verify kod wygasły (wymaga clock.advance + state check)', async () => {});
  test.fixme('TC-P024 Anti-abuse drugie hold tego samego AnonSessionId (wymaga cookie session)', async () => {});
});
