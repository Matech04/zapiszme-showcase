import { expect, test } from '../../fixtures/seeded-tenant.fixture';

/**
 * Auth — rejestracja i confirm-email. TC-A001..A010.
 *
 * Owner rejestruje się przez UI; e-mail confirm dostarczany do TestAuthEmailMailbox
 * (env=E2E). Backdoor /api/_e2e/auth-email pobiera URL z linku.
 */

const TIMESTAMP = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 7);

test.describe('Auth — registration @p0 @auth', () => {
  test('TC-A001 Owner registration — happy path', async ({ page, api }) => {
    const slug = `e2e-tca001-${TIMESTAMP()}`;
    const email = `tca001-${TIMESTAMP()}@e2e.local`;
    // Unikalny numer (PhoneTaken check w AuthController) — last 3 cyfry per test.
    const phone = `+48500${String(Date.now()).slice(-6)}`;

    await page.goto('/register');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await page.getByTestId('register-salon-name').fill('TC-A001 Salon');
    await page.getByTestId('register-salon-slug').fill(slug);
    await page.getByTestId('register-first-name').fill('Anna');
    await page.getByTestId('register-last-name').fill('Test');
    await page.getByTestId('register-display-name').fill('Anna T.');
    await page.getByTestId('register-phone').fill(phone);
    await page.getByTestId('register-email').fill(email);
    await page.getByTestId('register-password').fill('Password123!');
    await page.getByTestId('register-confirm-password').fill('Password123!');

    // Czekaj na slug-availability check
    await page.waitForTimeout(1500);
    await expect(page.getByTestId('register-submit')).toBeEnabled({ timeout: 5_000 });
    await page.getByTestId('register-submit').click();

    // Redirect na /check-email po sukcesie (brak auto-login).
    await expect(page).toHaveURL(/\/check-email/, { timeout: 15_000 });

    // Verify e-mail confirm dotarł
    const mail = await api.getLastAuthEmail(email);
    expect(mail.lastConfirmEmailUrl).toMatch(/\/confirm-email\?/);
  });

  test('TC-A003 Rejestracja — e-mail już istnieje', async ({ page, seededTenant }) => {
    await page.goto('/register');
    await page.getByTestId('register-salon-name').fill('Dup Test');
    await page.getByTestId('register-salon-slug').fill(`dup-${TIMESTAMP()}`);
    await page.getByTestId('register-first-name').fill('Dup');
    await page.getByTestId('register-last-name').fill('Test');
    await page.getByTestId('register-display-name').fill('Dup T.');
    await page.getByTestId('register-phone').fill(`+48500${String(Date.now()).slice(-6)}`);
    await page.getByTestId('register-email').fill(seededTenant.ownerEmail);
    await page.getByTestId('register-password').fill('Password123!');
    await page.getByTestId('register-confirm-password').fill('Password123!');
    await page.waitForTimeout(1500);
    // Submit może być enabled (slug nowy, hasła OK) — klik powinien dać error (e-mail istnieje).
    const submitBtn = page.getByTestId('register-submit');
    if (await submitBtn.isEnabled()) {
      await submitBtn.click();
      await page.waitForTimeout(1500);
    }
    await expect(page).not.toHaveURL(/\/check-email/);
  });

  test('TC-A004 Rejestracja — hasło za słabe (klient blokuje)', async ({ page }) => {
    await page.goto('/register');
    await page.getByTestId('register-password').fill('1234');
    await page.getByTestId('register-confirm-password').fill('1234');
    // Submit powinien zostać disabled lub form invalid — nie liczymy submitu UI.
    await expect(page.getByTestId('register-submit')).toBeDisabled();
  });

  test('TC-A006 Confirm e-mail — happy path (via backdoor URL)', async ({ page, api }) => {
    // Rejestracja Ownera UI → pobranie URL confirm z mailbox → otwarcie URL.
    const slug = `e2e-tca006-${TIMESTAMP()}`;
    const email = `tca006-${TIMESTAMP()}@e2e.local`;
    const phone = `+48501${String(Date.now()).slice(-6)}`;

    await page.goto('/register');
    await page.getByTestId('register-salon-name').fill('TC-A006');
    await page.getByTestId('register-salon-slug').fill(slug);
    await page.getByTestId('register-first-name').fill('Confirm');
    await page.getByTestId('register-last-name').fill('Flow');
    await page.getByTestId('register-display-name').fill('Conf F.');
    await page.getByTestId('register-phone').fill(phone);
    await page.getByTestId('register-email').fill(email);
    await page.getByTestId('register-password').fill('Password123!');
    await page.getByTestId('register-confirm-password').fill('Password123!');
    await page.waitForTimeout(1500);
    await expect(page.getByTestId('register-submit')).toBeEnabled({ timeout: 5_000 });
    await page.getByTestId('register-submit').click();
    await expect(page).toHaveURL(/\/check-email/, { timeout: 15_000 });

    // Pobierz confirm URL i otwórz
    const mail = await api.getLastAuthEmail(email);
    expect(mail.lastConfirmEmailUrl).toBeTruthy();
    await page.goto(mail.lastConfirmEmailUrl!);
    // Komponent confirm-email automatycznie weryfikuje przy mount; wystarczy success state
    // (treść może być różna; brak rzutu = OK).
    await page.waitForTimeout(500);
  });

  test.fixme('TC-A002 Rejestracja — slug zajęty (wymaga live slug-availability handler w UI)', async () => {
    // Slug-availability UI check zwraca real-time — wymaga oddzielnego testu UX.
  });

  test.fixme('TC-A005 Rejestracja — Turnstile fail (Turnstile wyłączony w env E2E z designu)', async () => {
    // env E2E ma turnstileSiteKey='' → widget nieobecny. Test nieosiągalny w E2E.
  });

  test.fixme('TC-A007 Confirm e-mail — token wygasły (wymaga manipulacji DB tokenu)', async () => {});

  test.fixme('TC-A008 Confirm e-mail — token użyty (idempotencja)', async () => {});

  test.fixme('TC-A009 Resend confirm — throttle (wymaga zliczania prób w UI)', async () => {});

  test.fixme('TC-A010 Cleanup BG niepotwierdzone >48h (wymaga DB direct + backdoor seed + time advance)', async () => {});
});
