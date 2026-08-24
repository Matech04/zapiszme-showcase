import { test, expect } from '../../fixtures/owner-session.fixture';
import type { Page } from '@playwright/test';
import { LoginPage } from '../../pages/dashboard/LoginPage';
import type { E2eApi } from '../../helpers/api-client';

/**
 * „Ustaw godziny na ten dzień" w trybie stałych godzin (wyjątek/override per-dzień).
 * Pracownik globalnie w trybie siatki; owner ustawia dla pojedynczego dnia STAŁE godziny przez UI.
 * Weryfikacja: round-trip przez API (override fixed) ORAZ available-slots tego dnia = wpisane godziny
 * (per-dzień tryb wygrywa z globalnym trybem pracownika).
 */

function isoWeekdayAhead(daysAhead: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysAhead);
  while (d.getUTCDay() === 0 || d.getUTCDay() === 6) d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

async function ensureLogin(page: Page, api: E2eApi, ownerEmail: string): Promise<void> {
  await page.goto('/forgot-password');
  await page.getByTestId('forgot-email').fill(ownerEmail);
  await page.getByTestId('forgot-submit').click();
  await page.waitForTimeout(800);
  const mail = await api.getLastAuthEmail(ownerEmail);
  if (mail.lastPasswordResetUrl) {
    await page.goto(mail.lastPasswordResetUrl);
    await page.getByTestId('reset-password-input').fill('Password123!');
    await page.getByTestId('reset-password-submit').click();
    await page.waitForTimeout(800);
  }
  await new LoginPage(page).login(ownerEmail, 'Password123!');
  await page.waitForURL(/\/admin/, { timeout: 10_000 }).catch(() => {});
}

interface OverrideDto {
  date?: string;
  slotGenerationMode?: number | string;
  fixedStartTimes?: string[];
}

test.describe('Dashboard — „Ustaw godziny na ten dzień" (wyjątek stały) @p1 @availability', () => {
  const date = isoWeekdayAhead(8);

  test.afterAll(async ({ ownerApi, seededTenant }) => {
    await ownerApi
      .delete(`/api/Employees/${seededTenant.employeeId}/schedule-overrides/${date}`, { failOnStatusCode: false })
      .catch(() => {});
  });

  test('TC-FS-EX01 Owner ustawia stałe godziny dla jednego dnia (round-trip + sloty)', async ({
    page,
    api,
    ownerApi,
    seededTenant,
  }) => {
    // Baza: pracownik w trybie siatki (grid) — żeby udowodnić tryb per-dzień.
    await api.seedEmployeeSchedule(seededTenant.employeeId).catch(() => {});

    await ensureLogin(page, api, seededTenant.ownerEmail);
    await page.goto(`/admin/resources/employees/${seededTenant.employeeId}/special-days?date=${date}`);

    await expect(page.getByTestId('special-day-date')).toBeVisible({ timeout: 15_000 });

    // Przełącz ten dzień na „Stałe godziny".
    await page.getByTestId('special-day-mode').getByText('Stałe godziny').click();

    // Dodaj godzinę startu (domyślnie 12:00) i zapisz.
    await page.getByTestId('special-day-add-fixed-time').click();
    await page.getByTestId('special-day-save').click();
    await page.waitForLoadState('networkidle').catch(() => {});

    // Round-trip: override zapisany w trybie stałym z godziną 12:00.
    await expect
      .poll(
        async () => {
          const res = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/schedule-overrides`, {
            failOnStatusCode: false,
          });
          if (!res.ok()) return null;
          const list = (await res.json()) as OverrideDto[];
          const ovr = list.find((o) => (o.date ?? '').startsWith(date));
          if (!ovr) return null;
          const isFixed = ovr.slotGenerationMode === 1 || ovr.slotGenerationMode === 'FixedStartTimes';
          return isFixed && (ovr.fixedStartTimes ?? []).some((t) => t.startsWith('12:00')) ? 'ok' : null;
        },
        { timeout: 10_000, intervals: [500, 1000, 1000, 2000] },
      )
      .toBe('ok');

    // Per-dzień: available-slots tego dnia = stałe godziny z wyjątku (mimo globalnego trybu siatki).
    const res = await ownerApi.get(
      `/api/Appointments/available-slots?employeeId=${seededTenant.employeeId}&serviceId=${seededTenant.serviceId}&date=${date}`,
      { failOnStatusCode: false },
    );
    expect(res.ok()).toBeTruthy();
    const slots = (await res.json()) as Array<{ slot?: string }>;
    expect(slots.map((s) => s.slot)).toEqual(['12:00']);
  });
});
