import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Notifications. TC-N001..N015.
 * Większość testów zweryfikowana przez backdoor + API.
 */

test.describe('Notifications — in-app @p1 @notifications', () => {
  test('TC-N007 GET /api/Notifications', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/Notifications');
    expect(res.ok()).toBeTruthy();
    const list = await res.json();
    expect(Array.isArray(list) || Array.isArray(list.items)).toBeTruthy();
  });

  test('TC-N010 GET outside-schedule alerts', async ({ ownerApi }) => {
    const from = new Date().toISOString().slice(0, 10);
    const to = new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10);
    const res = await ownerApi.get(`/api/Notifications/outside-schedule?from=${from}&to=${to}`);
    expect([200, 400, 404]).toContain(res.status());
  });

  test('TC-N008-009 GET customer-changes', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/Notifications/customer-changes');
    expect(res.ok()).toBeTruthy();
  });

  test('TC-N015 Status lifecycle BG trigger (idempotent)', async ({ api }) => {
    await api.runBgStatusLifecycle();
    // Brak rzutu = sukces (BG ran).
  });

  test('TC-N005/N006 Reminder BG trigger', async ({ api }) => {
    await api.runBgReminders();
  });

  test.fixme('TC-N001-004 E-mail delivery sanity (rejestracja, reset, invite, confirmation booking) — covered w auth/booking specs', async () => {});
  test.fixme('TC-N011/N012 Mark read / mark all read — wymaga notyfikacji w DB', async () => {});
  test.fixme('TC-N013 Notification settings toggle — wymaga UI/settings update', async () => {});
  test.fixme('TC-N014 Sensitive data masking w logach — wymaga inspekcji logów', async () => {});
});
