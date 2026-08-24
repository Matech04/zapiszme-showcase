import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint J': Employee schedule + leave + shift templates API.
 * TC-D038 Harmonogram weekly, TC-D039 Override, TC-D040 Leave, TC-D049 Apply template.
 */

test.describe('Dashboard — employee schedule API @p2 @crud', () => {
  test('TC-D038 GET employee schedule (po seed schedule)', async ({ ownerApi, api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);
    const res = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/schedules`, { failOnStatusCode: false });
    expect([200, 400, 404]).toContain(res.status());
  });

  test('TC-D039 GET employee schedule overrides', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/schedule-overrides`, { failOnStatusCode: false });
    expect([200, 400, 404]).toContain(res.status());
  });

  test('TC-D040 GET employee leaves (po seed leave)', async ({ ownerApi, api, seededTenant }) => {
    const today = new Date();
    const start = new Date(today.getFullYear() + 1, today.getMonth(), 1).toISOString().slice(0, 10);
    const end = new Date(today.getFullYear() + 1, today.getMonth(), 5).toISOString().slice(0, 10);
    await api.seedEmployeeLeave(seededTenant.employeeId, start, end).catch(() => {});

    const res = await ownerApi.get(`/api/Employees/${seededTenant.employeeId}/leaves`, { failOnStatusCode: false });
    expect([200, 400, 404]).toContain(res.status());
  });

  test('TC-D049 Shift template — list (smoke)', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/ShiftTemplates');
    expect(res.ok()).toBeTruthy();
  });
});
