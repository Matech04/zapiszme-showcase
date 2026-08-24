import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSessionService } from './auth-session.service';
import { map } from 'rxjs';

/**
 * Na `/login` — jeśli cookie sesji jest aktywne, idź do panelu zamiast pokazywać formularz.
 *
 * Przy `unavailable` (brak sieci) pokazujemy formularz: użytkownik i tak trafił tu świadomie, a próba
 * logowania zwróci czytelny błąd połączenia. Odesłanie na `/offline` zapętliłoby wejście na `/login`.
 */
export const redirectWhenAuthenticatedGuard: CanActivateFn = () => {
  const auth = inject(AuthSessionService);
  const router = inject(Router);

  return auth
    .hydrate()
    .pipe(map((outcome) => (outcome.kind === 'authenticated' ? router.createUrlTree(['/admin/resources']) : true)));
};
