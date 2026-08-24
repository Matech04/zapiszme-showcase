import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { AuthSessionService } from './auth-session.service';
import { offlineUrlTree } from './offline-route';
import { SalonSettingsClient, StaffCalendarVisibilityPolicy } from '@core/api/api-client';

/**
 * Dostęp do widoku „Zespół":
 *  - Owner / Manager — zawsze (pełne zarządzanie).
 *  - Employee — tylko gdy właściciel włączył widoczność zespołu
 *    (`StaffCalendarVisibilityPolicy` ≥ TeamReadOnly). Wtedy widzi roster tylko-do-odczytu.
 *    Przy OwnCalendarOnly (ścisły scoping) — odesłanie na własny kalendarz.
 *  - Inni (kiosk / systemAdmin) — odesłanie.
 *
 * Async (fetch ustawień) — guard blokuje nawigację do czasu rozstrzygnięcia, więc nie ma
 * mignięcia rostera przed sprawdzeniem polityki.
 */
export const teamViewGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const auth = inject(AuthSessionService);
  const settings = inject(SalonSettingsClient);

  const redirect = () => router.createUrlTree(['/admin/schedule']);

  return auth.hydrate().pipe(
    switchMap((outcome) => {
      // Bez sesji z serwera rola jest nieznana — odesłanie na kalendarz byłoby mylące, to problem sieci.
      if (outcome.kind === 'unavailable') {
        return of(offlineUrlTree(router, state.url));
      }
      const role = auth.currentRole();
      if (role === 'owner' || role === 'manager') return of(true);
      if (role !== 'employee') return of(redirect());

      return settings.get().pipe(
        map((s) => {
          const p = s?.staffCalendarVisibilityPolicy;
          return p === StaffCalendarVisibilityPolicy.TeamReadOnly ||
            p === StaffCalendarVisibilityPolicy.TeamFull
            ? true
            : redirect();
        }),
        catchError(() => of(redirect())),
      );
    }),
  );
};
