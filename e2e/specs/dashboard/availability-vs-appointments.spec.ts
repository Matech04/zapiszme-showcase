import { expect, test } from '../../fixtures/owner-session.fixture';

/**
 * Dashboard — pracownik może swobodnie dodawać urlop / dzień wolny / zwęzić grafik
 * NAWET GDY w danej dacie są już zaplanowane wizyty. Backend nie blokuje, tylko zwraca
 * listę kolidujących wizyt w response (UI pokazuje toast warn).
 *
 * Każdy test używa unikalnej daty w przyszłości, żeby nie kolidować z innymi testami
 * korzystającymi z tego samego seedowanego tenanta. Po asercjach sprzątamy artefakty.
 */

/**
 * Pierwszy dzień roboczy (pon–pt) w dniu lub po dniu `today + daysAhead`.
 * Seedowany pracownik ma godziny pracy tylko w dni robocze, więc daty liczone
 * surowo w dniach kalendarzowych losowo wpadały w weekend (zależnie od dnia
 * uruchomienia testu) i CREATE wizyty zwracał `appointment.slot_unavailable`.
 */
function nextWeekday(daysAhead: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysAhead);
  const dow = d.getUTCDay(); // 0 = niedziela, 6 = sobota
  if (dow === 6) d.setUTCDate(d.getUTCDate() + 2);
  else if (dow === 0) d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

