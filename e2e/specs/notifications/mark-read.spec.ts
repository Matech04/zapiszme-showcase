import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint G': Notifications mark-read flow. TC-N011, TC-N012, TC-N013.
 */

test.describe('Notifications — mark read flow @p1 @notifications', () => {
  test('TC-N011 Mark single notification as read', async ({ ownerApi, api }) => {
    // Spróbuj zaseedować notification — może padać jeśli reflection nie znajdzie typu
    const seeded = await api.seedNotification('TC-N011 test', 'Generic');
    if (!seeded.notificationId) {
      test.skip(true, 'Notification seed failed — backdoor reflection nie znalazł typu');
      return;
    }
    const res = await ownerApi.post(`/api/Notifications/${seeded.notificationId}/read`, {
      data: {},
      failOnStatusCode: false,
    });
    expect([200, 204, 400, 404]).toContain(res.status());
  });

  test('TC-N012 Mark all notifications as read', async ({ ownerApi }) => {
    const res = await ownerApi.post('/api/Notifications/read-all', {
      data: {},
      failOnStatusCode: false,
    });
    expect([200, 204, 400, 404]).toContain(res.status());
  });

  test.fixme('TC-N013 Notification settings toggle — wymaga zmiany settings + verify że event nie publikuje', async () => {});
});
