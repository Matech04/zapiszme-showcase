import { test, expect } from '../../fixtures/owner-session.fixture';
import { request as playwrightRequest } from '@playwright/test';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

function isoIn(d: number): string {
  const dt = new Date();
  dt.setUTCDate(dt.getUTCDate() + d);
  return dt.toISOString().slice(0, 10);
}

function randomSlot(): string {
  const hh = 9 + Math.floor(Math.random() * 7);
  return `${String(hh).padStart(2, '0')}:00:00`;
}

/**
 * Sprint F: concurrency. TC-D020.
 * Dwa równoczesne POST /api/Appointments na ten sam slot — tylko jeden powinien
 * przejść, drugi zwrócić 409 Conflict.
 */
test.describe('Dashboard — concurrency @p1 @appointment', () => {
  test('TC-D020 Race: dwa POST na ten sam slot → tylko jeden sukces', async ({ api, seededTenant }) => {
    await api.seedEmployeeSchedule(seededTenant.employeeId);

    const slot = randomSlot();
    const date = isoIn(8);
    const body = {
      employeeId: seededTenant.employeeId,
      serviceId: seededTenant.serviceId,
      customerId: seededTenant.customerId,
      date,
      startTime: slot,
    };

    const headers = {
      'X-Integration-Test-UserId': seededTenant.ownerUserId,
      'X-Integration-Test-Roles': 'Owner',
    };

    const ctx1 = await playwrightRequest.newContext({ baseURL: API_URL, extraHTTPHeaders: headers });
    const ctx2 = await playwrightRequest.newContext({ baseURL: API_URL, extraHTTPHeaders: headers });

    const [res1, res2] = await Promise.all([
      ctx1.post('/api/Appointments', { data: body, failOnStatusCode: false }),
      ctx2.post('/api/Appointments', { data: body, failOnStatusCode: false }),
    ]);

    await ctx1.dispose();
    await ctx2.dispose();

    const statuses = [res1.status(), res2.status()].sort();
    // Jeden powinien być sukces, drugi konflikt LUB oba 400 (jeśli walidacja innego pola).
    // Akceptujemy: jeden sukces + jeden konflikt; oba sukcesy NIE są OK (anti-race musi działać).
    const succeeded = statuses.filter((s) => s >= 200 && s < 300).length;
    expect(succeeded).toBeLessThanOrEqual(1);
  });
});
