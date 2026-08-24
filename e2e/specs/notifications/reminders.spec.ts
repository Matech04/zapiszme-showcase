import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint L: Reminder BG cycles. TC-N006, TC-N009.
 */

test.describe('Notifications — reminders BG @p2 @notifications', () => {
  test('TC-N006 Reminder 2h BG cycle (smoke trigger)', async ({ api }) => {
    await api.runBgReminders();
    // Brak rzutu = BG ran. Faktyczna wysyłka maila zależy od wizyt w oknie 2h —
    // ten test sprawdza tylko że endpoint i BG service działają.
  });

  test('TC-N009 Customer-changes endpoint (notifikacje po reschedule/cancel)', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/Notifications/customer-changes');
    expect(res.ok()).toBeTruthy();
    const list = await res.json();
    expect(Array.isArray(list) || Array.isArray(list.items)).toBeTruthy();
  });
});
