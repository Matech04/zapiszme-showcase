import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSessionService } from './auth-session.service';
import { OnboardingStateService } from './onboarding-state.service';
import { map, of, switchMap } from 'rxjs';

/**
 * Twardy guard na `/admin/**`: dopóki właściciel nie ukończył kreatora, panel jest niedostępny.
 *
 * Stan czytamy z `OnboardingStateService` (cache per sesja — nie strzelamy do API na każdej nawigacji).
 * Salony legacy/demo/admin backend backfilluje jako `onboardingCompleted = true`, więc przechodzą.
 * Świeży właściciel ma sesję z `tenantId == null` — guard przekierowuje na `/setup`, zanim
 * jakikolwiek widok panelu (zakładający tenant) zdąży się załadować.
 */
export const onboardingGuard: CanActivateFn = () => {
  const auth = inject(AuthSessionService);
  const onboarding = inject(OnboardingStateService);
  const router = inject(Router);

  return auth.hydrate().pipe(
    switchMap((session) => {
      if (!session) {
        return of(router.createUrlTree(['/login']));
      }
      return onboarding.ensure().pipe(
        map((state) => (state?.onboardingCompleted ? true : router.createUrlTree(['/setup']))),
      );
    }),
  );
};
