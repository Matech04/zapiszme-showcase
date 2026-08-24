import { DOCUMENT, Injectable, inject, isDevMode } from '@angular/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { ConfirmationService } from 'primeng/api';
import { filter } from 'rxjs/operators';

/**
 * Obsługa aktualizacji PWA. Gdy service worker pobierze nową wersję aplikacji,
 * pokazujemy prośbę o odświeżenie (dane w panelu nie są cache'owane, więc
 * przeładowanie jest bezpieczne). Aktywna tylko gdy SW jest włączony
 * (produkcyjne buildy — patrz provideServiceWorker w app.config.ts).
 */
@Injectable({ providedIn: 'root' })
export class PwaUpdateService {
  /** Minimalny odstęp między sondami wersji przy powrocie na pierwszy plan. */
  private static readonly CheckThrottleMs = 15 * 60 * 1000;

  private readonly swUpdate = inject(SwUpdate);
  private readonly confirmation = inject(ConfirmationService);
  private readonly document = inject(DOCUMENT);

  private lastCheckMs = 0;

  init(): void {
    if (isDevMode()) {
      // W dev SW jest wyłączony, więc VERSION_READY nigdy nie przyjdzie. Udostępniamy ręczny
      // wyzwalacz w konsoli (`__previewUpdatePrompt()`), żeby dało się obejrzeć modal aktualizacji.
      const win = this.document.defaultView as (Window & { __previewUpdatePrompt?: () => void }) | null;
      if (win) {
        win.__previewUpdatePrompt = () => this.promptReload();
      }
    }

    if (!this.swUpdate.isEnabled) {
      return;
    }

    this.swUpdate.versionUpdates
      .pipe(filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY'))
      .subscribe(() => {
        // Modal tylko w zainstalowanej PWA (standalone). Na zwykłej stronie service worker też
        // działa, ale tam nową wersję użytkownik dostaje naturalnie przy następnym wejściu —
        // nie zawracamy mu głowy prośbą o odświeżenie w trakcie sesji.
        if (this.isStandalone()) {
          this.promptReload();
        }
      });

    // Gdy SW trafi w stan nie do odzyskania — twardy reload przywraca spójność (wszędzie).
    this.swUpdate.unrecoverable.subscribe(() => this.reload());

    this.checkOnResume();
  }

  /**
   * Sprawdza wersję przy powrocie aplikacji na pierwszy plan.
   *
   * Angular pyta o aktualizację przy rejestracji SW (`registerWhenStable:30000`) i przy nawigacji.
   * Zainstalowana PWA na iOS bywa jednak tygodniami zawieszana i wznawiana BEZ pełnego startu —
   * wtedy żaden z tych momentów nie następuje i telefon zostaje na starym buildzie, mimo że
   * serwer ma nowy. Powrót na pierwszy plan to jedyny sygnał, który w tym cyklu życia jest pewny.
   *
   * Throttle, bo `visibilitychange` potrafi lecieć przy każdym przełączeniu aplikacji — bez niego
   * robilibyśmy żądanie po `ngsw.json` kilkadziesiąt razy dziennie bez powodu.
   */
  private checkOnResume(): void {
    const target = this.document as Document & { addEventListener?: Document['addEventListener'] };
    if (typeof target.addEventListener !== 'function') {
      return;
    }

    target.addEventListener('visibilitychange', () => {
      if (this.document.visibilityState !== 'visible') {
        return;
      }

      const now = Date.now();
      if (now - this.lastCheckMs < PwaUpdateService.CheckThrottleMs) {
        return;
      }
      this.lastCheckMs = now;

      // Odrzucenie jest normalne (offline, SW jeszcze nieaktywny) — to sonda best-effort,
      // a nie ścieżka, o której użytkownik ma się dowiadywać.
      this.swUpdate.checkForUpdate().catch(() => undefined);
    });
  }

  private isStandalone(): boolean {
    const win = this.document.defaultView;
    if (!win) {
      return false;
    }
    const nav = win.navigator as Navigator & { standalone?: boolean };
    return win.matchMedia?.('(display-mode: standalone)').matches === true || nav.standalone === true;
  }

  private promptReload(): void {
    this.confirmation.confirm({
      header: 'Dostępna nowa wersja',
      message: 'Aplikacja zaktualizowała się w tle. Odśwież, aby korzystać z najnowszej wersji.',
      icon: 'pi pi-refresh',
      acceptLabel: 'Odśwież',
      rejectLabel: 'Później',
      accept: () => this.activateAndReload(),
    });
  }

  private activateAndReload(): void {
    // Nawet gdy activateUpdate() odrzuci (np. SW wyłączony w dev-preview) — przeładowujemy,
    // bo reload i tak sprowadzi najnowszą wersję i przywróci spójność.
    this.swUpdate.activateUpdate().finally(() => this.reload());
  }

  private reload(): void {
    this.document.location.reload();
  }
}
