import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { HasUnsavedChanges, isHasUnsavedChanges } from './has-unsaved-changes';

const DIALOG_MESSAGE =
  'Masz niezapisane zmiany. Czy na pewno chcesz opuścić formularz?';
const DIALOG_HEADER = 'Niezapisane zmiany';

/**
 * CanDeactivate dla formularzy implementujących `HasUnsavedChanges`.
 * Korzysta z PrimeNG `ConfirmationService` (spójny styl z resztą aplikacji)
 * zamiast natywnego `window.confirm`.
 */
export const dirtyFormGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => {
  if (!isHasUnsavedChanges(component) || !component.hasUnsavedChanges()) {
    return true;
  }

  const confirmation = inject(ConfirmationService);

  return new Promise<boolean>((resolve) => {
    confirmation.confirm({
      header: DIALOG_HEADER,
      message: DIALOG_MESSAGE,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Opuść formularz',
      rejectLabel: 'Pozostań',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => resolve(true),
      reject: () => resolve(false),
    });
  });
};
