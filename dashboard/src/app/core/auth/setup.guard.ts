import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { AuthSessionService } from './auth-session.service';
import { OnboardingStateService } from './onboarding-state.service';
import { map, of, switchMap } from 'rxjs';

/**
 * Guard na `/setup/**`: wymaga zalogowanej sesji, a ukończonego właściciela odsyła do panelu
 * (żeby nie wchodził ponownie w kreator). Wybór właściwego pod-kroku po odświeżeniu robi
 * `SetupIndexRedirectComponent` na trasie indeksowej (na podstawie `state.nextStep`).
 */
export const setupGuard: CanActivateFn = (route) => {
  const auth = inject(AuthSessionService);
  const onboarding = inject(OnboardingStateService);
  const router = inject(Router);

  return auth.hydrate().pipe(
    switchMap((session) => {
      if (!session) {
        return of(router.createUrlTree(['/login'], { queryParams: { returnUrl: '/setup' } }));
      }
      return onboarding.ensure().pipe(
        map((state) => {
          if (!state?.onboardingCompleted) {
            return true;
          }
          // „Gotowe" jest WYJĄTKIEM od odsyłania ukończonych do panelu: ten krok pokazuje się
          // dopiero PO oznaczeniu onboardingu jako ukończonego, więc odświeżenie na nim wyrzucało
          // właścicielkę do panelu i zabierało jej ekran z publicznym linkiem salonu — jedyne
          // miejsce, gdzie ten link dostaje wraz z „Kopiuj" i „Otwórz jak klient".
          if (firstChildPath(route) === 'done') {
            return true;
          }
          return router.createUrlTree(['/admin/schedule']);
        }),
      );
    }),
  );
};

/** Ścieżka pod-kroku, na który leci nawigacja (guard siedzi na rodzicu `/setup`). */
function firstChildPath(route: ActivatedRouteSnapshot): string | null {
  return route.firstChild?.routeConfig?.path ?? null;
}

/** Mapa `OnboardingStateDto.nextStep` → trasa pod-kroku kreatora (dla wznowienia/refresh). */
export function nextStepToRoute(nextStep: string | undefined | null): string {
  switch (nextStep) {
    case 'Profile':
      return '/setup/profile';
    case 'Industry':
      return '/setup/industry';
    // „Rules" to wejście w trójkę zapisy → terminy → godziny (patrz komentarz przy `nextStep`
    // w GetOnboardingStateQuery), a „Schedule" znaczy wąsko: grafik zapisany, kreator niedomknięty.
    case 'Rules':
      return '/setup/rules';
    case 'Schedule':
      return '/setup/schedule';
    // Nie krok kreatora, tylko ślepy zaułek: konto po dezaktywacji pracownika. Trafia tu, bo
    // `onboardingGuard` odbija każdy nieukończony onboarding na /setup — ekran ma to nazwać po
    // imieniu zamiast pokazywać zwolnionej osobie kreator zakładania salonu.
    case 'InactiveAccount':
      return '/setup/konto-nieaktywne';
    case 'Completed':
      return '/admin/schedule';
    default:
      return '/setup/profile';
  }
}
