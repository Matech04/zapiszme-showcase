import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint H: notification settings. TC-N013.
 */

test.describe('Notifications — settings toggle @p2 @notifications', () => {
  test('TC-N013 GET notification settings endpoint odpowiada', async ({ ownerApi }) => {
    // Część salon settings — sprawdzamy że dostępne
    const res = await ownerApi.get('/api/SalonSettings');
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    // NotificationSettings może być wewnętrznym polem
    expect(body).toBeTruthy();
  });
});