function addDays(isoDateStr: string, delta: number): string {
  const d = new Date(`${isoDateStr}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + delta);
  return d.toISOString().slice(0, 10);
}

/** Stworz wizytę i zwróć jej id; jeśli backend odrzuci (brak schedule / inny powód) — skip. */
async function createAppointment(
  ownerApi: import('@playwright/test').APIRequestContext,
  args: {
    employeeId: string;
    serviceId: string;
    customerId: string;
    date: string;
    startTime: string;
  },
): Promise<string> {
  const res = await ownerApi.post('/api/Appointments', {
    data: args,
    failOnStatusCode: false,
  });
  if (!res.ok()) {
    throw new Error(`POST /api/Appointments → ${res.status()} ${await res.text()}`);
  }
  const body = await res.json();
  // Endpoint zwraca surowe GUID (Ok(appointmentId)) — przyjmujemy też shape { id }.
  return typeof body === 'string' ? body : body.id;
}

/**
 * Sprząta artefakty (wizyty / urlopy / override) w danym zakresie dat dla pracownika.
 * Tenant jest reused między uruchomieniami testów — bez tego porzucone dane z poprzedniego
 * runa blokowały by CREATE z `appointment.slot_unavailable` albo `leave.overlap`.
 */
async function cleanupDayRange(
  ownerApi: import('@playwright/test').APIRequestContext,
  employeeId: string,
  fromDate: string,
  toDate: string,
): Promise<void> {
  const apps = await ownerApi.get(
    `/api/Appointments?startDate=${fromDate}&endDate=${toDate}&employeeId=${employeeId}`,
    { failOnStatusCode: false },
  );
  if (apps.ok()) {
    const body = await apps.json();
    const items: Array<{ id: string }> = Array.isArray(body) ? body : body.items ?? [];
    for (const a of items) {
      await ownerApi.delete(`/api/Appointments/${a.id}`, { failOnStatusCode: false });
    }
  }

  const leaves = await ownerApi.get(`/api/Employees/${employeeId}/leaves`, {
    failOnStatusCode: false,
  });
  if (leaves.ok()) {
    const list: Array<{ id: string; startDate: string; endDate: string }> = await leaves.json();
    for (const l of list) {
      const ls = (l.startDate ?? '').slice(0, 10);
      const le = (l.endDate ?? '').slice(0, 10);
      if (ls <= toDate && le >= fromDate) {
        await ownerApi.delete(`/api/Employees/${employeeId}/leaves/${l.id}`, {
          failOnStatusCode: false,
        });
      }
    }
  }

  // Każdy dzień w zakresie — defensywny DELETE override (404 ignorowane).
  for (let d = new Date(fromDate); d <= new Date(toDate); d.setUTCDate(d.getUTCDate() + 1)) {
    const iso = d.toISOString().slice(0, 10);
    await ownerApi.delete(`/api/Employees/${employeeId}/schedule-overrides/${iso}`, {
      failOnStatusCode: false,
    });
  }
}

test.describe('Dashboard — pracownik mimo wizyt @p1 @availability', () => {
  test('TC-DA001 Urlop można dodać mimo wizyt w zakresie — zwraca listę kolidujących', async ({
    ownerApi,
    seededTenant,
  }) => {
    const apptDate = nextWeekday(91);
    const leaveStart = addDays(apptDate, -1);
    const leaveEnd = addDays(apptDate, 1);

    await cleanupDayRange(ownerApi, seededTenant.employeeId, leaveStart, leaveEnd);

    const appointmentId = await createAppointment(ownerApi, {
      employeeId: seededTenant.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date: apptDate,
      startTime: '10:00:00',
    });

    const res = await ownerApi.post(`/api/Employees/${seededTenant.employeeId}/leaves`, {
      data: { startDate: leaveStart, endDate: leaveEnd, absenceType: 1 /* Vacation */ },
      failOnStatusCode: false,
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(Array.isArray(body.appointmentsInLeave)).toBe(true);
    expect(body.appointmentsInLeave).toHaveLength(1);
    expect(body.appointmentsInLeave[0].appointmentId).toBe(appointmentId);

    // cleanup — defensive (best effort, nie blokuje testu)
    const leaves = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/leaves`);
    if (leaves.ok()) {
      const list = (await leaves.json()) as Array<{ id: string; startDate: string }>;
      const created = list.find((l) => l.startDate?.slice(0, 10) === leaveStart);
      if (created) {
        await ownerApi.delete(
          `/api/Employees/${seededTenant.employeeId}/leaves/${created.id}`,
          { failOnStatusCode: false },
        );
      }
    }
    await ownerApi.delete(`/api/Appointments/${appointmentId}`, { failOnStatusCode: false });
  });

  test('TC-DA002 SpecialDay (nieobecność jednodniowa) mimo wizyt', async ({
    ownerApi,
    seededTenant,
  }) => {
    const date = nextWeekday(60);

    await cleanupDayRange(ownerApi, seededTenant.employeeId, date, date);

    const appointmentId = await createAppointment(ownerApi, {
      employeeId: seededTenant.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date,
      startTime: '11:00:00',
    });

    const res = await ownerApi.post(`/api/Employees/${seededTenant.employeeId}/leaves`, {
      data: { startDate: date, endDate: date, absenceType: 3 /* SpecialDay */ },
      failOnStatusCode: false,
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.appointmentsInLeave).toHaveLength(1);
    expect(body.appointmentsInLeave[0].appointmentId).toBe(appointmentId);

    const leaves = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/leaves`);
    if (leaves.ok()) {
      const list = (await leaves.json()) as Array<{ id: string; startDate: string }>;
      const created = list.find((l) => l.startDate?.slice(0, 10) === date);
      if (created) {
        await ownerApi.delete(
          `/api/Employees/${seededTenant.employeeId}/leaves/${created.id}`,
          { failOnStatusCode: false },
        );
      }
    }
    await ownerApi.delete(`/api/Appointments/${appointmentId}`, { failOnStatusCode: false });
  });

  test('TC-DA003 Override = dzień wolny (puste workRanges) mimo wizyt', async ({
    ownerApi,
    seededTenant,
  }) => {
    const date = nextWeekday(50);

    await cleanupDayRange(ownerApi, seededTenant.employeeId, date, date);

    const appointmentId = await createAppointment(ownerApi, {
      employeeId: seededTenant.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date,
      startTime: '10:00:00',
    });

    const res = await ownerApi.post(
      `/api/Employees/${seededTenant.employeeId}/schedule-overrides`,
      {
        data: { date, workRanges: [], breaks: [] },
        failOnStatusCode: false,
      },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(Array.isArray(body.appointmentsOutsideSchedule)).toBe(true);
    expect(body.appointmentsOutsideSchedule).toHaveLength(1);
    expect(body.appointmentsOutsideSchedule[0].appointmentId).toBe(appointmentId);

    await ownerApi.delete(
      `/api/Employees/${seededTenant.employeeId}/schedule-overrides/${date}`,
      { failOnStatusCode: false },
    );
    await ownerApi.delete(`/api/Appointments/${appointmentId}`, { failOnStatusCode: false });
  });

  test('TC-DA004 Override = zwężenie godzin pracy, wizyta zostaje poza nowym zakresem', async ({
    ownerApi,
    seededTenant,
  }) => {
    const date = nextWeekday(57);

    await cleanupDayRange(ownerApi, seededTenant.employeeId, date, date);

    const appointmentId = await createAppointment(ownerApi, {
      employeeId: seededTenant.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date,
      startTime: '09:00:00',
    });

    const res = await ownerApi.post(
      `/api/Employees/${seededTenant.employeeId}/schedule-overrides`,
      {
        data: {
          date,
          // nowe godziny 12:00-16:00 — wizyta 09:00 jest poza nimi
          workRanges: [{ startTime: '12:00:00', endTime: '16:00:00' }],
          breaks: [],
        },
        failOnStatusCode: false,
      },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.appointmentsOutsideSchedule).toHaveLength(1);
    expect(body.appointmentsOutsideSchedule[0].appointmentId).toBe(appointmentId);

    await ownerApi.delete(
      `/api/Employees/${seededTenant.employeeId}/schedule-overrides/${date}`,
      { failOnStatusCode: false },
    );
    await ownerApi.delete(`/api/Appointments/${appointmentId}`, { failOnStatusCode: false });
  });
});
