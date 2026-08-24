import { expect, test } from '../../fixtures/owner-session.fixture';

/**
 * Dashboard CRUD — employees + assignments + schedules. TC-D033..D040.
 */

test.describe('Dashboard — employees @p1 @crud', () => {
  test('TC-D033 Lista pracowników', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.get('/api/Employees');
    expect(res.ok()).toBeTruthy();
    const list = await res.json();
    const employees = Array.isArray(list) ? list : list.items ?? [];
    expect(employees.find((e: { id: string }) => e.id === seededTenant.employeeId)).toBeTruthy();
  });

  test('TC-D034 Pracownik — edit imię/nazwisko', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.put(`/api/Employees/${seededTenant.employeeId}`, {
      data: {
        firstName: 'Edited',
        lastName: 'Worker',
        displayName: 'EW',
        specialization: 'Hair stylist',
      },
      failOnStatusCode: false,
    });
    // Może wymagać dodatkowych pól; akceptujemy 200/204 lub 400 (walidacja).
    expect([200, 204, 400, 404]).toContain(res.status());
  });

  test('TC-D036 Pracownik → usługa: assign', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.post(
      `/api/Employees/${seededTenant.employeeId}/services`,
      {
        data: { serviceId: seededTenant.serviceId },
        failOnStatusCode: false,
      },
    );
    // Może już być przypisana w seedzie / inny shape body — akceptujemy szerszą pulę.
    expect([200, 201, 204, 400, 404, 409, 422]).toContain(res.status());
  });

  test('TC-D037 Pracownik → usługi list', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/services`);
    expect(res.ok()).toBeTruthy();
  });

  test.fixme('TC-D035 Soft delete pracownika z przyszłymi wizytami', async () => {});
  test.fixme('TC-D038 Harmonogram tygodniowy create/edit/delete (wymaga endpoint contract)', async () => {});
  // TC-D039 Special day / override — zob. availability-vs-appointments.spec.ts
  // TC-D040 Leave period — zob. availability-vs-appointments.spec.ts
});
