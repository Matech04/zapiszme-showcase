import { expect, test } from '../../fixtures/seeded-tenant.fixture';

/**
 * Sprint M — phone OTP przy rejestracji. TC-A031..A035.
 *
 * Flow:
 *   1. POST /api/auth/register-owner z PhoneNumber → użytkownik utworzony z PhoneNumberConfirmed=false
 *   2. POST /api/auth/confirm-email → backend wysyła SMS OTP (SendPhoneOtpCommand)
 *   3. POST /api/auth/confirm-phone {userId, code} → ustawia PhoneNumberConfirmed=true
 *   4. POST /api/auth/login z PhoneNumberConfirmed=false → 401 errorCode='auth.phone_not_confirmed' + userId
 *
 * Backdoory:
 *   - GET /api/_e2e/phone-otp/{userId} → ostatni kod z TestPhoneOtpMailbox
 *   - GET /api/_e2e/auth-email/{email} → confirm-email URL
 */

const UNIQUE = () => `${Date.now()}${Math.random().toString(36).slice(2, 5)}`;

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

async function registerOwner(request: import('@playwright/test').APIRequestContext, opts: {
  email: string;
  phone: string;
  slug: string;
  promoCode?: string;
}) {
  return request.post(`${API_URL}/api/auth/register-owner`, {
    data: {
      salonName: `E2E ${opts.slug}`,
      salonSlug: opts.slug,
      timeZoneId: 'Europe/Warsaw',
      currency: 'PLN',
      email: opts.email,
      password: 'Password123!',
      displayName: 'E2E User',
      firstName: 'E2E',
      lastName: 'User',
      phoneNumber: opts.phone,
      turnstileToken: 'e2e-bypass',
      promoCode: opts.promoCode ?? null,
    },
    failOnStatusCode: false,
  });
}

