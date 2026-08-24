import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { switchMap } from 'rxjs';
import { CsrfTokenService } from '../auth/csrf-token.service';

const XSRF_HEADER = 'X-XSRF-TOKEN';
const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export const xsrfInterceptor: HttpInterceptorFn = (req, next) => {
  if (!requiresXsrf(req)) {
    return next(req);
  }

  // Token pobierany z cache (CsrfTokenService), a nie osobnym GET-em na każdy POST — patrz komentarz
  // w samym serwisie: to eliminuje lawinę cross-origin połączeń przy seriach ponownych żądań.
  const csrf = inject(CsrfTokenService);
  return csrf.getToken().pipe(
    switchMap((token) => next(token ? withXsrf(req, token) : req)),
  );
};

function requiresXsrf(req: HttpRequest<unknown>): boolean {
  if (!unsafeMethods.has(req.method.toUpperCase()) || req.headers.has(XSRF_HEADER)) {
    return false;
  }

  const url = req.url.startsWith('http') ? req.url : `${environment.apiBaseUrl}${req.url}`;
  return url.startsWith(environment.apiBaseUrl) && !url.includes('/api/auth/csrf');
}

function withXsrf(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    headers: req.headers.set(XSRF_HEADER, token),
  });
}

