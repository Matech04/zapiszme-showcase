import { Router, UrlTree } from '@angular/router';

export const OFFLINE_PATH = '/offline';
export const OFFLINE_RETURN_URL_PARAM = 'returnUrl';

/**
 * Ekran „brak połączenia" zamiast `/login`, gdy sonda sesji nie doleciała do API.
 *
 * Odesłanie na `/login` byłoby kłamstwem (cookie sesji nadal może być ważne) i do niczego nie prowadzi
 * — formularz logowania bez sieci też nie zadziała. Zapamiętujemy cel nawigacji, żeby po powrocie
 * sieci wrócić dokładnie tam, gdzie użytkownik zmierzał.
 */
export function offlineUrlTree(router: Router, returnUrl?: string): UrlTree {
  const isReturnable = !!returnUrl && !returnUrl.startsWith(OFFLINE_PATH);
  return router.createUrlTree([OFFLINE_PATH], {
    queryParams: isReturnable ? { [OFFLINE_RETURN_URL_PARAM]: returnUrl } : undefined,
  });
}
