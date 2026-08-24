import { UserRole } from '@core/services/NavigationService';

/** `role === null` (sesja jeszcze nieznana) → neutralny kalendarz; guardy i tak przekierują dalej. */
export function defaultAdminRouteForRole(role: UserRole | null, employeeId?: string | null): string {
  switch (role) {
    case 'systemAdmin':
      return '/admin/system/tenants';
    case 'owner':
    case 'manager':
      return '/admin/schedule';
    case 'employee':
    case 'kiosk':
      return employeeId ? `/admin/schedule/${employeeId}` : '/admin/schedule';
    default:
      return '/admin/schedule';
  }
}
