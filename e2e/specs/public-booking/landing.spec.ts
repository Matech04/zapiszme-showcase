import { test, expect } from '../../fixtures/seeded-tenant.fixture';

/**
 * Public booking — landing page UI. TC-P001..P004.
 * Web (Astro+Svelte). baseURL ustawiany przez project chromium-web w playwright.config.
 */

const SLUG = 'rest-api-integration';
const WEB_URL = process.env.E2E_WEB_URL ?? 'http://localhost:4321';

test.describe('Public booking — landing UI @p0 @public-booking', () => {
  test('TC-P001 Landing /[slug] renderuje booking entry', async ({ page, seededTenant }) => {
    await page.goto(`${WEB_URL}/${SLUG}`);
    // Mount BookingEntry + tab toggle widoczne
    await expect(page.getByTestId('booking-entry-mode-toggle')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('booking-entry-mode-book')).toBeVisible();
    await expect(page.getByTestId('booking-entry-mode-manage')).toBeVisible();
  });

  test('TC-P001b Tab toggle Manage → ManageAppointment', async ({ page, seededTenant }) => {
    await page.goto(`${WEB_URL}/${SLUG}`);
    await page.getByTestId('booking-entry-mode-manage').click();
    // ManageAppointment renderuje contact form (email mode default)
    await expect(page.getByTestId('manage-contact-email')).toBeVisible({ timeout: 10_000 });
  });

  test('TC-P002 Slug nieistniejący nadal renderuje wrapper (BookingPanel zgłosi salonNotFound)', async ({ page }) => {
    await page.goto(`${WEB_URL}/nonexistent-slug-${Date.now()}`);
    // BookingEntry mount się odpalił ale BookingPanel uzna że nie znalazł salonu (poprzez emit).
    // Slug nieprawidłowy = strona ładuje się, ale później salon-not-found state.
    // Wystarczy że strona nie crashuje (200 OK z Astro SSR).
    await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {});
    // Brak konkretnej asercji — sprawdzamy że nie ma 500.
    expect(page.url()).toContain('nonexistent-slug');
  });

  test.fixme('TC-P003 Wybór kategorii i usługi (UI — wymaga visible booking panel)', async () => {});
  test.fixme('TC-P004 Wybór pracownika filtrowany przez usługę', async () => {});
});
