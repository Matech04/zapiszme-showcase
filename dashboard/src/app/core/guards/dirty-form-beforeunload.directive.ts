import { DestroyRef, Directive, HostListener, inject, input } from '@angular/core';

/**
 * Dyrektywa hosta dla komponentów formularzy. Gdy `appDirtyFormBeforeUnload`
 * przyjmuje `true`, blokuje natywne zamknięcie/odświeżenie karty natywnym
 * dialogiem przeglądarki (komunikat ustala przeglądarka, nie mamy nad nim kontroli).
 *
 * Łączy się z `dirtyFormGuard` (CanDeactivate), który chroni nawigacje
 * w ramach aplikacji Angular — razem dają pełne pokrycie.
 */
@Directive({
  selector: '[appDirtyFormBeforeUnload]',
  standalone: true,
})
export class DirtyFormBeforeUnloadDirective {
  /** Czy formularz aktualnie ma niezapisane zmiany. */
  isDirty = input<boolean>(false, { alias: 'appDirtyFormBeforeUnload' });

  constructor() {
    inject(DestroyRef);
  }

  @HostListener('window:beforeunload', ['$event'])
  handleBeforeUnload(event: BeforeUnloadEvent): void {
    if (!this.isDirty()) return;
    event.preventDefault();
    // Wymagane przez starsze przeglądarki — zwracana wartość ignorowana w nowoczesnych.
    event.returnValue = '';
  }
}
