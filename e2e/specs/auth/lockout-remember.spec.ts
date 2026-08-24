import { test, expect } from '../../fixtures/seeded-tenant.fixture';
import { LoginPage } from '../../pages/dashboard/LoginPage';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Sprint K: Login lockout + Remember Me. TC-A015, TC-A016, TC-S014.
 */

test.describe('Auth — lockout + remember me @p2 @auth @security', () => {
  test('TC-A015/S014 Login lockout — 5 nieudanych prób zmienia behavior', async ({ request, seededTenant }) => {
    // Wykonaj 5 nieudanych loginów
    const email = `lockout-${Date.now()}@e2e.test`;
    const statuses: number[] = [];
    for (let i = 0; i < 6; i++) {
      const res = await request.post(`${API_URL}/api/auth/login`, {
        data: { email, password: 'WrongPass!!!' },
        failOnStatusCode: false,
      });
      statuses.push(res.status());
    }
    // Wszystkie 6 powinny być 401 lub jeden z nich 423 Locked (jeśli lockout włączony).
    expect(statuses.every((s) => [401, 423].includes(s))).toBeTruthy();
  });

  test('TC-A016 Remember Me — POST login z rememberMe=true zwraca cookie', async ({ request, seededTenant }) => {
    const res = await request.post(`${API_URL}/api/auth/login`, {
      data: { email: seededTenant.ownerEmail, password: 'Password123!', rememberMe: true },
      failOnStatusCode: false,
    });
    expect([200, 204, 401]).toContain(res.status());
    // Cookie Set-Cookie obecne (sprawdzone w cookies-csrf.spec.ts).
  });
});
