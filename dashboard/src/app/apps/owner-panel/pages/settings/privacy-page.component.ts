import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import {
  SettingChoiceComponent,
  type SettingChoiceOption,
} from '@shared/ui/settings/setting-choice.component';
import { SettingsSubpageComponent } from './settings-subpage.component';
import { SalonSettingsStore } from './salon-settings.store';

/** Ustawienia → Prywatność: przechowywanie historii wizyt + jednorazowe, trwałe usunięcie zebranej historii. */
@Component({
  selector: 'app-privacy-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SettingsSubpageComponent, FormLayoutComponent, SettingChoiceComponent],
  template: `
    <app-settings-subpage
      title="Prywatność"
      subtitle="Zdecyduj, czy salon przechowuje zakończone i anulowane wizyty z przeszłości."
    >
      <app-form-layout
        title="Prywatność"
        [isEdit]="true"
        [confirmOnCancel]="true"
        testId="privacy"
        submitButtonLabel="Zapisz"
        (submit)="store.onSave()"
        (cancel)="onCancel()"
      >
        <app-setting-choice
          label="Historia wizyt"
          description="Decyduje, czy salon przechowuje zakończone i anulowane wizyty z przeszłości."
          [options]="historyOptions"
          [value]="store.salonModel().doNotRetainAppointmentHistory"
          (valueChange)="store.setDoNotRetainAppointmentHistory($event)"
        >
          @if (store.salonModel().doNotRetainAppointmentHistory) {
            <div
              data-testid="no-history-warning"
              class="rounded-lg border border-red-300 dark:border-red-800 bg-red-50 dark:bg-red-950/40 px-3 py-2.5 flex items-start gap-2"
            >
              <i class="pi pi-exclamation-triangle text-red-600 dark:text-red-400 mt-0.5"></i>
              <p class="text-xs text-red-700 dark:text-red-300 m-0 leading-relaxed">
                <strong>Uwaga — operacja nieodwracalna.</strong> Po włączeniu zakończone i anulowane
                wizyty z przeszłości będą automatycznie i <strong>trwale usuwane</strong> z bazy (bez
                możliwości odzyskania). Bieżące i przyszłe wizyty oraz przypomnienia pozostają nietknięte.
              </p>
            </div>

            <div
              data-testid="purge-history-box"
              class="rounded-lg border border-red-300 dark:border-red-800 bg-white dark:bg-surface-50/40 px-3 py-3"
            >
              <p class="text-xs text-surface-600 dark:text-surface-400 m-0 mb-2 leading-relaxed">
                Masz już zapisane wizyty z przeszłości? Usuń je trwale jednym kliknięciem (zakończone i
                anulowane sprzed dziś). Bieżące i przyszłe pozostaną.
              </p>
              @if (!store.purgeConfirming()) {
                <button
                  type="button"
                  data-testid="purge-history-start"
                  class="rounded-lg border-2 border-red-500 text-red-700 dark:text-red-300 px-3 py-2 text-sm font-black transition-colors hover:bg-red-50 dark:hover:bg-red-950/30"
                  (click)="store.purgeConfirming.set(true)"
                >
                  <i class="pi pi-trash mr-1.5"></i>Usuń dotychczasową historię teraz
                </button>
              } @else {
                <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
                  <span class="text-xs font-bold text-red-700 dark:text-red-300">
                    Na pewno? Tej operacji nie można cofnąć.
                  </span>
                  <div class="flex gap-2">
                    <button
                      type="button"
                      data-testid="purge-history-confirm"
                      class="rounded-lg bg-red-600 text-white px-3 py-2 text-sm font-black transition-colors hover:bg-red-700 disabled:opacity-50"
                      [disabled]="store.purgingHistory()"
                      (click)="store.purgeAppointmentHistory()"
                    >
                      {{ store.purgingHistory() ? 'Usuwanie…' : 'Tak, usuń trwale' }}
                    </button>
                    <button
                      type="button"
                      data-testid="purge-history-cancel"
                      class="rounded-lg border-2 border-surface-300 text-surface-700 px-3 py-2 text-sm font-bold transition-colors hover:bg-surface-100 disabled:opacity-50"
                      [disabled]="store.purgingHistory()"
                      (click)="store.purgeConfirming.set(false)"
                    >
                      Anuluj
                    </button>
                  </div>
                </div>
              }
            </div>
          }
        </app-setting-choice>
      </app-form-layout>
    </app-settings-subpage>
  `,
})
export class PrivacyPageComponent {
  protected readonly store = inject(SalonSettingsStore);
  private readonly router = inject(Router);

  protected readonly historyOptions: readonly SettingChoiceOption[] = [
    {
      value: false,
      title: 'Przechowuj historię',
      description: 'Zakończone i anulowane wizyty zostają w systemie (domyślne).',
      tone: 'accent',
      testId: 'retain-history-on',
    },
    {
      value: true,
      title: 'Nie przechowuj historii',
      description: 'Zakończone i anulowane wizyty z przeszłości są trwale usuwane.',
      tone: 'danger',
      testId: 'retain-history-off',
    },
  ];

  protected onCancel(): void {
    void this.router.navigateByUrl('/admin/settings');
  }
}
