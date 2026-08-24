import { computed, inject, Injectable, signal } from '@angular/core';
import { OnboardingClient, OnboardingStateDto } from '@core/api/api-client';
import { catchError, Observable, of, tap } from 'rxjs';

/**
 * Cache stanu onboardingu (`GET /api/onboarding/state`) — jedna instancja na sesję.
 *
 * Wzorowane na `AuthSessionService.hydrate()`: guardy wołają `ensure()` przy każdej nawigacji,
 * ale HTTP leci tylko raz. Po każdym udanym kroku kreatora komponent woła `markStale()`,
 * więc kolejny odczyt guardu pobiera świeży stan i przekierowuje na właściwy krok.
 */
@Injectable({ providedIn: 'root' })
export class OnboardingStateService {
  private readonly client = inject(OnboardingClient);

  private readonly stateSignal = signal<OnboardingStateDto | null>(null);
  private readonly loaded = signal(false);

  /** Ostatnio pobrany stan (albo `null`, gdy jeszcze nie pobrano / błąd). */
  readonly state = this.stateSignal.asReadonly();
  readonly completed = computed(() => !!this.stateSignal()?.onboardingCompleted);

  /** Pobiera stan raz per sesję; kolejne wywołania zwracają cache bez HTTP. */
  ensure(): Observable<OnboardingStateDto | null> {
    if (this.loaded()) {
      return of(this.stateSignal());
    }
    return this.fetch();
  }

  /** Wymuszony re-fetch (ignoruje cache). */
  refresh(): Observable<OnboardingStateDto | null> {
    return this.fetch();
  }

  /** Oznacza cache jako nieaktualny — kolejny `ensure()` pobierze świeży stan. */
  markStale(): void {
    this.loaded.set(false);
  }

  private fetch(): Observable<OnboardingStateDto | null> {
    return this.client.getState().pipe(
      tap((state) => {
        this.stateSignal.set(state);
        this.loaded.set(true);
      }),
      catchError(() => {
        // Brak stanu (np. 401 zanim authGuard przekieruje) — nie cache'ujemy błędu.
        this.stateSignal.set(null);
        this.loaded.set(false);
        return of(null);
      }),
    );
  }
}
