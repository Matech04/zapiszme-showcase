import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';

/**
 * Sygnał „serwer odrzucił sesję" — 401 z żądania innego niż sonda `/api/auth/me`.
 *
 * Celowo BEZ ŻADNYCH zależności. `errorInterceptor` nie może wstrzyknąć `AuthSessionService`, bo ten
 * ciągnie `AuthClient` → `HttpClient` → z powrotem łańcuch interceptorów. Ten serwis rozcina cykl:
 * interceptor wyłącznie publikuje, a całą reakcję (czyszczenie sesji + nawigacja) montuje
 * `AuthSessionService`, który jako jedyny wie, czy jest w ogóle co wygaszać.
 */
@Injectable({ providedIn: 'root' })
export class SessionExpiryService {
  private readonly expired = new Subject<void>();

  /** Emituje przy każdym 401 z żądania domenowego — także w serii, gdy padnie kilka naraz. */
  readonly expired$: Observable<void> = this.expired.asObservable();

  notifyExpired(): void {
    this.expired.next();
  }
}
