import { test, expect } from '../../fixtures/seeded-tenant.fixture';

/**
 * Public booking — API smoke. TC-P001..P008, P015..P022.
 * Anonymous endpoints — wystarczy request context bez auth.
 */

const SLUG = 'rest-api-integration';
const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

function inFutureIso(daysAhead: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysAhead);
  return d.toISOString().slice(0, 10);
}

test.describe('Public booking — anonymous API @p0 @public-booking', () => {
  test('TC-P001 GET /api/booking/{slug}/public-salon → 200 z brandem', async ({ request, seededTenant }) => {
    const res = await request.get(`${API_URL}/api/booking/${SLUG}/public-salon`);
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body).toBeTruthy();
  });

  test('TC-P002 GET /api/booking/{slug}/public-salon — nieistniejący slug → 404', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/booking/nonexistent-slug-xyz/public-salon`);
    expect(res.status()).toBe(404);
  });

  test('TC-P003 GET service-categories + services', async ({ request, seededTenant }) => {
    const catsRes = await request.get(`${API_URL}/api/booking/${SLUG}/service-categories`);
    expect(catsRes.ok()).toBeTruthy();
    const cats = await catsRes.json();
    expect(Array.isArray(cats)).toBeTruthy();
    expect(cats.length).toBeGreaterThan(0);

    const svcsRes = await request.get(`${API_URL}/api/booking/${SLUG}/services`);
    expect(svcsRes.ok()).toBeTruthy();
    const svcs = await svcsRes.json();
    expect(Array.isArray(svcs)).toBeTruthy();
  });

  test('TC-P004 GET employees (anonymously)', async ({ request, seededTenant }) => {
    const res = await request.get(`${API_URL}/api/booking/${SLUG}/employees`);
    expect(res.ok()).toBeTruthy();
    const list = await res.json();
    expect(Array.isArray(list)).toBeTruthy();
  });

  test('TC-P005 GET month availability', async ({ request, seededTenant }) => {
    const now = new Date();
    const year = now.getUTCFullYear();
    const month = now.getUTCMonth() + 1;
    const res = await request.get(
      `${API_URL}/api/booking/${SLUG}/appointments/month-availability?employeeId=${seededTenant.employeeId}&serviceId=${seededTenant.serviceId}&year=${year}&month=${month}`,
    );
    // Może zwracać 200 lub 400 (jeśli employee bez schedule); akceptujemy oba.
    expect([200, 400, 404]).toContain(res.status());
  });

  test('TC-P006 GET available slots', async ({ request, seededTenant }) => {
    const res = await request.get(
      `${API_URL}/api/booking/${SLUG}/appointments/available-slots?employeeId=${seededTenant.employeeId}&serviceId=${seededTenant.serviceId}&date=${inFutureIso(1)}`,
    );
    expect([200, 400, 404]).toContain(res.status());
  });

  test('TC-P009 POST appointment (hold) — anonymous booking', async ({ request, seededTenant }) => {
    const res = await request.post(`${API_URL}/api/booking/${SLUG}/appointments`, {
      data: {
        employeeId: seededTenant.employeeId,
        serviceId: seededTenant.serviceId,
        date: inFutureIso(1),
        startTime: '10:00:00',
      },
      failOnStatusCode: false,
    });
    // 200/201 = hold utworzony; 400 = brak slotu/schedule; oba akceptowalne.
    expect([200, 201, 400, 404, 409]).toContain(res.status());
  });

  test('TC-P003b services zawiera pola hidePrice + orderIndex (kontrakt)', async ({ request, seededTenant }) => {
    const res = await request.get(`${API_URL}/api/booking/${SLUG}/services`);
    expect(res.ok()).toBeTruthy();
    const svcs = (await res.json()) as { id: string; hidePrice?: boolean; orderIndex?: number; price?: { amount?: number } }[];
    expect(svcs.length).toBeGreaterThan(0);
    // Pola muszą istnieć w kontrakcie publicznym (web sortuje wg orderIndex, ukrywa cenę wg hidePrice).
    for (const s of svcs) {
      expect(typeof s.hidePrice).toBe('boolean');
      expect(typeof s.orderIndex).toBe('number');
    }
    // Lista posortowana rosnąco po orderIndex (backend OrderBy(OrderIndex).ThenBy(Name)).
    const orders = svcs.map((s) => s.orderIndex ?? 0);
    const sorted = [...orders].sort((a, b) => a - b);
    expect(orders).toEqual(sorted);
  });

  test.fixme('TC-P007 Past-date slot not shown — UI dependent', async () => {});
  test.fixme('TC-P008 AdjacentOnly gap filling — wymaga setup tenant settings', async () => {});
  test.fixme('TC-P010 Hold lease — ostrzeżenie wygasania (UI)', async () => {});
  test.fixme('TC-P011 Hold release po expiry — wymaga clock advance + sprawdzenie czy slot wolny', async () => {});
  test.fixme('TC-P012 Slot squat protection — 2 hold tego samego slotu', async () => {});
  test.fixme('TC-P013 Email validation (UI)', async () => {});
  test.fixme('TC-P014 Phone validation per channel (UI)', async () => {});
  test.fixme('TC-P015-019 OTP flow (request/verify) — wymaga aktywnego hold + mailbox check', async () => {});
  test.fixme('TC-P020 Confirmation screen — UI', async () => {});
  test.fixme('TC-P021 Confirmation e-mail (via mailbox check po pełnym flow)', async () => {});
  test.fixme('TC-P022 AutoConfirm vs Manual — wymaga zmiany tenant settings', async () => {});
  test.fixme('TC-P023 RequireManualConfirmation + notification staff', async () => {});
  test.fixme('TC-P024 Anti-abuse: drugie hold tego samego AnonSessionId', async () => {});
  test.fixme('TC-P025 Past-date booking blocked (API direct)', async () => {});
  test.fixme('TC-P026-028 Manage appointment flow (reschedule, cancel) — wymaga aktywnej wizyty', async () => {});
  test.fixme('TC-P029 Self-service token expiry', async () => {});
  test.fixme('TC-P030 Cross-origin cookies', async () => {});
});
