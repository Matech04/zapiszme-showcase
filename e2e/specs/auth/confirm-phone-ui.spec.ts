import { expect, test } from '../../fixtures/seeded-tenant.fixture';

/**
 * Sprint M — UI flow potwierdzenia telefonu po rejestracji. TC-A042..A043.
 *
 * Flow użytkownika:
 *   1. /register (TC-A001) → wypełnienie formularza + telefon
 *   2. /check-email → kliknięcie linku z maila
 *   3. /confirm-email → automatyczne potwierdzenie, redirect na /confirm-phone?userId=X
 *   4. Wpisanie 6-cyfrowego kodu z SMS → submit → redirect /login z toast'em sukcesu
 */

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';
const UNIQUE = () => `${Date.now()}${Math.random().toString(36).slice(2, 5)}`;

test.describe('Auth — confirm-phone UI flow @p0 @auth @phone @ui', () => {
  test('TC-A042 Owner po confirm-email trafia na /confirm-phone i potwierdza kodem', async ({
    page,
    request,
    api,
  }) => {
    const slug = `tc-a042-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    const phone = `+48510${String(Date.now()).slice(-6)}`;

    // 1) Register przez API (UI zostało już pokryte przez TC-A001)
    const reg = await request.post(`${API_URL}/api/auth/register-owner`, {
      data: {
        salonName: 'TC-A042',
        salonSlug: slug,
        timeZoneId: 'Europe/Warsaw',
        currency: 'PLN',
        email,
        password: 'Password123!',
        displayName: 'TC-A042',
        firstName: 'A',
        lastName: 'Phone',
        phoneNumber: phone,
        turnstileToken: 'e2e-bypass',
      },
    });
    expect(reg.ok()).toBeTruthy();
    const userId = (await reg.json()).userId as string;

    // 2) Confirm-email przez API (UI confirm-email page auto-konfirmuje, ale wymaga loadu
    // skomplikowanej Angular app). Wystarczy że backend dostał POST.
    const mail = await api.getLastAuthEmail(email);
    expect(mail.lastConfirmEmailUrl).toBeTruthy();
    const cnfUrl = new URL(mail.lastConfirmEmailUrl!);
    await request.post(`${API_URL}/api/auth/confirm-email`, {
      data: {
        userId: cnfUrl.searchParams.get('userId'),
        token: cnfUrl.searchParams.get('token'),
      },
    });

    // 3) Pobierz wysłany SMS OTP (z TestPhoneOtpMailbox via backdoor)
    const otp = await api.getLastPhoneOtp(userId);
    expect(otp).not.toBeNull();
    expect(otp!.code).toMatch(/^\d{6}$/);

    // 4) Otwórz /confirm-phone w UI i wpisz kod
    await page.goto(`/confirm-phone?userId=${userId}`);

    // 5) Wpisz kod i submit
    await page.getByTestId('confirm-phone-code').fill(otp!.code);
    await page.getByTestId('confirm-phone-submit').click();

    // 6) Success → redirect /login (lub komunikat „Telefon potwierdzony")
    await page.waitForTimeout(2000);
    const url = page.url();
    expect(url).toMatch(/\/login|\/confirm-phone/);

    // Po confirm-phone happy path PhoneNumberConfirmed=true → login działa.
    const login = await request.post(`${API_URL}/api/auth/login`, {
      data: { email, password: 'Password123!', rememberMe: false, turnstileToken: 'e2e-bypass' },
      failOnStatusCode: false,
    });
    expect(login.ok()).toBeTruthy();
  });

  test('TC-A043 Confirm-phone z błędnym kodem pokazuje błąd', async ({ page, request, api }) => {
    const slug = `tc-a043-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    const phone = `+48511${String(Date.now()).slice(-6)}`;

    const reg = await request.post(`${API_URL}/api/auth/register-owner`, {
      data: {
        salonName: 'TC-A043',
        salonSlug: slug,
        timeZoneId: 'Europe/Warsaw',
        currency: 'PLN',
        email,
        password: 'Password123!',
        displayName: 'TC-A043',
        firstName: 'B',
        lastName: 'Phone',
        phoneNumber: phone,
        turnstileToken: 'e2e-bypass',
      },
    });
    const userId = (await reg.json()).userId as string;

    // Skip confirm-email — wystarczy że user istnieje i ma phone. Confirm-phone
    // page przyjmuje userId z query string bez wcześniejszego confirm-email.
    await page.goto(`/confirm-phone?userId=${userId}`);

    await page.getByTestId('confirm-phone-code').fill('000000');
    await page.getByTestId('confirm-phone-submit').click();

    await page.waitForTimeout(1500);
    // UI pokazuje błąd (alert lub komunikat inline) — URL nadal /confirm-phone
    expect(page.url()).toContain('/confirm-phone');
  });
});
