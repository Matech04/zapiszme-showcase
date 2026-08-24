import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';

import { AdminDrawerComponent } from '@domains/appointments/feature/visit-schedule/shared/admin-drawer.component';

/**
 * Wspólny szablon drawera formularzowego — warstwa nad {@link AdminDrawerComponent}.
 *
 * Zbiera powtarzalny chrome wszystkich drawerów z formularzem w jednym miejscu:
 *  - samodzielne śledzenie `isDesktop` (viewport + listener `resize`) — konkretne
 *    drawery i ich rodzice nie muszą już tego liczyć ani podawać,
 *  - standardową stopkę „Anuluj / [submit]" (PrimeNG `p-button`),
 *  - blokadę zamknięcia w trakcie zapisu (`submitting`).
 *
 * Konkretny drawer rzutuje treść w `[drawer-body]`, podaje etykiety/stan i nasłuchuje
 * `(submitClicked)` / `(closeRequested)`. Logika „dirty-confirm" pozostaje po stronie
 * konkretnego drawera (reaguje na `closeRequested`) — shell tylko zgłasza intencję.
 *
 * ```html
 * <app-form-drawer-shell
 *   [isOpen]="isOpen()" title="Tytuł" label="Sekcja"
 *   submitLabel="Zapisz" [submitting]="saving()" [submitDisabled]="!canSave()"
 *   (submitClicked)="formRef.submit()" (closeRequested)="requestClose()">
 *   <div drawer-body> ... </div>
 * </app-form-drawer-shell>
 * ```
 */
@Component({
  selector: 'app-form-drawer-shell',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, ButtonModule, AdminDrawerComponent],
  template: `
    <app-admin-drawer
      [isOpen]="isOpen()"
      [isDesktop]="isDesktop()"
      [title]="title()"
      [label]="label()"
      [closeable]="closeable() && !submitting()"
      (closeRequested)="closeRequested.emit()"
    >
      <div drawer-body>
        <ng-content select="[drawer-body]" />
      </div>

      <div drawer-footer class="flex gap-3">
        <p-button
          [label]="cancelLabel()"
          [outlined]="true"
          severity="secondary"
          [disabled]="submitting()"
          (onClick)="closeRequested.emit()"
          styleClass="flex-1 py-3 font-bold uppercase tracking-wider"
        />
        <p-button
          [label]="submitLabel()"
          [loading]="submitting()"
          [disabled]="submitDisabled()"
          (onClick)="submitClicked.emit()"
          styleClass="flex-[2] py-3 font-bold uppercase tracking-wider"
          [attr.data-testid]="'form-drawer-submit'"
          data-tour="form-drawer-submit"
        />
      </div>
    </app-admin-drawer>
  `,
})
export class FormDrawerShellComponent {
  readonly isOpen = input<boolean>(false);
  readonly title = input<string>('');
  /** Mały napis nad tytułem (np. „Dostępność"). */
  readonly label = input<string>('');
  readonly submitLabel = input<string>('Zapisz');
  readonly cancelLabel = input<string>('Anuluj');
  /** Gdy true — przycisk submit jest nieaktywny (np. niewypełniony formularz). */
  readonly submitDisabled = input<boolean>(false);
  /** Gdy true — spinner na submit + zablokowane zamknięcie (trwa zapis). */
  readonly submitting = input<boolean>(false);
  /** Gdy false — X i klik poza drawerem są ignorowane niezależnie od `submitting`. */
  readonly closeable = input<boolean>(true);

  readonly submitClicked = output<void>();
  readonly closeRequested = output<void>();

  private readonly destroyRef = inject(DestroyRef);

  private readonly viewport = signal(typeof window !== 'undefined' ? window.innerWidth : 1280);
  protected readonly isDesktop = computed(() => this.viewport() >= 1024);

  constructor() {
    if (typeof window !== 'undefined') {
      const handler = () => this.viewport.set(window.innerWidth);
      window.addEventListener('resize', handler, { passive: true });
      this.destroyRef.onDestroy(() => window.removeEventListener('resize', handler));
    }
  }
}
