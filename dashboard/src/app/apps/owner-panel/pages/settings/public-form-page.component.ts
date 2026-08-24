import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FormField } from '@angular/forms/signals';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';
import {
  SettingChoiceComponent,
  type SettingChoiceOption,
} from '@shared/ui/settings/setting-choice.component';
import { SettingsSubpageComponent } from './settings-subpage.component';
import { SalonSettingsStore } from './salon-settings.store';

/**
 * Ustawienia → Dane przy rezerwacji: co klient podaje w publicznym formularzu — dane wymagane
 * (imię i nazwisko), pola opcjonalne (Instagram, inspiracje) oraz regulamin do akceptacji.
 */
@Component({
  selector: 'app-public-form-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SettingsSubpageComponent,
    FormLayoutComponent,
    FormFieldComponent,
    SettingChoiceComponent,
    FormField,
  ],
  template: `
    <app-settings-subpage
      title="Dane przy rezerwacji"
      subtitle="Co klient podaje i akceptuje w publicznym formularzu rezerwacji: dane wymagane, pola opcjonalne oraz regulamin do zaakceptowania."
    >
      <app-form-layout
        title="Dane przy rezerwacji"
        [isEdit]="true"
        [confirmOnCancel]="true"
        testId="public-form"
        submitButtonLabel="Zapisz"
        (submit)="store.onSave()"
        (cancel)="onCancel()"
      >
        <div class="grid grid-cols-1 gap-4">
          <app-setting-choice
            data-tour="public-form-required"
            label="Dane klienta przy rezerwacji"
            description="Decyduje, jakie dane klient podaje przy rezerwacji online. Możesz wymagać dodatkowo imienia i nazwiska."
            [options]="customerDataOptions"
            [value]="store.salonModel().requireCustomerName"
            (valueChange)="store.setRequireCustomerName($event)"
          />

          <app-setting-choice
            data-tour="public-form-instagram"
            label="Nick na Instagramie"
            description="Dodaje na formularzu rezerwacji opcjonalne pole na nick klienta na Instagramie. Pole zawsze pozostaje nieobowiązkowe — podany nick trafia na kartę klienta w CRM."
            [options]="instagramOptions"
            [value]="store.salonModel().collectInstagramHandle"
            (valueChange)="store.setCollectInstagramHandle($event)"
          />

          <app-setting-choice
            label="Zdjęcia inspiracji"
            description="Dodaje na formularzu rezerwacji opcjonalną sekcję, w której klientka może dorzucić zdjęcia (np. fryzura, paznokcie). Zdjęcia trafiają na storage dopiero po potwierdzeniu wizyty i są widoczne w szczegółach wizyty w panelu."
            [options]="inspirationOptions"
            [value]="store.salonModel().collectInspirationImages"
            (valueChange)="store.setCollectInspirationImages($event)"
          />

          <div
            class="rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50 dark:bg-surface-50/40 px-4 py-4 space-y-3"
          >
            <div>
              <span
                class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider block mb-1"
                >Regulamin rezerwacji</span
              >
              <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
                Treść pokazywana klientce w publicznym kalendarzu — musi ją zaakceptować, aby dokończyć
                rezerwację. Puste pole = pokazujemy domyślny regulamin zapisz.me.
              </p>
            </div>
            <app-form-field
              testId="salon-terms-of-service"
              data-tour="public-form-terms"
              label="Treść regulaminu"
              id="termsOfService"
              placeholder="np. zasady odwoływania wizyt, zadatki, spóźnienia…"
              [multiline]="true"
              [rows]="8"
              [formField]="store.salonForm.termsOfService"
            />
            <p class="text-xs text-surface-500 dark:text-surface-500 px-1">
              Maksymalnie {{ store.TERMS_OF_SERVICE_MAX }} znaków.
            </p>
          </div>
        </div>
      </app-form-layout>
    </app-settings-subpage>
  `,
})
export class PublicFormPageComponent {
  protected readonly store = inject(SalonSettingsStore);
  private readonly router = inject(Router);

  protected readonly customerDataOptions: readonly SettingChoiceOption[] = [
    {
      value: false,
      title: 'Tylko kontakt',
      description: 'Klient podaje wyłącznie numer telefonu lub adres e-mail.',
      testId: 'require-customer-name-off',
    },
    {
      value: true,
      title: 'Imię i nazwisko',
      description: 'Klient dodatkowo podaje imię i nazwisko, aby zarezerwować wizytę.',
      testId: 'require-customer-name-on',
    },
  ];

  protected readonly instagramOptions: readonly SettingChoiceOption[] = [
    { value: false, title: 'Ukryte', description: 'Formularz nie pyta o Instagram.', testId: 'collect-instagram-off' },
    {
      value: true,
      title: 'Pokaż pole',
      description: 'Klient może (opcjonalnie) podać swój nick na Instagramie.',
      testId: 'collect-instagram-on',
    },
  ];

  protected readonly inspirationOptions: readonly SettingChoiceOption[] = [
    {
      value: false,
      title: 'Ukryte',
      description: 'Formularz nie pozwala dodać zdjęć inspiracji.',
      testId: 'collect-inspirations-off',
    },
    {
      value: true,
      title: 'Pokaż sekcję',
      description: 'Klientka może (opcjonalnie) dodać zdjęcia inspiracji.',
      testId: 'collect-inspirations-on',
    },
  ];

  protected onCancel(): void {
    void this.router.navigateByUrl('/admin/settings');
  }
}
