import { test, expect } from '../../fixtures/seeded-tenant.fixture';
import type { APIRequestContext } from '@playwright/test';
import type { E2eSeedResult } from '../../helpers/api-client';

/**
 * Tryb stałych slotów — publiczna rezerwacja.
 * Pracownik ustawiony na FixedStartTimes (09:00/12:00/15:00, pon-pt) przez backdoor
 * /api/_e2e/seed-employee-fixed-schedule. Sprawdzamy:
 *  - available-slots zwraca DOKŁADNIE stałe godziny (bez siatki co 15 min),
 *  - hold poza slotem (09:30) jest odrzucony, na slocie przyjęty,
 *  - UI rezerwacji pokazuje tylko stałe godziny,
 *  - pełna rezerwacja online (OTP) na stałym slocie kończy się potwierdzeniem.
 */

const SLUG = 'rest-api-integration';
const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';
const WEB_URL = process.env.E2E_WEB_URL ?? 'http://localhost:4321';
const FIXED = ['09:00:00', '12:00:00', '15:00:00'];

/** Najbliższy dzień roboczy (pon-pt) co najmniej `daysAhead` dni w przód — grafik fixed jest pon-pt. */
function nextWeekdayIso(daysAhead: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysAhead);
  while (d.getUTCDay() === 0 || d.getUTCDay() === 6) d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

function availableSlots(request: APIRequestContext, seededTenant: E2eSeedResult, date: string) {
  return request.get(
    `${API_URL}/api/booking/${SLUG}/appointments/available-slots` +
      `?date=${date}&employeeId=${seededTenant.employeeId}&serviceId=${seededTenant.serviceId}`,
    { failOnStatusCode: false },
  );
}

function postHold(request: APIRequestContext, seededTenant: E2eSeedResult, date: string, startTime: string) {
  return request.post(`${API_URL}/api/booking/${SLUG}/public-appointment/hold`, {
    data: { serviceId: seededTenant.serviceId, employeeId: seededTenant.employeeId, date, startTime },
    failOnStatusCode: false,
  });
}

/** Przechodzi flow do listy slotów dla danej daty (usługa → pracownik → dzień). */
async function openSlotsForDate(
  page: import('@playwright/test').Page,
  seededTenant: E2eSeedResult,
  dateIso: string,
): Promise<void> {
  // Bezwzględny WEB_URL — public-booking spec bywa uruchamiany też w projekcie chromium-dashboard.
  await page.goto(`${WEB_URL}/${SLUG}`);
  await expect(page.getByTestId('booking-entry-mode-toggle')).toBeVisible({ timeout: 15_000 });

  const service = page.getByTestId(`booking-service-${seededTenant.serviceId}`);
  await service.waitFor({ state: 'visible', timeout: 10_000 });
  await service.click();

  const employee = page.getByTestId(`booking-employee-${seededTenant.employeeId}`).first();
  if (await employee.isVisible().catch(() => false)) {
    await employee.click();
  }

  const day = page.getByTestId(`booking-day-${dateIso}`);
  await day.waitFor({ state: 'visible', timeout: 10_000 });
  await day.click();
}

test.describe('Public booking — stałe sloty @p1 @public-booking', () => {
  test.beforeEach(async ({ api, seededTenant }) => {
    await api.resetTime().catch(() => {});
    await api.seedEmployeeFixedSchedule(seededTenant.employeeId, FIXED);
  });

  test.afterAll(async ({ api, seededTenant }) => {
    // Przywróć grid — współdzielony tenant, inne specy zakładają tryb siatki.
    await api.seedEmployeeSchedule(seededTenant.employeeId).catch(() => {});
  });

  test('TC-FS-API01 available-slots zwraca dokładnie stałe godziny (bez siatki)', async ({ request, seededTenant }) => {
    const date = nextWeekdayIso(7);
    const res = await availableSlots(request, seededTenant, date);
    expect(res.ok()).toBeTruthy();
    const slots = (await res.json()) as Array<{ slot?: string; isPreferred?: boolean }>;
    expect(slots.map((s) => s.slot)).toEqual(['09:00', '12:00', '15:00']);
    expect(slots.every((s) => s.isPreferred === false)).toBeTruthy();
  });

  test('TC-FS-API02 hold poza slotem odrzucony (400), na slocie przyjęty', async ({ request, seededTenant }) => {
    const date = nextWeekdayIso(8);
    const off = await postHold(request, seededTenant, date, '09:30:00');
    expect([400, 409]).toContain(off.status());

    const on = await postHold(request, seededTenant, date, '12:00:00');
    expect([200, 201]).toContain(on.status());
  });

  test('TC-FS-UI01 strona rezerwacji pokazuje tylko stałe godziny', async ({ page, seededTenant }) => {
    const date = nextWeekdayIso(3);
    await openSlotsForDate(page, seededTenant, date);

    // Dostępne dokładnie 09:00 / 12:00 / 15:00 …
    await expect(page.getByTestId('booking-slot-09:00')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('booking-slot-12:00')).toBeVisible();
    await expect(page.getByTestId('booking-slot-15:00')).toBeVisible();

    // … i ŻADNYCH slotów z siatki co 15 min.
    await expect(page.getByTestId('booking-slot-09:15')).toHaveCount(0);
    await expect(page.getByTestId('booking-slot-09:30')).toHaveCount(0);
    await expect(page.getByTestId('booking-slot-10:00')).toHaveCount(0);
  });

  test('TC-FS-UI02 pełna rezerwacja online na stałym slocie (OTP → potwierdzenie)', async ({ page, api, seededTenant }) => {
    const date = nextWeekdayIso(4);
    await openSlotsForDate(page, seededTenant, date);

    await page.getByTestId('booking-slot-09:00').click();
    await page.getByTestId('booking-footer-primary').click();

    const email = `fixed-${Date.now()}@e2e.test`;
    await page.getByTestId('booking-otp-contact').fill(email);
    await page.getByTestId('booking-otp-consent').check();
    await page.getByTestId('booking-otp-send').click();

    const otp = await api.getLastOtp(email);
    expect(otp?.code).toMatch(/^\d{6}$/);

    await page.getByTestId('booking-otp-code').fill(otp!.code);
    await page.getByTestId('booking-otp-verify').click();

    await expect(page.getByText(/potwierdzona|zarezerwowana|dziękujemy/i)).toBeVisible({ timeout: 10_000 });
  });
});
