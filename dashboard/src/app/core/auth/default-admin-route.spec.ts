import { describe, expect, it } from 'vitest';
import { defaultAdminRouteForRole } from './default-admin-route';

describe('defaultAdminRouteForRole', () => {
  it('owner ląduje na kalendarzu (Dashboard usunięty)', () => {
    expect(defaultAdminRouteForRole('owner')).toBe('/admin/schedule');
  });

  it('manager ląduje na kalendarzu', () => {
    expect(defaultAdminRouteForRole('manager')).toBe('/admin/schedule');
  });

  it('systemAdmin ląduje na liście salonów', () => {
    expect(defaultAdminRouteForRole('systemAdmin')).toBe('/admin/system/tenants');
  });

  it('employee z id ląduje na własnym grafiku, bez id — na wspólnym', () => {
    expect(defaultAdminRouteForRole('employee', 'emp-1')).toBe('/admin/schedule/emp-1');
    expect(defaultAdminRouteForRole('employee')).toBe('/admin/schedule');
  });

  it('kiosk ląduje na grafiku', () => {
    expect(defaultAdminRouteForRole('kiosk')).toBe('/admin/schedule');
  });

  it('nieznana rola (null) → neutralny kalendarz zamiast panelu admina', () => {
    expect(defaultAdminRouteForRole(null)).toBe('/admin/schedule');
    expect(defaultAdminRouteForRole(null, 'emp-1')).toBe('/admin/schedule');
  });
});