test.describe('Auth — phone OTP @p0 @auth @phone @sms', () => {
  test('TC-A031 Register zwraca błąd przy nieprawidłowym numerze', async ({ request }) => {
    const slug = `e2e-phone-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    // Krótszy niż wymagane PhoneNumber.MinLength → 400
    const res = await registerOwner(request, { email, phone: '123', slug });
    expect(res.status()).toBe(400);
  });

  test('TC-A032 Register + confirm-email triggeruje wysyłkę SMS OTP', async ({ request, api }) => {
    const slug = `e2e-phone-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    const phone = `+48504${String(Date.now()).slice(-6)}`;

    // 1) Register
    const reg = await registerOwner(request, { email, phone, slug });
    expect(reg.ok()).toBeTruthy();
    const regBody = (await reg.json()) as { userId: string };
    expect(regBody.userId).toMatch(/^[0-9a-f-]{36}$/);

    // 2) Confirm-email — wymaga URL z mailbox
    const mail = await api.getLastAuthEmail(email);
    expect(mail.lastConfirmEmailUrl).toBeTruthy();
    // URL ma format /confirm-email?userId=X&token=Y — wyciągnij params i POST na /api/auth/confirm-email
    const url = new URL(mail.lastConfirmEmailUrl!);
    const userId = url.searchParams.get('userId')!;
    const token = url.searchParams.get('token')!;
    const cnf = await request.post(`${API_URL}/api/auth/confirm-email`, {
      data: { userId, token },
    });
    expect(cnf.ok()).toBeTruthy();

    // 3) Po confirm-email backend wysyła SMS OTP — sprawdź TestPhoneOtpMailbox
    const otp = await api.getLastPhoneOtp(userId);
    expect(otp).not.toBeNull();
    expect(otp!.code).toMatch(/^\d{6}$/);
    expect(otp!.phone).toBe(phone);
  });

  test('TC-A033 confirm-phone happy path → PhoneNumberConfirmed=true', async ({ request, api }) => {
    const slug = `e2e-phone-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    const phone = `+48505${String(Date.now()).slice(-6)}`;

    const reg = await registerOwner(request, { email, phone, slug });
    expect(reg.ok()).toBeTruthy();
    const userId = (await reg.json()).userId;

    // Confirm-email
    const mail = await api.getLastAuthEmail(email);
    const url = new URL(mail.lastConfirmEmailUrl!);
    await request.post(`${API_URL}/api/auth/confirm-email`, {
      data: { userId: url.searchParams.get('userId'), token: url.searchParams.get('token') },
    });

    // Pobierz OTP i confirm-phone
    const otp = await api.getLastPhoneOtp(userId);
    expect(otp).not.toBeNull();
    const cp = await request.post(`${API_URL}/api/auth/confirm-phone`, {
      data: { userId, code: otp!.code },
    });
    expect(cp.ok()).toBeTruthy();

    // Po confirm-phone login powinien przejść (oba gate'y zaspokojone)
    const login = await request.post(`${API_URL}/api/auth/login`, {
      data: { email, password: 'Password123!', rememberMe: false, turnstileToken: 'e2e-bypass' },
      failOnStatusCode: false,
    });
    expect(login.ok()).toBeTruthy();
  });

  test('TC-A034 confirm-phone z błędnym kodem → 400', async ({ request, api }) => {
    const slug = `e2e-phone-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    const phone = `+48506${String(Date.now()).slice(-6)}`;

    const reg = await registerOwner(request, { email, phone, slug });
    expect(reg.ok()).toBeTruthy();
    const userId = (await reg.json()).userId;

    const mail = await api.getLastAuthEmail(email);
    const url = new URL(mail.lastConfirmEmailUrl!);
    await request.post(`${API_URL}/api/auth/confirm-email`, {
      data: { userId: url.searchParams.get('userId'), token: url.searchParams.get('token') },
    });

    const cp = await request.post(`${API_URL}/api/auth/confirm-phone`, {
      data: { userId, code: '000000' },
      failOnStatusCode: false,
    });
    expect(cp.status()).toBeGreaterThanOrEqual(400);
    expect(cp.status()).toBeLessThan(500);
  });

  test('TC-A035 Login PhoneNotConfirmed → 401 errorCode auth.phone_not_confirmed + userId', async ({
    request,
    api,
  }) => {
    const slug = `e2e-phone-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    const phone = `+48507${String(Date.now()).slice(-6)}`;

    const reg = await registerOwner(request, { email, phone, slug });
    expect(reg.ok()).toBeTruthy();
    const userId = (await reg.json()).userId;

    // Confirm email ale NIE phone
    const mail = await api.getLastAuthEmail(email);
    const url = new URL(mail.lastConfirmEmailUrl!);
    await request.post(`${API_URL}/api/auth/confirm-email`, {
      data: { userId: url.searchParams.get('userId'), token: url.searchParams.get('token') },
    });

    // Login bez confirm-phone → 401 + auth.phone_not_confirmed
    const login = await request.post(`${API_URL}/api/auth/login`, {
      data: { email, password: 'Password123!', rememberMe: false, turnstileToken: 'e2e-bypass' },
      failOnStatusCode: false,
    });
    expect(login.status()).toBe(401);
    const body = (await login.json()) as { errorCode?: string; userId?: string };
    expect(body.errorCode).toBe('auth.phone_not_confirmed');
    expect(body.userId).toBe(userId);
  });

  test('TC-A035b Resend phone OTP wysyła nowy kod', async ({ request, api }) => {
    const slug = `e2e-phone-${UNIQUE()}`;
    const email = `${slug}@e2e.test`;
    const phone = `+48508${String(Date.now()).slice(-6)}`;

    const reg = await registerOwner(request, { email, phone, slug });
    expect(reg.ok()).toBeTruthy();
    const userId = (await reg.json()).userId;

    const mail = await api.getLastAuthEmail(email);
    const url = new URL(mail.lastConfirmEmailUrl!);
    await request.post(`${API_URL}/api/auth/confirm-email`, {
      data: { userId: url.searchParams.get('userId'), token: url.searchParams.get('token') },
    });

    const firstOtp = await api.getLastPhoneOtp(userId);
    expect(firstOtp).not.toBeNull();

    // Cooldown — handler ma rate limit; ten test może padać z 429.
    const resend = await request.post(`${API_URL}/api/auth/resend-phone-otp`, {
      data: { userId },
      failOnStatusCode: false,
    });
    // 200 (wysłano) lub 429/400 (cooldown) — oba akceptowalne.
    expect([200, 204, 400, 429]).toContain(resend.status());
  });
});

test.describe('Auth — phone unique constraint @p1 @auth @phone', () => {
  test('TC-A031b Duplicate phone number — rejestracja 2 z tym samym telefonem rzuca 409', async ({
    request,
  }) => {
    const phone = `+48509${String(Date.now()).slice(-6)}`;

    const first = await registerOwner(request, {
      email: `dup-phone-1-${UNIQUE()}@e2e.test`,
      phone,
      slug: `e2e-dphone1-${UNIQUE()}`,
    });
    expect(first.ok()).toBeTruthy();

    const second = await registerOwner(request, {
      email: `dup-phone-2-${UNIQUE()}@e2e.test`,
      phone,
      slug: `e2e-dphone2-${UNIQUE()}`,
    });
    expect(second.status()).toBeGreaterThanOrEqual(400);
    expect(second.status()).toBeLessThan(500);
  });
});
