import { Injectable } from '@angular/core';
import { MessageService, ToastMessageOptions } from 'primeng/api';

/**
 * `MessageService` z twardym limitem jednocześnie widocznych toastów.
 *
 * PrimeNG 21 nie ma takiego wejścia na `<p-toast>` (są tylko `preventDuplicates`
 * i `preventOpenDuplicates`), więc limit wymuszamy po stronie usługi. Bez tego seria błędów
 * (np. kilka nieudanych żądań pod rząd) zalewa ekran stosem toastów.
 *
 * Nadmiarowe komunikaty NIE są gubione — czekają w kolejce i wchodzą, gdy zwolni się miejsce.
 * Zwolnienie sygnalizuje `<p-toast (onClose)>` w `App`: PrimeNG emituje je zarówno po
 * auto-wygaśnięciu, jak i po kliknięciu „x" (patrz `ToastItem.onAfterLeave`), więc nie musimy
 * duplikować odliczania `life` własnym timerem.
 *
 * WAŻNE: liczy poprawnie tylko przy JEDNYM `<p-toast>` w aplikacji (montaż w `App`). Dodatkowe
 * outlety bez `key` renderowałyby te same wiadomości po raz drugi i rozjechałyby licznik.
 */
@Injectable()
export class CappedMessageService extends MessageService {
  /** Trzy naraz — tyle mieści się na ekranie telefonu bez zasłaniania treści. */
  private static readonly MaxVisible = 3;

  /** Bezpiecznik na wypadek lawiny: starsze oczekujące odpadają, świeższe są istotniejsze. */
  private static readonly MaxPending = 10;

  private visible = 0;
  private readonly pending: ToastMessageOptions[] = [];

  override add(message: ToastMessageOptions): void {
    if (this.visible < CappedMessageService.MaxVisible) {
      this.visible++;
      super.add(message);
      return;
    }

    this.pending.push(message);
    if (this.pending.length > CappedMessageService.MaxPending) {
      this.pending.shift();
    }
  }

  override addAll(messages: ToastMessageOptions[]): void {
    for (const message of messages) {
      this.add(message);
    }
  }

  override clear(key?: string): void {
    this.visible = 0;
    this.pending.length = 0;
    super.clear(key);
  }

  /** Wołane z `(onClose)` na `<p-toast>` — zwalnia slot i wpuszcza kolejny komunikat z kolejki. */
  release(): void {
    this.visible = Math.max(0, this.visible - 1);

    const next = this.pending.shift();
    if (next) {
      this.visible++;
      super.add(next);
    }
  }

  /** Dla testów/diagnostyki. */
  get pendingCount(): number {
    return this.pending.length;
  }

  get visibleCount(): number {
    return this.visible;
  }
}
