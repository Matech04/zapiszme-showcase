import { computed, inject, Injectable, signal } from '@angular/core';
import { catchError, firstValueFrom, of, tap } from 'rxjs';
import { SalonSettingsClient } from '@core/api/api-client';

/**
 * Współdzielony stan wstrzymania rezerwacji salonu + globalnego trybu serwisowego platformy.
 * Pozwala banerowi w layoucie i karcie ustawień trzymać tę samą prawdę: gdy owner wstrzyma
 * rezerwacje na stronie ustawień, baner reaguje od razu (bez przeładowania), a szybkie „Wznów"
 * z banera również aktualizuje kartę. Dodatkowo wystawia `appointmentsBlocked` — używane, by
 * ukryć/zablokować zarządzanie wizytami (kalendarz), gdy rezerwacje są wstrzymane albo trwa
 * globalny tryb serwisowy platformy.
 */
@Injectable({ providedIn: 'root' })
export class BookingPauseStore {
  private readonly client = inject(SalonSettingsClient);

  private readonly _paused = signal(false);
  /** Czy salon ma aktualnie wstrzymane rezerwacje online. */
  readonly paused = this._paused.asReadonly();

  private readonly _platformMaintenance = signal(false);
  /** Czy trwa globalny tryb serwisowy platformy (kill-switch admina). */
  readonly platformMaintenance = this._platformMaintenance.asReadonly();

  /** Zarządzanie wizytami (kalendarz) jest zablokowane, gdy salon wstrzymał rezerwacje albo trwa tryb serwisowy. */
  readonly appointmentsBlocked = computed(() => this._paused() || this._platformMaintenance());

  private loaded = false;
  private loadPromise: Promise<void> | null = null;

  private load(): Promise<void> {
    return firstValueFrom(
      this.client.get().pipe(
        tap((dto) => {
          this._paused.set(dto.bookingPaused ?? false);
          this._platformMaintenance.set(dto.platformMaintenance ?? false);
        }),
        catchError(() => of(undefined)),
      ),
    ).then(() => undefined);
  }

  /** Pobiera stan z API raz na sesję (kolejne wywołania są no-op, chyba że force=true). */
  refresh(force = false): void {
    if (this.loaded && !force) return;
    this.loaded = true;
    this.loadPromise = this.load();
  }

  /** Zapewnia, że stan jest wczytany (dla guardów tras) i zwraca gdy gotowe. */
  async ensureLoaded(): Promise<void> {
    if (!this.loaded) {
      this.loaded = true;
      this.loadPromise = this.load();
    }
    if (this.loadPromise) {
      await this.loadPromise;
    }
  }

  /** Synchronizuje stan po zapisie (np. z karty ustawień). */
  set(paused: boolean): void {
    this._paused.set(paused);
  }
}
