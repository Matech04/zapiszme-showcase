import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FormField } from '@angular/forms/signals';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';
import { FormFieldSelectComponent } from '@shared/ui/forms/form-field-select.component';
import { SettingsSubpageComponent } from './settings-subpage.component';
import { SalonSettingsStore } from './salon-settings.store';

/** Ustawienia → Dane salonu: nazwa, publiczny link (slug), interwał slotów, strefa czasowa, waluta. */
@Component({
  selector: 'app-salon-brand-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SettingsSubpageComponent,
    FormLayoutComponent,
    FormFieldComponent,
    FormFieldSelectComponent,
    FormField,
  ],
  template: `
    <app-settings-subpage
      title="Dane salonu"
      subtitle="Nazwa, publiczny adres rezerwacji oraz podstawowe parametry: strefa czasowa i waluta."
    >
      <app-form-layout
        title="Dane salonu"
        [isEdit]="true"
        [confirmOnCancel]="true"
        testId="salon-brand"
        submitButtonLabel="Zapisz"
        (submit)="store.onSave()"
        (cancel)="onCancel()"
      >
        <div class="grid grid-cols-1 gap-6">
          <app-form-field
            testId="salon-name"
            label="Nazwa salonu"
            id="name"
            placeholder="np. Architektoniczna Strefa Wellness"
            [formField]="store.salonForm.name"
          />
          <app-form-field
            testId="salon-slug"
            data-tour="salon-slug"
            label="Link do kalendarza zapisz.me/nazwa"
            id="slug"
            placeholder="np. salon-ania"
            [formField]="store.salonForm.slug"
          />
          <p class="text-xs text-surface-500 dark:text-surface-500 -mt-2 font-sans leading-relaxed">
            Nazwa: małe litery, spacje zostaną zastąpione myślnikami. Używany w adresie publicznej
            rezerwacji.
          </p>

          <app-form-field-select
            testId="salon-timezone"
            label="Strefa czasowa salonu"
            id="timeZoneId"
            placeholder="Wybierz strefę"
            [filter]="true"
            [value]="store.salonModel().timeZoneId"
            (valueChange)="store.onTimeZoneChange($event)"
            [options]="store.timeZoneOptions"
          />
          <p class="text-xs text-surface-500 dark:text-surface-500 -mt-2 font-sans leading-relaxed">
            Strefa wpływa na interpretację godzin grafiku i wizyt. Domyślnie Europe/Warsaw.
          </p>

          <app-form-field-select
            testId="salon-currency-choice"
            label="Waluta salonu"
            id="currencyChoice"
            placeholder="Wybierz walutę"
            [value]="store.currencyChoice()"
            (valueChange)="store.onCurrencyChoiceChange($event)"
            [options]="store.currencyOptions"
          />
          @if (store.isCustomCurrency()) {
            <div class="flex flex-col gap-2 -mt-2">
              <label
                for="custom-currency"
                class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1"
              >
                3-literowy kod ISO waluty
              </label>
              <input
                id="custom-currency"
                data-testid="salon-currency-custom"
                placeholder="np. NOK"
                maxlength="3"
                [value]="store.customCurrency()"
                (input)="store.onCustomCurrencyInput($event)"
                class="w-full py-3 px-4 rounded-xl border border-surface-300 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 uppercase tracking-widest text-sm font-mono"
              />
              <p class="text-xs text-surface-500 dark:text-surface-500 px-1">
                Wpisz dokładnie 3 litery (ISO 4217), np. NOK, SEK, DKK.
              </p>
            </div>
          }
        </div>
      </app-form-layout>
    </app-settings-subpage>
  `,
})
export class SalonBrandPageComponent {
  protected readonly store = inject(SalonSettingsStore);
  private readonly router = inject(Router);

  protected onCancel(): void {
    void this.router.navigateByUrl('/admin/settings');
  }
}
