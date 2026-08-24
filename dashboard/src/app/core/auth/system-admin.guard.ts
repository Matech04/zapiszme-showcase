import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSessionService } from './auth-session.service';

export const systemAdminGuard: CanActivateFn = () => {
  const session = inject(AuthSessionService).session();
  const isAdmin = session?.roles?.some(
    (r) => r.toLowerCase() === 'admin',
  ) ?? false;

  if (isAdmin) return true;
  return inject(Router).createUrlTree(['/admin/schedule']);
};
