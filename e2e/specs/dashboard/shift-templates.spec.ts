import { test, expect } from '../../fixtures/owner-session.fixture';
import { request as playwrightRequest } from '@playwright/test';
import { loginAsOwner as ensureLogin } from '../../helpers/dashboard-login';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Szablony zmian w trybie stałych godzin — tworzenie przez UI + round-trip przez API.
 * + widoczność karty „Szablony zmian" w „Mojej dostępności" wg roli (Owner/Manager vs Employee).
 */

interface TemplateDto {
  id?: string;
  name?: string;
  slotGenerationMode?: number | string;
  fixedStartTimes?: string[];
}

test.describe('Dashboard — szablony zmian (tryb stały) @p1 @templates', () => {
  const NAME = `E2E Stałe ${Date.now()}`;

  test.afterAll(async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/ShiftTemplates', { failOnStatusCode: false });
    if (!res.ok()) return;
    const list = (await res.json()) as TemplateDto[];
    for (const t of list.filter((x) => x.name === NAME && x.id)) {
      await ownerApi.delete(`/api/ShiftTemplates/${t.id}`, { failOnStatusCode: false }).catch(() => {});
    }
  });

  test('TC-TPL01 Owner tworzy szablon w trybie stałych godzin (round-trip)', async ({
    page,
    api,
    ownerApi,
    seededTenant,
  }) => {
    await ensureLogin(page, api, seededTenant.ownerEmail);
    await page.goto('/admin/resources/shift-templates/new');

    await expect(page.getByTestId('shift-template-save')).toBeVisible({ timeout: 15_000 });

    await page.getByTestId('shift-template-name').fill(NAME);
    await page.getByTestId('shift-template-mode').getByText('Stałe godziny').click();
    await page.getByTestId('shift-template-add-fixed-time').click();
    await page.getByTestId('shift-template-save').click();
    await page.waitForLoadState('networkidle').catch(() => {});

    await expect
      .poll(
        async () => {
          const res = await ownerApi.get('/api/ShiftTemplates', { failOnStatusCode: false });
          if (!res.ok()) return null;
          const list = (await res.json()) as TemplateDto[];
          const tpl = list.find((t) => t.name === NAME);
          if (!tpl) return null;
          const isFixed = tpl.slotGenerationMode === 1 || tpl.slotGenerationMode === 'FixedStartTimes';
          return isFixed && (tpl.fixedStartTimes ?? []).some((t) => t.startsWith('12:00')) ? 'ok' : null;
        },
        { timeout: 10_000, intervals: [500, 1000, 1000, 2000] },
      )
      .toBe('ok');
  });
});

/**
 * Feedback-fixes — karta „Szablony zmian" w „Mojej dostępności".
 *
 * Regresja: karta była ukryta w self-mode (`/admin/my-availability/:id`) dla każdego,
 * mimo że Owner/Manager mają dostęp do panelu szablonów. Teraz pokazujemy ją w self-mode
 * gdy rola = Owner/Manager (i ukrywamy dla Employee — chroni przed linkiem donikąd,
 * spójnie z `staffManagementGuard` na route `/admin/resources/shift-templates`).
 */
test.describe('Dashboard — karta szablonów w „Mojej dostępności" @p1 @templates', () => {
  test('TC-TPL02 Owner widzi kartę „Szablony zmian" w self-mode my-availability', async ({
    page,
    api,
    seededTenant,
  }) => {
    await ensureLogin(page, api, seededTenant.ownerEmail);

    await page.goto(`/admin/my-availability/${seededTenant.employeeId}`);
    // Karta ma stabilny testid + prowadzi do panelu szablonów.
    const card = page.getByTestId('availability-shift-templates-card');
    await expect(card).toBeVisible({ timeout: 15_000 });
    await expect(card).toHaveAttribute('href', /\/admin\/resources\/shift-templates/);
  });

  test('TC-TPL03 RBAC: Employee nie zarządza szablonami (StaffManagement na zapisie)', async ({
    seededTenant,
  }) => {
    // Karta dla Employee jest ukryta w UI; źródłem prawdy dla ACL jest API.
    // ShiftTemplatesController: GET = GeneralAccess (Employee OK, 200), ale POST/PUT/DELETE
    // = StaffManagement (Owner/Manager). Pracownik nie utworzy/usunie szablonu — to chroni
    // przed linkiem donikąd w „Mojej dostępności" (karta ukryta dla Employee).
    const ctx = await playwrightRequest.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: {
        'X-Integration-Test-UserId': seededTenant.ownerUserId,
        'X-Integration-Test-Roles': 'Employee',
      },
    });
    // GET jest dozwolony dla Employee (GeneralAccess).
    const getRes = await ctx.get('/api/ShiftTemplates', { failOnStatusCode: false });
    expect(getRes.status()).toBe(200);

    // Zapis (tu: DELETE losowego id) wymaga StaffManagement → 403 dla Employee.
    const writeRes = await ctx.delete('/api/ShiftTemplates/00000000-0000-0000-0000-000000000001', {
      failOnStatusCode: false,
    });
    await ctx.dispose();
    expect(writeRes.status()).toBe(403);
  });

  test('TC-TPL04 Menu wyboru szablonu ma klasę shift-template-menu (szersze)', async ({
    page,
    api,
    seededTenant,
  }) => {
    // Klasa styleClass="shift-template-menu" na p-menu poszerza popup wyboru szablonu.
    // Tu weryfikujemy że jest obecna w wyrenderowanym widoku tygodniowym (po otwarciu menu).
    await ensureLogin(page, api, seededTenant.ownerEmail);
    await page.goto(`/admin/my-availability/${seededTenant.employeeId}/schedules`);

    // Trigger „Wstaw szablon" w karcie dnia — pierwszy dostępny.
    const trigger = page.locator('[data-testid^="use-template-"]').first();
    const visible = await trigger.isVisible({ timeout: 8_000 }).catch(() => false);
    test.skip(!visible, 'Brak triggera szablonu w tym widoku (zależne od stanu grafiku) — pomijamy.');
    await trigger.click();
    await expect(page.locator('.shift-template-menu')).toBeVisible({ timeout: 5_000 });
  });
});
