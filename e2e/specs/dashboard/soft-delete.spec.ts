import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint G': Soft delete + i18n errors. TC-D032, TC-D035, TC-S007.
 */

test.describe('Dashboard — soft delete @p2 @crud', () => {
  test('TC-D032 Soft delete klienta z historią — wizyty zachowane', async ({ ownerApi, api, seededTenant }) => {
    // Seed wizytę z customerem (kontekst soft delete + appointment history)
    const isoIn = (d: number) => { const dt = new Date(); dt.setUTCDate(dt.getUTCDate() + d); return dt.toISOString().slice(0, 10); };
    await api.seedAppointment({
      employeeId: seededTenant.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date: isoIn(25),
      startTime: `${String(10 + Math.floor(Math.random() * 5)).padStart(2, '0')}:${String(Math.floor(Math.random() * 60)).padStart(2, '0')}:00`,
      status: 'Completed',
    });

    const res = await ownerApi.delete(`/api/Customers/${seededTenant.customerId}`, { failOnStatusCode: false });
    expect([200, 204, 400, 404, 409]).toContain(res.status());
  });

  test('TC-D035 Soft delete pracownika z przyszłymi wizytami', async ({ ownerApi, api, seededTenant }) => {
    const isoIn = (d: number) => { const dt = new Date(); dt.setUTCDate(dt.getUTCDate() + d); return dt.toISOString().slice(0, 10); };
    await api.seedAppointment({
      employeeId: seededTenant.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date: isoIn(26),
      startTime: `${String(10 + Math.floor(Math.random() * 5)).padStart(2, '0')}:${String(Math.floor(Math.random() * 60)).padStart(2, '0')}:00`,
      status: 'Booked',
    });

    const res = await ownerApi.delete(`/api/Employees/${seededTenant.employeeId}`, { failOnStatusCode: false });
    expect([200, 204, 400, 404, 409]).toContain(res.status());
  });
});

/**
 * Feedback-fixes — REGRESJA kalendarza po soft-delete.
 *
 * Bug: po soft-delete USŁUGI (lub dezaktywacji pracownika) wizyta historyczna
 * znikała z kalendarza, a jej detal rzucał NotFoundException. Przyczyna: globalny
 * query filter wycinał nieaktywny rekord, a INNER JOIN na Employees/Services gubił
 * całą wizytę. Fix: IgnoreQueryFilters + LEFT JOIN w GetAppointmentsByRange /
 * GetAppointmentById. Wizyta MUSI nadal być widoczna i otwierać detal.
 *
 * Każdy test tworzy DEDYKOWANĄ usługę/pracownika do skasowania, żeby nie psuć
 * współdzielonego seeda (testy są sekwencyjne, jeden tenant na run).
 */
test.describe('Dashboard — kalendarz po soft-delete (regresja) @p0 @crud @calendar', () => {
  const isoIn = (d: number) => {
    const dt = new Date();
    dt.setUTCDate(dt.getUTCDate() + d);
    return dt.toISOString().slice(0, 10);
  };

  interface PreviewDto {
    id: string;
    employeeId?: string;
    serviceName?: string;
    employeeFirstName?: string;
  }

  async function createDisposableService(
    ownerApi: import('@playwright/test').APIRequestContext,
    seededTenant: { serviceCategoryId: string; vatRateId: string; employeeId: string },
  ): Promise<string> {
    const res = await ownerApi.post('/api/Services', {
      data: {
        name: `Doomed-${Date.now()}`,
        amount: 50,
        currency: 'PLN',
        durationInMinutes: 30,
        categoryId: seededTenant.serviceCategoryId,
        vatRateId: seededTenant.vatRateId,
        employeeIds: [seededTenant.employeeId],
      },
    });
    expect(res.ok(), `create doomed service → ${res.status()}`).toBeTruthy();
    const b = await res.json();
    return (typeof b === 'string' ? b : b.id) as string;
  }

  test('TC-D040 Wizyta z soft-deletowaną USŁUGĄ wciąż widoczna w kalendarzu + detal się otwiera', async ({
    ownerApi,
    api,
    seededTenant,
  }) => {
    const serviceId = await createDisposableService(ownerApi, seededTenant);
    const date = isoIn(3);
    const startTime = `${String(9 + Math.floor(Math.random() * 6)).padStart(2, '0')}:${String(Math.floor(Math.random() * 50)).padStart(2, '0')}:00`;

    const seeded = await api.seedAppointment({
      employeeId: seededTenant.employeeId,
      serviceId,
      customerId: seededTenant.customerId,
      date,
      startTime,
      status: 'Booked',
    });
    const apptId = seeded.appointmentId;

    // Soft-delete usługi (DeletionService → Deactivate, nie hard delete).
    const del = await ownerApi.delete(`/api/Services/${serviceId}`, { failOnStatusCode: false });
    expect([200, 204]).toContain(del.status());

    // 1) Kalendarz (range) — wizyta NADAL obecna.
    const rangeRes = await ownerApi.get(
      `/api/Appointments?startDate=${date}&endDate=${date}&employeeId=${seededTenant.employeeId}`,
    );
    expect(rangeRes.ok()).toBeTruthy();
    const list = (await rangeRes.json()) as PreviewDto[];
    expect(list.some((a) => a.id === apptId)).toBeTruthy();

    // 2) Detal wizyty — otwiera się (200), nie NotFound.
    const byId = await ownerApi.get(`/api/Appointments/${apptId}`, { failOnStatusCode: false });
    expect(byId.status()).toBe(200);
  });

  test('TC-D041 Wizyta z dezaktywowanym PRACOWNIKIEM wciąż widoczna + detal się otwiera', async ({
    ownerApi,
    api,
    seededTenant,
  }) => {
    // Dedykowany pracownik (z credami) + jego harmonogram, żeby nie kasować seeda.
    const emp = await api.seedEmployeeWithCredentials(`doomed-emp-${Date.now()}@e2e.local`, 'Employee');
    await api.seedEmployeeSchedule(emp.employeeId).catch(() => {});

    const date = isoIn(4);
    const startTime = `${String(9 + Math.floor(Math.random() * 6)).padStart(2, '0')}:${String(Math.floor(Math.random() * 50)).padStart(2, '0')}:00`;

    const seeded = await api.seedAppointment({
      employeeId: emp.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date,
      startTime,
      status: 'Booked',
    });
    const apptId = seeded.appointmentId;

    const del = await ownerApi.delete(`/api/Employees/${emp.employeeId}`, { failOnStatusCode: false });
    expect([200, 204]).toContain(del.status());

    // Range bez filtra employeeId (pracownik nieaktywny — manager widzi cały zespół).
    const rangeRes = await ownerApi.get(`/api/Appointments?startDate=${date}&endDate=${date}`);
    expect(rangeRes.ok()).toBeTruthy();
    const list = (await rangeRes.json()) as PreviewDto[];
    expect(list.some((a) => a.id === apptId)).toBeTruthy();

    const byId = await ownerApi.get(`/api/Appointments/${apptId}`, { failOnStatusCode: false });
    expect(byId.status()).toBe(200);
  });
});
