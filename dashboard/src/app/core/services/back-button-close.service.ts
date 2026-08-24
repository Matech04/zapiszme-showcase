import { DOCUMENT, inject, Injectable } from '@angular/core';

/**
 * Sprawia, że sprzętowy/gestowy przycisk „wstecz" (Android, PWA) najpierw ZAMYKA otwartą nakładkę
 * (drawer, modal), zamiast cofać treść na ekranie.
 *
 * Mechanika: przy otwarciu nakładki dokładamy sztuczny wpis do historii (`pushState`, ten sam URL).
 * Naciśnięcie „wstecz" emituje `popstate` — wtedy zamykamy najwyższą nakładkę i NIE nawigujemy (URL
 * się nie zmienił). Zamknięcie z UI sprząta dołożony wpis (`history.back()` z flagą, by nie zamykać
 * ponownie). Obsługuje zagnieżdżone nakładki (stos LIFO).
 */
@Injectable({ providedIn: 'root' })
export class BackButtonCloseService {
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly closers: Array<() => void> = [];
  private suppressNextPop = false;

  constructor() {
    this.window?.addEventListener('popstate', () => this.onPopState());
  }

  /** Wywołaj przy OTWARCIU nakładki. `close` zostanie użyte, gdy użytkownik naciśnie „wstecz". */
  push(close: () => void): void {
    if (!this.window) {
      return;
    }
    this.closers.push(close);
    this.window.history.pushState({ __overlay: this.closers.length }, '');
  }

  /** Wywołaj przy zamknięciu nakładki z UI (nie przez „wstecz") — sprząta dołożony wpis historii. */
  pop(close: () => void): void {
    if (!this.window) {
      return;
    }
    const index = this.closers.lastIndexOf(close);
    if (index === -1) {
      return;
    }
    this.closers.splice(index, 1);
    // Cofamy dołożony wpis tylko jeśli wciąż jest na wierzchu (użytkownik nie nawigował dalej).
    if ((this.window.history.state as { __overlay?: number } | null)?.__overlay) {
      this.suppressNextPop = true;
      this.window.history.back();
    }
  }

  private onPopState(): void {
    if (this.suppressNextPop) {
      this.suppressNextPop = false;
      return;
    }
    // „Wstecz" — zamknij najwyższą nakładkę. Wpis historii już został zdjęty przez przeglądarkę.
    this.closers.pop()?.();
  }
}
