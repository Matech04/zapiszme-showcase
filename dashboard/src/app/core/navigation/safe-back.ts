import { inject } from '@angular/core';
import { Location } from '@angular/common';
import { Router } from '@angular/router';

/**
 * Bezpieczne „cofnij” w obrębie aplikacji. Gdy historia ma więcej niż jeden wpis
 * i zewnętrzny referrer NIE jest częścią naszej aplikacji, używamy `Location.back()`
 * (zachowanie pojedynczego routera Angular). W przeciwnym wypadku — nawigujemy na
 * wskazany fallback, żeby nie wyrzucić użytkownika poza aplikację.
 */
export function safeBack(fallback: string | string[]): void {
  const location = inject(Location);
  const router = inject(Router);

  const sameOriginReferrer =
    typeof document !== 'undefined'
      && typeof document.referrer === 'string'
      && document.referrer.startsWith(window.location.origin);

  const hasHistory =
    typeof window !== 'undefined' && typeof window.history !== 'undefined' && window.history.length > 1;

  if (hasHistory && sameOriginReferrer) {
    location.back();
    return;
  }

  const commands = Array.isArray(fallback) ? fallback : [fallback];
  void router.navigate(commands);
}

/** Wariant „bez DI”, do użycia w komponentach które już mają wstrzyknięte `Location`/`Router`. */
export function safeBackWith(
  location: Location,
  router: Router,
  fallback: string | string[],
): void {
  const sameOriginReferrer =
    typeof document !== 'undefined'
      && typeof document.referrer === 'string'
      && document.referrer.startsWith(window.location.origin);

  const hasHistory =
    typeof window !== 'undefined' && typeof window.history !== 'undefined' && window.history.length > 1;

  if (hasHistory && sameOriginReferrer) {
    location.back();
    return;
  }

  const commands = Array.isArray(fallback) ? fallback : [fallback];
  void router.navigate(commands);
}
