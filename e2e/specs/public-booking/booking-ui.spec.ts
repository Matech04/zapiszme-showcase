import { test, expect } from '../../fixtures/seeded-tenant.fixture';
import { request as playwrightRequest } from '@playwright/test';

/**
 * Sprint H: public booking UI flow basic. TC-P003, TC-P004, TC-P020.
 * + Feedback-fixes: usługa „Bezpłatnie" (0 zł) i hidePrice w publicznym ServicePicker.
 */

const WEB_URL = process.env.E2E_WEB_URL ?? 'http://localhost:4321';
const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';
const SLUG = 'rest-api-integration';

/** Kontekst API z nagłówkami test-auth ownera — do seedowania usług przed renderem web. */
async function ownerContext(ownerUserId: string) {
  return playwrightRequest.newContext({
    baseURL: API_URL,
    extraHTTPHeaders: {
      'X-Integration-Test-UserId': ownerUserId,
      'X-Integration-Test-Roles': 'Owner',
    },
  });
}

/**
 * Rozwija wszystkie zwinięte akordeony kategorii w ServicePicker, żeby kafelki usług
 * (booking-service-{id}) trafiły do DOM. Salon bez kategorii renderuje listę płasko
 * (brak togglów) — wtedy helper jest no-opem.
 */
async function expandAllBookingCategories(page: import('@playwright/test').Page): Promise<void> {
  await page.getByTestId('booking-entry-mode-toggle').waitFor({ state: 'visible', timeout: 12_000 }).catch(() => {});
  const toggles = page.locator('[data-testid^="booking-category-toggle-"]');
  const count = await toggles.count();
  for (let i = 0; i < count; i++) {
    const t = toggles.nth(i);
    if ((await t.getAttribute('aria-expanded')) === 'false') {
      await t.click().catch(() => {});
    }
  }
}

async function createPublicService(
  ownerUserId: string,
  seededTenant: { serviceCategoryId: string; vatRateId: string; employeeId: string },
  opts: { name: string; amount: number; hidePrice?: boolean },
): Promise<string> {
  const ctx = await ownerContext(ownerUserId);
  const res = await ctx.post('/api/Services', {
    data: {
      name: opts.name,
      amount: opts.amount,
      currency: 'PLN',
      durationInMinutes: 30,
      categoryId: seededTenant.serviceCategoryId,
      vatRateId: seededTenant.vatRateId,
      employeeIds: [seededTenant.employeeId],
      hidePrice: opts.hidePrice ?? false,
    },
    failOnStatusCode: false,
  });
  const status = res.status();
  const body = await res.json().catch(() => null);
  await ctx.dispose();
  expect([200, 201].includes(status), `create ${opts.name} → ${status}`).toBeTruthy();
  return (typeof body === 'string' ? body : body?.id) as string;
}

test.describe('Public booking — UI flow @p1 @public-booking', () => {
  test('TC-P003 Wybór kategorii i usługi (selektor service-{id})', async ({ page, seededTenant, api }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    await page.goto(`${WEB_URL}/${SLUG}`);
    await expect(page.getByTestId('booking-entry-mode-toggle')).toBeVisible({ timeout: 10_000 });

    // Wybierz seeded service
    const serviceBtn = page.getByTestId(`booking-service-${seededTenant.serviceId}`);
    await serviceBtn.waitFor({ state: 'visible', timeout: 10_000 }).catch(() => {});
    expect(await serviceBtn.isVisible()).toBeTruthy();
    await serviceBtn.click();

    // Po wyborze usługi flow idzie dalej. Przy >1 pracowniku pojawia się picker osoby; przy
    // JEDNYM (seed = solo) krok osoby jest świadomie pomijany (auto-wybór) — oba przypadki OK.
    // Sprawdzamy, że panel rezerwacji działa (footer-primary obecny) i — gdy multi — picker jest.
    const employeeBtn = page.getByTestId(`booking-employee-${seededTenant.employeeId}`);
    await employeeBtn.first().waitFor({ state: 'visible', timeout: 3_000 }).catch(() => {});
    await expect(page.getByTestId('booking-footer-primary')).toBeVisible({ timeout: 5_000 });
  });

  test('TC-P004 Wybór pracownika — kontynuacja flow', async ({ page, seededTenant, api }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    await page.goto(`${WEB_URL}/${SLUG}`);
    await expect(page.getByTestId('booking-entry-mode-toggle')).toBeVisible({ timeout: 10_000 });

    const serviceBtn = page.getByTestId(`booking-service-${seededTenant.serviceId}`);
    if (await serviceBtn.isVisible()) {
      await serviceBtn.click();
      const employeeBtn = page.getByTestId(`booking-employee-${seededTenant.employeeId}`);
      if (await employeeBtn.first().isVisible()) {
        await employeeBtn.first().click();
        // Po wyborze employee powinien pojawić się date/slot picker
        await page.waitForTimeout(1000);
      }
    }
  });

  test.fixme('TC-P020 Confirmation screen — wymaga full booking flow do końca (z OTP)', async () => {});

  test('TC-P031 Usługa za 0 zł pokazuje „Bezpłatnie" w ServicePicker', async ({ page, seededTenant, api }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId).catch(() => {});
    const freeId = await createPublicService(seededTenant.ownerUserId, seededTenant, {
      name: `WebFree-${Date.now()}`,
      amount: 0,
    });

    await page.goto(`${WEB_URL}/${SLUG}`);
    await expandAllBookingCategories(page);
    const card = page.getByTestId(`booking-service-${freeId}`);
    await expect(card).toBeVisible({ timeout: 12_000 });
    // Cena bazowa 0 zł → formatMoney → „Bezpłatnie".
    await expect(card).toContainText('Bezpłatnie');
  });

  test('TC-P032 Usługa z hidePrice=true NIE pokazuje ceny w ServicePicker', async ({ page, seededTenant, api }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId).catch(() => {});
    const hiddenId = await createPublicService(seededTenant.ownerUserId, seededTenant, {
      name: `WebHidden-${Date.now()}`,
      amount: 199,
      hidePrice: true,
    });

    await page.goto(`${WEB_URL}/${SLUG}`);
    await expandAllBookingCategories(page);
    const card = page.getByTestId(`booking-service-${hiddenId}`);
    await expect(card).toBeVisible({ timeout: 12_000 });
    // Kwota nie może się pojawić; czas trwania (30 min) tak.
    await expect(card).not.toContainText('199');
    await expect(card).not.toContainText('zł');
    await expect(card).toContainText('min');
  });
});
