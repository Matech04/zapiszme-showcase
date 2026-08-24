import { test, expect } from '../../fixtures/owner-session.fixture';
import type { APIRequestContext, Page } from '@playwright/test';
import { LoginPage } from '../../pages/dashboard/LoginPage';
import type { E2eApi } from '../../helpers/api-client';

/**
 * Tryb stałych slotów — ustawianie grafiku w panelu (UI).
 * Owner przełącza pracownika na „Stałe godziny", wpisuje godziny startu dla dni roboczych,
 * zapisuje i weryfikujemy round-trip przez API (GET employee-schedules musi zwrócić tryb + godziny).
 * Następnie sprawdzamy, że available-slots faktycznie używa tych godzin.
 */

const WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'];

function isoIn(daysAhead: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysAhead);
  while (d.getUTCDay() === 0 || d.getUTCDay() === 6) d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

/** Logowanie przez reset hasła (jedyny deterministyczny sposób w env E2E — patrz swap-appointments). */
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
  const login = new LoginPage(page);
  await login.login(ownerEmail, 'Password123!');
  await page.waitForURL(/\/admin/, { timeout: 10_000 }).catch(() => {});
}

async function deleteAllSchedules(ownerApi: APIRequestContext, employeeId: string): Promise<void> {
  const res = await ownerApi.get(`/api/Employees/${employeeId}/employee-schedules`, { failOnStatusCode: false });
  if (!res.ok()) return;
  const list = (await res.json()) as Array<{ id: string }>;
  for (const s of list) {
    await ownerApi.delete(`/api/Employees/${employeeId}/employee-schedules/${s.id}`, { failOnStatusCode: false });
  }
}

interface ScheduleDay {
  cycleIndex: number;
  fixedStartTimes?: string[] | null;
  workRanges?: unknown[];
}
interface ScheduleDto {
  slotGenerationMode?: number | string;
  days: ScheduleDay[];
}

test.describe('Dashboard — grafik w trybie stałych slotów @p1 @availability', () => {
  test.afterAll(async ({ ownerApi, api, seededTenant }) => {
    // Sprzątanie: usuń grafiki fixed i przywróć grid (współdzielony tenant).
    await deleteAllSchedules(ownerApi, seededTenant.employeeId);
    await api.seedEmployeeSchedule(seededTenant.employeeId).catch(() => {});
  });

  test('TC-FS-D01 Owner ustawia tryb „Stałe godziny" w panelu i zapisuje (round-trip)', async ({
    page,
    api,
    ownerApi,
    seededTenant,
  }) => {
    // Czysty stan — brak grafiku, więc „nowy grafik" nie wpadnie w kolizję zakresów.
    await deleteAllSchedules(ownerApi, seededTenant.employeeId);

    await ensureLogin(page, api, seededTenant.ownerEmail);
    await page.goto(`/admin/resources/employees/${seededTenant.employeeId}/schedules/new`);

    // Edytor gotowy (przycisk zapisu widoczny).
    await expect(page.getByTestId('schedule-save')).toBeVisible({ timeout: 15_000 });

    // Przełącz na „Stałe godziny".
    await page.getByTestId('slot-mode-toggle').getByText('Stałe godziny').click();

    // Edytor przeszedł w tryb fixed (pojawiają się przyciski „Dodaj godzinę”).
    await expect(page.getByTestId('add-fixed-time-Monday-0')).toBeVisible({ timeout: 5_000 });

    // Dni robocze (pon-pt) są wstępnie włączone — w trybie fixed każdy musi mieć godzinę startu.
    for (const dayKey of WEEKDAYS) {
      await page.getByTestId(`add-fixed-time-${dayKey}-0`).click();
    }

    await page.getByTestId('schedule-save').click();
    await page.waitForLoadState('networkidle').catch(() => {});

    // Round-trip przez API: tryb + godziny faktycznie zapisane (regresja: GET musi zwracać fixed).
    await expect
      .poll(
        async () => {
          const res = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/employee-schedules`, {
            failOnStatusCode: false,
          });
          if (!res.ok()) return null;
          const schedules = (await res.json()) as ScheduleDto[];
          const fixed = schedules.find(
            (s) => s.slotGenerationMode === 1 || s.slotGenerationMode === 'FixedStartTimes',
          );
          if (!fixed) return null;
          const monday = fixed.days.find((d) => d.cycleIndex === 1);
          return monday?.fixedStartTimes?.some((t) => t.startsWith('12:00')) ? 'ok' : null;
        },
        { timeout: 10_000, intervals: [500, 1000, 1000, 2000] },
      )
      .toBe('ok');
  });

  test('TC-FS-D02 Po ustawieniu trybu available-slots zwraca tylko stałe godziny', async ({
    api,
    ownerApi,
    seededTenant,
  }) => {
    // Ten test nie zależy od UI — potwierdza efekt końcowy trybu fixed na slotach panelu.
    await api.seedEmployeeFixedSchedule(seededTenant.employeeId, ['10:00:00', '14:00:00']);

    const date = isoIn(7);
    const res = await ownerApi.get(
      `/api/Appointments/available-slots?employeeId=${seededTenant.employeeId}&serviceId=${seededTenant.serviceId}&date=${date}`,
      { failOnStatusCode: false },
    );
    expect(res.ok()).toBeTruthy();
    const slots = (await res.json()) as Array<{ slot?: string }>;
    expect(slots.map((s) => s.slot)).toEqual(['10:00', '14:00']);
  });
});
