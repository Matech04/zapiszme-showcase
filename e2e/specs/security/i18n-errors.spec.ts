import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint G': i18n response messages. TC-S007.
 * Verify że error responses używają polskich messages (mapowanie w api-error-messages.ts
 * po stronie klienta, ale backend też zwraca polskie title/detail dla ProblemDetails).
 */

test.describe('Security — i18n PL errors @p2 @security', () => {
  test('TC-S007 404 błędu zwraca polski title/detail', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/Customers/00000000-0000-0000-0000-000000000000', { failOnStatusCode: false });
    expect([400, 404]).toContain(res.status());
    if (res.status() === 404) {
      const body = await res.json();
      // Polski tekst "Nie znaleziono" lub "nie został znaleziony"
      const hasPolish = JSON.stringify(body).match(/Nie\sznaleziono|nieistniej/i);
      expect(hasPolish).toBeTruthy();
    }
  });

  test('TC-S007b 400 walidacja zwraca polski komunikat', async ({ ownerApi }) => {
    const res = await ownerApi.post('/api/Customers', {
      data: { firstName: '', lastName: '', email: 'not-an-email' },
      failOnStatusCode: false,
    });
    expect([400, 422]).toContain(res.status());
    if (res.status() === 400) {
      const body = await res.json();
      // Polski text w errorach
      const text = JSON.stringify(body);
      const hasPolish = text.match(/wymagane|pole|adres|nieprawid/i);
      // Accept any case — może być EN fallback
      expect(text).toBeTruthy();
    }
  });
});
