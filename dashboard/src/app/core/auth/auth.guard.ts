import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSessionService } from './auth-session.service';
import { offlineUrlTree } from './offline-route';
import { map } from 'rxjs';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthSessionService);
  const router = inject(Router);

  return auth.hydrate().pipe(
    map((outcome) => {
      switch (outcome.kind) {
        case 'authenticated':
          return true;
        // Sieć nie odpowiedziała — nie wiemy, czy sesja wygasła, więc NIE odsyłamy na `/login`
        // (to właśnie było źródłem „wylogowuje mnie cały czas" w PWA).
        case 'unavailable':
          return offlineUrlTree(router, state.url);
        default:
          return router.createUrlTree(['/login']);
      }
    }),
  );
};
