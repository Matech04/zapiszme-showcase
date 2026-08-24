import { HttpBackend, HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, finalize, map, Observable, of, shareReplay, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

/**
 * Trzyma pojedynczy token antiforgery (CSRF) i pobiera go z backendu tylko raz na daną tożsamość.
 *
 * Wcześniej `xsrfInterceptor` robił osobny `GET /api/auth/csrf` przed KAŻDYM POST/PUT/PATCH/DELETE.
 * Przy serii ponownych prób (np. logowanie po błędnym haśle) tworzyło to lawinę sekwencyjnych,
 * cross-origin połączeń TLS do `api.*`, co bywało źródłem intermitentnych błędów sieci/„certyfikatu"
 * znikających dopiero po F5. Cache eliminuje ten churn: dopóki tożsamość się nie zmienia, token jest
 * reużywany.
 *
 * Token antiforgery jest związany z tożsamością użytkownika, więc `refresh()`/`invalidate()` MUSZĄ być
 * wołane przy każdej zmianie tożsamości (login / logout / demo / accept-invite) — inaczej stary token
 * (np. dla zalogowanego usera po wylogowaniu) nie przejdzie walidacji i backend zwróci 500.
 *
 * Używa `HttpBackend` (z pominięciem łańcucha interceptorów), żeby GET po token nie wpadał rekurencyjnie
 * w ten sam interceptor XSRF.
 */
@Injectable({ providedIn: 'root' })
export class CsrfTokenService {
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly csrfUrl = `${environment.apiBaseUrl}/api/auth/csrf`;

  private token: string | null = null;
  private inFlight$: Observable<string> | null = null;

  /** Zwraca token z cache; przy pierwszym wywołaniu (lub po unieważnieniu) pobiera go z backendu. */
  getToken(): Observable<string> {
    if (this.token) {
      return of(this.token);
    }
    return this.fetch();
  }

  /** Pobiera świeży token i nadpisuje cache. Wołać po wejściu w nową tożsamość (login/demo/invite). */
  refresh(): Observable<string> {
    this.invalidate();
    return this.fetch();
  }

  /** Czyści cache bez pobierania. Wołać po wyjściu z tożsamości (logout) — kolejny POST pobierze świeży. */
  invalidate(): void {
    this.token = null;
    this.inFlight$ = null;
  }

  private fetch(): Observable<string> {
    // Deduplikacja: równoległe żądania (zanim token się pobierze) współdzielą jeden GET.
    this.inFlight$ ??= this.http
      .get<{ token: string }>(this.csrfUrl, { withCredentials: true })
      .pipe(
        map((response) => response.token ?? ''),
        catchError(() => of('')),
        tap((token) => (this.token = token || null)),
        finalize(() => (this.inFlight$ = null)),
        shareReplay(1),
      );
    return this.inFlight$;
  }
}
