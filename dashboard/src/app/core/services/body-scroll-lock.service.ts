import { DOCUMENT } from '@angular/common';
import { inject, Injectable, Renderer2, RendererFactory2 } from '@angular/core';

/**
 * Ref-licznikowa blokada scrolla `body`, współdzielona przez wszystkie dolne
 * sheety/drawery (admin-drawer, appointment-detail-sheet, month-day-sheet,
 * calendar-filters).
 *
 * Robi dwie rzeczy, dopóki otwarty jest ≥1 sheet:
 *  1. `body { overflow: hidden }` — blokada scrolla tła.
 *  2. dokleja `body.admin-drawer-open` — marker dla CSS, który chowa dolny
 *     mobile navbar.
 *
 * Dlaczego (2): navbar (`app-mobile-nav`) to glass-card z `backdrop-filter`.
 * Na iOS Safari taka warstwa maluje się PONAD `position: fixed` drawerem
 * PrimeNG i przykrywała stopkę drawera (przyciski „Anuluj" / „Zmień termin");
 * Android układał stacking poprawnie i navbar znikał pod drawerem. Deterministyczne
 * schowanie navbara naprawia oba zachowania niezależnie od quirków stackingu.
 *
 * Ref-licznik jest istotny: z `appointment-detail-sheet` można otworzyć dialog
 * zmiany terminu — przy per-komponentowej blokadzie zamknięcie wewnętrznego
 * panelu odblokowywało scroll mimo wciąż otwartego panelu zewnętrznego.
 */
@Injectable({ providedIn: 'root' })
export class BodyScrollLockService {
  private readonly document = inject(DOCUMENT);
  private readonly renderer: Renderer2 = inject(RendererFactory2).createRenderer(null, null);

  private count = 0;
  private previousOverflow: string | null = null;

  lock(): void {
    if (this.count++ > 0) return;
    this.previousOverflow = this.document.body.style.overflow;
    this.renderer.setStyle(this.document.body, 'overflow', 'hidden');
    this.renderer.addClass(this.document.body, 'admin-drawer-open');
  }

  unlock(): void {
    if (this.count === 0) return;
    if (--this.count > 0) return;
    this.renderer.setStyle(this.document.body, 'overflow', this.previousOverflow ?? '');
    this.renderer.removeClass(this.document.body, 'admin-drawer-open');
    this.previousOverflow = null;
  }
}
