import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthSessionService } from './auth-session.service';
import { offlineUrlTree } from './offline-route';

/**
 * Wpuszcza tylko sesje MAJĄCE kontekst salonu. Odsyła admina platformy bez własnego pracownika —
 * dla niego każdy endpoint tenant-scoped kończy się 400 `tenant.missing`.
 *
 * Powód powstania: `/admin/schedule` nie miało żadnego guarda, więc admin platformy, który tam
 * trafił (zapamiętany URL, restore karty, ręczne wejście), montował pełny kalendarz. Ten strzelał
 * w /api/Appointments, /api/Employees i /api/SalonSettings — na produkcji 11 odbitych żądań
 * w 35 sekund, zero szans na załadowanie widoku. `defaultAdminRouteForRole` kieruje admina do
 * `/admin/system/tenants` i to jest jedyne miejsce, w którym ma on co robić.
 *
 * Predykat celowo NIE jest samym `role === 'systemAdmin'`:
 * • admin, który ma też własny salon, MA `employeeId` — zablokowanie go odcięłoby mu jego kalendarz;
 * • w trybie wsparcia (impersonacja) `/api/auth/me` zwraca rolę `Owner` i id salonu, więc sesja
 *   przechodzi tędy normalnie i wsparcie działa bez zmian.
 */
export const salonContextGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const auth = inject(AuthSessionService);

  return auth.hydrate().pipe(
    map((outcome) => {
      // Bez odpowiedzi z serwera rola jest nieznana — to problem sieci, nie uprawnień.
      if (outcome.kind === 'unavailable') {
        return offlineUrlTree(router, state.url);
      }

      const bezKontekstuSalonu =
        auth.currentRole() === 'systemAdmin' && auth.currentEmployeeId() === null;

      return bezKontekstuSalonu ? router.createUrlTree(['/admin/system/tenants']) : true;
    }),
  );
};
