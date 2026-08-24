import { expect, test } from '../../fixtures/admin-session.fixture';

/**
 * Sprint M — kod promocyjny w formularzu rejestracji. TC-A044, TC-A045.
 *
 * UI rejestracji (/register) ma collapsible section z polem `register-promo-code`.
 * Real-time walidacja przez POST /api/promo/validate (debounce 500ms) — UI pokazuje
 * sukces („Cena bazy: 49 zł...") lub błąd. Po submit registracji backend redeemuje
 * kod atomowo z utworzeniem tenant'a.
 */

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';
const UNIQUE = () => `${Date.now()}${Math.random().toString(36).slice(2, 5)}`;

test.describe('Auth — promo code in register UI @p0 @auth @promo @ui', () => {
  test('TC-A044 Promo code section toggleable + real-time validate (valid code)', async ({
    page,
    adminApi,
  }) => {
    // Pre: stwórz kod promocyjny przez admin
    const code = `E2E-UI-${UNIQUE()}`;
    const createRes = await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0, // AdminIssued
        discountType: 0, // PriceOverride
        discountValue: 49.0,
        maxUsesPerTenant: 1,
        appliesTo: 0, // NewTenantsOnly
      },
    });
    expect(createRes.ok()).toBeTruthy();

    await page.goto('/register');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // Sekcja jest collapsed — kliknij toggle „Masz kod promocyjny?". Astro/Svelte
    // hydruje wyspę asynchronicznie: pierwszy klik bywa zgubiony, zanim handler się
    // podepnie. Idempotentny re-klik (klikamy tylko gdy pole jeszcze niewidoczne)
    // jest odporny na ten wyścig i nie zwija sekcji z powrotem.
    const promoInput = page.getByTestId('register-promo-code');
    await expect(async () => {
      if (!(await promoInput.isVisible())) {
        await page.getByRole('button', { name: /kod promocyjny/i }).click();
      }
      await expect(promoInput).toBeVisible({ timeout: 1500 });
    }).toPass({ timeout: 12000 });

    await promoInput.fill(code);
    // Debounce 500ms + HTTP — daj 2s na walidację
    await page.waitForTimeout(2000);

    // UI pokazuje discountPreview (zawiera „49")
    const visibleText = await page.locator('body').textContent();
    expect(visibleText).toMatch(/49/);
  });

  test('TC-A045 Promo code — nieprawidłowy kod pokazuje błąd', async ({ page }) => {
    await page.goto('/register');

    const promoInput = page.getByTestId('register-promo-code');
    await expect(async () => {
      if (!(await promoInput.isVisible())) {
        await page.getByRole('button', { name: /kod promocyjny/i }).click();
      }
      await expect(promoInput).toBeVisible({ timeout: 1500 });
    }).toPass({ timeout: 12000 });

    await promoInput.fill(`NOPE-${UNIQUE()}`);
    await page.waitForTimeout(2000);

    // UI komunikuje błąd („Kod nieprawidłowy" lub „Nie udało się sprawdzić")
    const visibleText = await page.locator('body').textContent();
    expect(visibleText).toMatch(/nieprawidłow|Nie udało się/i);
  });
});

test.describe('Auth — register with promo code applied @p1 @auth @promo', () => {
  test('TC-A046 Rejestracja z PriceOverride 49zł kod → subscription baseMonthlyPriceInGrosze=4900', async ({
    request,
    adminApi,
  }) => {
    // Pre: stwórz kod
    const code = `E2E-APPLY-${UNIQUE()}`;
    await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0,
        discountType: 0, // PriceOverride
        discountValue: 49.0,
        maxUsesPerTenant: 1,
        appliesTo: 0,
      },
    });

    const slug = `tc-a046-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    // +48 + 9 cyfr (mobile PL). Slice(-7) na timestamp = ostatnie 7 z timestampu w ms,
    // bo 502 prefix to 3 cyfry + 6 z timestampu = 9 razem.
    const phone = `+48502${String(Date.now()).slice(-6)}`;

    const reg = await request.post(`${API_URL}/api/auth/register-owner`, {
      data: {
        salonName: 'TC-A046',
        salonSlug: slug,
        timeZoneId: 'Europe/Warsaw',
        currency: 'PLN',
        email,
        password: 'Password123!',
        displayName: 'TC-A046',
        firstName: 'Promo',
        lastName: 'Apply',
        phoneNumber: phone,
        turnstileToken: 'e2e-bypass',
        promoCode: code,
      },
    });
    if (!reg.ok()) {
      throw new Error(`Register failed ${reg.status()}: ${await reg.text()}`);
    }
    const userId = (await reg.json()).userId as string;

    // Owner GET /api/Subscription (przez integration-test auth) — cena powinna być zniżkowa.
    const subInfo = await request.get(`${API_URL}/api/Subscription`, {
      headers: {
        'X-Integration-Test-UserId': userId,
        'X-Integration-Test-Roles': 'Owner',
      },
    });
    expect(subInfo.ok()).toBeTruthy();
    const body = (await subInfo.json()) as {
      monthlyPriceInGrosze: number;
      baseMonthlyPriceInGrosze: number;
      activePromoCode: { code: string; discountType: string } | null;
    };
    // PriceOverride 49 zł = 4900 groszy
    expect(body.monthlyPriceInGrosze).toBe(4900);
    expect(body.activePromoCode).not.toBeNull();
    // PromoCode.NormalizeCode upperc'aseuje code w DB — case-insensitive porównanie.
    expect(body.activePromoCode?.code.toUpperCase()).toBe(code.toUpperCase());
  });
});
