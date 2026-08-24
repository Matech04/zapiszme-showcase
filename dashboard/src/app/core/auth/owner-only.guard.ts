import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { AuthSessionService } from './auth-session.service';
import { offlineUrlTree } from './offline-route';
import { map } from 'rxjs';

/** Dostęp wyłącznie dla Ownera. */
export const ownerOnlyGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const auth = inject(AuthSessionService);
  const messages = inject(MessageService);

  return auth.hydrate().pipe(
    map((outcome) => {
      // Bez sesji z serwera rola jest nieznana — „Brak dostępu" byłoby mylące, to problem sieci.
      if (outcome.kind === 'unavailable') {
        return offlineUrlTree(router, state.url);
      }
      const role = auth.currentRole();
      if (role === 'owner') {
        return true;
      }
      messages.add({
        severity: 'warn',
        summary: 'Brak dostępu',
        detail: 'Ta sekcja jest dostępna tylko dla właściciela salonu.',
        life: 3500,
      });
      return router.createUrlTree(['/admin/schedule']);
    }),
  );
};
