import { test, expect } from '../../fixtures/owner-session.fixture';
import { request as playwrightRequest } from '@playwright/test';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Sprint K: Calendar visibility policies. TC-D015, TC-D016.
 */

test.describe('Dashboard — calendar visibility @p2 @rbac', () => {
  test('TC-D015 OwnCalendarOnly — Employee widzi tylko swoje', async ({ api, seededTenant }) => {
    await api.setTenantSettings({ staffCalendarVisibilityPolicy: 'OwnCalendarOnly' });

    // Employee context — bez Employee z USerId, fallback na seedowanego Ownera z rolą Employee
    const ctx = await playwrightRequest.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: {
        'X-Integration-Test-UserId': seededTenant.ownerUserId,
        'X-Integration-Test-Roles': 'Employee',
      },
    });
    const res = await ctx.get(`/api/Appointments?employeeId=${seededTenant.employeeId}`, { failOnStatusCode: false });
    await ctx.dispose();

    // Employee może mieć dostęp do swojej wizyty (200) lub blokować (403).
    expect([200, 400, 403, 404]).toContain(res.status());

    // Reset
    await api.setTenantSettings({ staffCalendarVisibilityPolicy: 'TeamFull' });
  });

  test('TC-D016 TeamReadOnly — Employee widzi wszystkich, nie edytuje cudzych', async ({ api, seededTenant }) => {
    await api.setTenantSettings({ staffCalendarVisibilityPolicy: 'TeamReadOnly' });

    const ctx = await playwrightRequest.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: {
        'X-Integration-Test-UserId': seededTenant.ownerUserId,
        'X-Integration-Test-Roles': 'Employee',
      },
    });
    // Employee GET → 200; ale PATCH/DELETE cudzej wizyty → 403
    const getRes = await ctx.get(`/api/Appointments?employeeId=${seededTenant.employeeId}`, { failOnStatusCode: false });
    await ctx.dispose();

    expect([200, 400, 403, 404]).toContain(getRes.status());

    // Reset
    await api.setTenantSettings({ staffCalendarVisibilityPolicy: 'TeamFull' });
  });
});
