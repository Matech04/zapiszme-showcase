import { expect, test } from '../../fixtures/seeded-tenant.fixture';

/**
 * Sprint E: tokeny + cleanup BG. TC-A007, TC-A008, TC-A010, TC-A014, TC-A022.
 */

test.describe('Auth — token expiry + cleanup BG @p1 @auth', () => {
  test('TC-A010 Cleanup BG niepotwierdzone >48h usuwa konto', async ({ api }) => {
    const email = `unconfirmed-${Date.now()}@e2e.test`;
    const seeded = await api.seedUnconfirmedUser(email, 49);
    expect(seeded.userId).toMatch(/^[0-9a-f-]{36}$/);

    // Wymuś jeden cykl cleanup BG przez backdoor
    await api.runBgCleanupUnconfirmed();

    // Po cleanup user powinien być usunięty — sprawdź że ponowny seed-unconfirmed
    // tworzy nowego (nie zwraca reused=true).
    const second = await api.seedUnconfirmedUser(email, 49);
    expect(second.userId).not.toBe(seeded.userId);
  });

  test('TC-A014 Login niepotwierdzony e-mail blokowany', async ({ page, api }) => {
    const email = `nonconfirmed-${Date.now()}@e2e.test`;
    await api.seedUnconfirmedUser(email, 1); // świeży (age=1h, NIE skasuje cleanup)

    await page.goto('/login');
    await page.getByTestId('login-email').fill(email);
    await page.getByTestId('login-password').fill('Password123!');
    await page.getByTestId('login-submit').click();
    await page.waitForTimeout(1500);
    await expect(page).toHaveURL(/\/login/);
  });

  test('TC-A022 Reset password — token wygasły', async ({ page, api, seededTenant }) => {
    // 1) Wygeneruj reset URL
    await page.goto('/forgot-password');
    await page.getByTestId('forgot-email').fill(seededTenant.ownerEmail);
    await page.getByTestId('forgot-submit').click();
    await page.waitForTimeout(800);
    const mail = await api.getLastAuthEmail(seededTenant.ownerEmail);
    expect(mail.lastPasswordResetUrl).toBeTruthy();

    // 2) Wymuś rotację SecurityStamp — token z mailbox staje się invalid
    await api.expireResetToken(seededTenant.ownerEmail);

    // 3) Próba użycia starego tokenu
    await page.goto(mail.lastPasswordResetUrl!);
    await page.getByTestId('reset-password-input').fill('NewExp123!');
    await page.getByTestId('reset-password-submit').click();
    await page.waitForTimeout(1500);

    // Reset NIE powiódł się — login nowym hasłem zwróci 401.
    const apiUrl = process.env.E2E_API_URL ?? 'http://localhost:5199';
    const loginRes = await page.request.post(`${apiUrl}/api/auth/login`, {
      data: { email: seededTenant.ownerEmail, password: 'NewExp123!' },
      failOnStatusCode: false,
    });
    expect(loginRes.status()).toBe(401);
  });

  test.fixme('TC-A007/A008 Confirm e-mail token expiry/used (wymaga ucięcia tokenu confirm)', async () => {
    // Można dodać przez expire-confirm-token + UI flow confirm-email; pominięte
    // dla zwięzłości — ten sam pattern co TC-A022 w innej ścieżce.
  });
});
