import { Component, computed, HostListener, inject, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ConfirmationService } from 'primeng/api';

@Component({
  selector: 'app-form-layout',
  standalone: true,
  imports: [ButtonModule],
  template: `
    <div class="admin-glass-card flex flex-col h-full rounded-4xl overflow-hidden">
      
      <header
        [attr.data-testid]="testId() + '-header'"
        class="flex items-center gap-3 p-6 border-b border-surface-200/70 dark:border-surface-200/70 shrink-0">
        <button
          type="button"
          class="grid size-9 place-items-center rounded-full text-surface-500 hover:text-primary hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
          (click)="handleCancel()"
          [attr.aria-label]="'Wróć'">
          <i class="pi pi-chevron-left"></i>
        </button>
        <span [attr.data-testid]="testId() + '-title'" class="text-xl font-black tracking-tight text-surface-900">
          {{ headerText() }}
        </span>
      </header>

      <div class="flex-1 overflow-y-auto px-6 py-6 flex flex-col gap-6">
        <ng-content></ng-content>
      </div>

      <footer class="p-6 border-t border-surface-200/70 dark:border-surface-200/70 bg-surface-50/35 dark:bg-surface-950/20">
        <div class="flex flex-row gap-4 items-center">
          
          <p-button
            [attr.data-testid]="testId() + '-cancel-btn'"
            label="Anuluj"
            [outlined]="true"
            (onClick)="handleCancel()"
            styleClass="flex-1 py-3 font-bold uppercase tracking-wider"
          />

          <p-button
            [attr.data-testid]="testId() + '-submit-btn'"
            data-tour="form-submit"
            [label]="footerButtonLabel()"
            (onClick)="submit.emit()"
            styleClass="flex-[2] py-3 font-bold uppercase tracking-wider"
          />
        </div>
      </footer>
    </div>
  `
})
export class FormLayoutComponent {
  submit = output();
  cancel = output();
  title = input.required<string>();
  isEdit = input<boolean>(false);
  testId = input.required<string>();
  confirmOnCancel = input<boolean>(false);
  cancelConfirmationText = input<string>('Masz niezapisane zmiany. Czy na pewno chcesz opuścić formularz?');
  /** Gdy `true`, ostrzeżemy użytkownika natywnym dialogiem przed zamknięciem/odświeżeniem karty. */
  hasUnsavedChanges = input<boolean>(false);
  /** Gdy podane, nagłówek i przycisk używają tej etykiety zamiast "Edytuj" / "Dodaj" + tytuł. */
  submitButtonLabel = input<string | undefined>(undefined);

  @HostListener('window:beforeunload', ['$event'])
  protected handleBeforeUnload(event: BeforeUnloadEvent): void {
    if (!this.hasUnsavedChanges()) return;
    event.preventDefault();
    event.returnValue = '';
  }

  private defaultActionLabel = computed(() => (this.isEdit() ? 'Edytuj' : 'Dodaj'));

  headerText = computed(() =>
    this.submitButtonLabel() != null ? this.title() : `${this.defaultActionLabel()} ${this.title()}`,
  );

  footerButtonLabel = computed(() => this.submitButtonLabel() ?? this.defaultActionLabel());

  private confirmation = inject(ConfirmationService);

  handleCancel(): void {
    if (!this.confirmOnCancel()) {
      this.cancel.emit();
      return;
    }
    this.confirmation.confirm({
      header: 'Niezapisane zmiany',
      message: this.cancelConfirmationText(),
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Opuść formularz',
      rejectLabel: 'Pozostań',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.cancel.emit(),
    });
  }
}