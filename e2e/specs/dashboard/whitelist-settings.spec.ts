import { test, expect } from '../../fixtures/owner-session.fixture';

/**
 * Sprint G': Whitelist + tenant settings. TC-D030, TC-D031, TC-D043-046.
 */

test.describe('Dashboard — whitelist + settings @p1 @crud', () => {
  test('TC-D030 Whitelist single — toggle', async ({ api, seededTenant }) => {
    await api.setCustomerWhitelist(seededTenant.customerId, true);
    // Bez błędu = OK. Verify-side query potem.
  });

  test('TC-D031 Whitelist bulk pattern (kilka toggle)', async ({ api, seededTenant }) => {
    // Bulk symulujemy serią pojedynczych toggle.
    for (let i = 0; i < 3; i++) {
      await api.setCustomerWhitelist(seededTenant.customerId, i % 2 === 0);
    }
  });

  test('TC-D043 BookingAccessPolicy → PublicWithWhitelist', async ({ api }) => {
    await api.setTenantSettings({ bookingAccessPolicy: 'PublicWithWhitelist' });
  });

  test('TC-D044 ConfirmationMode → RequireManualConfirmation', async ({ api }) => {
    await api.setTenantSettings({ confirmationMode: 'RequireManualConfirmation' });
    // Reset z powrotem żeby kolejne testy bookingu nie traktowały Pending.
    await api.setTenantSettings({ confirmationMode: 'AutoConfirm' });
  });

  test('TC-D045 CustomerVerificationChannel → Phone', async ({ api }) => {
    await api.setTenantSettings({ customerVerificationChannel: 'Phone' });
    await api.setTenantSettings({ customerVerificationChannel: 'Email' });
  });

  test('TC-D046 StaffCalendarVisibilityPolicy → OwnCalendarOnly', async ({ api }) => {
    await api.setTenantSettings({ staffCalendarVisibilityPolicy: 'OwnCalendarOnly' });
    await api.setTenantSettings({ staffCalendarVisibilityPolicy: 'TeamFull' });
  });
});
