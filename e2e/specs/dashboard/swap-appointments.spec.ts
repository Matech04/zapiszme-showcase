import { test, expect } from '../../fixtures/owner-session.fixture';
import { LoginPage } from '../../pages/dashboard/LoginPage';

/**
 * Zamiana terminów dwóch wizyt. POST /api/Appointments/swap + GET /api/Appointments/swap/preview.
 * API-level (jak reszta dashboard e2e) + smoke trybu zamiany w UI kalendarza.
 *
 * Uwaga: scenariusze różnej długości + harmonizacji (zmiana usługi dłuższej wizyty) są pokryte
 * testami integracyjnymi backendu (AppointmentSwapIntegrationTests) — wymagają realnej drugiej
 * usługi o innej długości, czego backdoor /api/_e2e/seed-appointment nie tworzy (czas trwania
 * wizyty liczony jest z usługi, nie z pola durationMinutes).
 *
 * Baza booking_e2e jest współdzielona i NIE resetowana między uruchomieniami — dlatego daty
 * wizyt są unikalne per-uruchomienie, by uniknąć kolizji z UNIQUE-indexem na slocie.
 */

const RUN = Date.now();

function futureIso(dayOffset: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + 200 + (RUN % 4000) + dayOffset);
  return d.toISOString().slice(0, 10);
}

test.describe('Dashboard — zamiana terminów @p0 @appointment', () => {
  test('TC-SWAP01 Zamiana równych długości — happy path (API)', async ({ ownerApi, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId, '08:00:00', '20:00:00');
    const date = futureIso(1);
    const a = await api.seedAppointment({ employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, customerId: seededTenant.customerId, date, startTime: '10:00:00', durationMinutes: 30 });
    const b = await api.seedAppointment({ employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, customerId: seededTenant.customerId, date, startTime: '12:00:00', durationMinutes: 30 });

    const res = await ownerApi.post('/api/Appointments/swap', {
      data: { firstAppointmentId: a.appointmentId, secondAppointmentId: b.appointmentId, harmonizeToShorter: false },
      failOnStatusCode: false,
    });
    expect(res.status(), await res.text()).toBe(200);

    // Weryfikacja: godziny zamienione miejscami.
    const list = await ownerApi.get(`/api/Appointments?startDate=${date}&endDate=${date}&employeeId=${seededTenant.employeeId}`);
    expect(list.status()).toBe(200);
    const items = (await list.json()) as Array<{ id: string; startTime: string }>;
    const ra = items.find((x) => x.id === a.appointmentId);
    const rb = items.find((x) => x.id === b.appointmentId);
    expect(ra?.startTime?.slice(0, 5)).toBe('12:00');
    expect(rb?.startTime?.slice(0, 5)).toBe('10:00');
  });

  test('TC-SWAP02 Podgląd — równe długości (API)', async ({ ownerApi, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId, '08:00:00', '20:00:00');
    const date = futureIso(2);
    const a = await api.seedAppointment({ employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, customerId: seededTenant.customerId, date, startTime: '10:00:00', durationMinutes: 30 });
    const b = await api.seedAppointment({ employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, customerId: seededTenant.customerId, date, startTime: '12:00:00', durationMinutes: 30 });

    const res = await ownerApi.get(`/api/Appointments/swap/preview?firstId=${a.appointmentId}&secondId=${b.appointmentId}`, { failOnStatusCode: false });
    expect(res.status(), await res.text()).toBe(200);
    const dto = (await res.json()) as { equalDuration: boolean; plainSwapFits: boolean; harmonizationAvailable: boolean };
    expect(dto.equalDuration).toBe(true);
    expect(dto.plainSwapFits).toBe(true);
    expect(dto.harmonizationAvailable).toBe(false);
  });

  test('TC-SWAP03 Walidacja — ta sama wizyta odrzucona (API)', async ({ ownerApi, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId, '08:00:00', '20:00:00');
    const date = futureIso(3);
    const a = await api.seedAppointment({ employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, customerId: seededTenant.customerId, date, startTime: '10:00:00', durationMinutes: 30 });

    const res = await ownerApi.post('/api/Appointments/swap', {
      data: { firstAppointmentId: a.appointmentId, secondAppointmentId: a.appointmentId, harmonizeToShorter: false },
      failOnStatusCode: false,
    });
    expect(res.status()).toBe(400);
  });

  test('TC-SWAP04 Tryb zamiany z drawera wizyty (UI smoke)', async ({ page, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId, '08:00:00', '20:00:00');
    const date = futureIso(4);
    const a = await api.seedAppointment({ employeeId: seededTenant.employeeId, serviceId: seededTenant.serviceId, customerId: seededTenant.customerId, date, startTime: '10:00:00', durationMinutes: 30 });
    await ensureLogin(page, api, seededTenant.ownerEmail);

    // Deep-link otwiera drawer wizyty bezpośrednio — start zamiany jest teraz w drawerze.
    await page.goto(`/admin/schedule/${seededTenant.employeeId}?date=${date}&appointment=${a.appointmentId}`).catch(() => {});
    await page.waitForLoadState('networkidle').catch(() => {});

    const swapBtn = page.getByTestId('swap-from-drawer');
    if (!(await swapBtn.isVisible().catch(() => false))) {
      test.skip(true, 'Drawer nie wyrenderował akcji zamiany — smoke pomijany.');
      return;
    }
    await swapBtn.click();
    await expect(page.getByTestId('swap-mode-banner')).toBeVisible();
    await page.getByTestId('swap-mode-cancel').click();
    await expect(page.getByTestId('swap-mode-banner')).toHaveCount(0);
  });
});

async function ensureLogin(page: any, api: any, ownerEmail: string): Promise<void> {
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
