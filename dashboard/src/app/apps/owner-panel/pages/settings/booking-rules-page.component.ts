import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { FormField } from '@angular/forms/signals';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';
import { CustomerVerificationChannel, GapFillingMode } from '@core/api/api-client';
import {
  SettingChoiceComponent,
  type SettingChoiceOption,
} from '@shared/ui/settings/setting-choice.component';
import { SettingsSubpageComponent } from './settings-subpage.component';
import { SalonSettingsStore } from './salon-settings.store';

/**
 * Ustawienia → Zasady rezerwacji: wstrzymanie rezerwacji (natychmiastowe), dostęp, potwierdzanie,
 * weryfikacja klienta, zakres danych, wypełnianie luk oraz widoczność kalendarza dla pracowników.
 */
@Component({
  selector: 'app-booking-rules-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SettingsSubpageComponent,
    FormLayoutComponent,
    FormFieldComponent,
    SettingChoiceComponent,
    FormsModule,
    FormField,
  ],
  template: `
    <app-settings-subpage
      title="Zasady rezerwacji"
      subtitle="Reguły rezerwacji online: kto i jak może się umówić, jak potwierdzasz wizyty oraz co widzi zespół w kalendarzu."
    >
      <!-- Wstrzymanie rezerwacji — natychmiastowe, niezależne od „Zapisz" -->
      <div
        data-testid="booking-pause-card"
        class="rounded-2xl border px-5 py-5 space-y-4 transition-colors"
        [class.border-red-300]="store.bookingPaused()"
        [class.bg-red-50]="store.bookingPaused()"
        [class.dark:border-red-900/50]="store.bookingPaused()"
        [class.dark:bg-red-950/20]="store.bookingPaused()"
        [class.border-surface-200]="!store.bookingPaused()"
        [class.dark:border-surface-200]="!store.bookingPaused()"
        [class.bg-surface-50]="!store.bookingPaused()"
        [class.dark:bg-surface-50/40]="!store.bookingPaused()"
      >
        <div>
          <span
            class="text-sm font-bold uppercase tracking-wider block mb-1 text-surface-700 dark:text-surface-300"
          >
            ⏸️ Wstrzymaj rezerwacje
          </span>
          <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
            Tymczasowo wstrzymuje rezerwacje online (publiczny kalendarz) — np. na czas zmian w grafiku.
            W panelu pojawia się baner przypominający zespołowi, że klienci nie mogą teraz rezerwować.
            Zmiana zapisuje się od razu.
          </p>
        </div>
        <div class="grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            data-testid="booking-pause-off"
            [disabled]="store.bookingPauseSaving()"
            class="rounded-xl border-2 px-4 py-3 text-left transition-colors disabled:opacity-60"
            [class.border-amber-500]="!store.bookingPaused()"
            [class.bg-amber-50]="!store.bookingPaused()"
            [class.dark:bg-amber-950/30]="!store.bookingPaused()"
            [class.border-surface-200]="store.bookingPaused()"
            [class.dark:border-surface-200]="store.bookingPaused()"
            (click)="store.toggleBookingPause(false)"
          >
            <span class="block text-sm font-black text-surface-900">Aktywne</span>
            <span class="block text-xs text-surface-500 mt-1">Salon przyjmuje rezerwacje online normalnie.</span>
          </button>
          <button
            type="button"
            data-testid="booking-pause-on"
            [disabled]="store.bookingPauseSaving()"
            class="rounded-xl border-2 px-4 py-3 text-left transition-colors disabled:opacity-60"
            [class.border-red-500]="store.bookingPaused()"
            [class.bg-red-100]="store.bookingPaused()"
            [class.dark:bg-red-950/40]="store.bookingPaused()"
            [class.border-surface-200]="!store.bookingPaused()"
            [class.dark:border-surface-200]="!store.bookingPaused()"
            (click)="store.toggleBookingPause(true)"
          >
            <span class="block text-sm font-black text-surface-900">Wstrzymane</span>
            <span class="block text-xs text-surface-500 mt-1">Rezerwacje online zablokowane — klient widzi komunikat.</span>
          </button>
        </div>
        @if (store.bookingPaused()) {
          <div class="flex flex-col gap-2 pt-1">
            <label
              for="booking-pause-message"
              class="text-xs font-bold text-surface-600 dark:text-surface-400 uppercase tracking-wider"
            >
              Komunikat dla klientów (opcjonalny)
            </label>
            <textarea
              id="booking-pause-message"
              data-testid="booking-pause-message"
              rows="2"
              [attr.maxlength]="store.bookingPauseMessageMaxLength"
              [value]="store.bookingPauseMessage"
              (input)="store.onBookingPauseMessageInput($event)"
              placeholder="np. Zmieniamy grafik — zadzwoń, aby umówić wizytę."
              class="w-full py-2.5 px-3 rounded-xl border border-surface-300 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 text-sm resize-none"
            ></textarea>
            <div class="flex items-center justify-between">
              <span class="text-xs text-surface-500">Pusty = domyślny tekst na stronie rezerwacji.</span>
              <button
                type="button"
                data-testid="booking-pause-save-message"
                [disabled]="store.bookingPauseSaving()"
                class="rounded-lg border border-surface-300 bg-surface-0 px-4 py-2 text-sm font-bold uppercase tracking-wide text-surface-800 shadow-sm hover:border-primary/40 disabled:opacity-60 dark:border-surface-600 dark:bg-surface-50"
                (click)="store.saveBookingPauseMessage()"
              >
                Zapisz komunikat
              </button>
            </div>
          </div>
        }
      </div>

      <app-form-layout
        title="Zasady rezerwacji"
        [isEdit]="true"
        [confirmOnCancel]="true"
        testId="booking-rules"
        submitButtonLabel="Zapisz"
        (submit)="store.onSave()"
        (cancel)="onCancel()"
      >
        <div class="grid grid-cols-1 gap-4">
          <div
            class="rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50 dark:bg-surface-50/40 px-4 py-4 space-y-3"
          >
            <div>
              <span
                class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider block mb-1"
                >Interwał slotów rezerwacji</span
              >
              <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
                Co ile minut klient może rozpocząć wizytę (np. co 15 min: 9:00, 9:15, 9:30…). Dotyczy
                grafików z <strong>elastycznymi godzinami</strong>; przy <strong>ustalonych godzinach</strong>
                jest pomijane.
              </p>
            </div>
            <app-form-field
              testId="salon-slot-step"
              data-tour="booking-interval"
              label="Interwał (minuty)"
              id="appointmentSlotStepMinutes"
              placeholder="np. 15"
              type="number"
              [formField]="store.salonForm.appointmentSlotStepMinutes"
            />
            <p class="text-xs text-surface-500 dark:text-surface-500 px-1">Zakres: 1–240 minut.</p>
          </div>

          <div
            class="rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50 dark:bg-surface-50/40 px-4 py-4 space-y-3"
          >
            <div>
              <span
                class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider block mb-1"
                >Jak daleko naprzód można rezerwować</span
              >
              <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
                Klient zobaczy terminy tylko w tym oknie, licząc od dziś. Krótsze okno chroni przed
                rezerwacjami na odległą przyszłość, których grafik jeszcze nie obejmuje. Pojedynczy
                miesiąc możesz otworzyć wcześniej lub później w kalendarzu
                (<strong>Otwarcie zapisów</strong>).
              </p>
            </div>
            <app-form-field
              testId="salon-booking-horizon"
              data-tour="booking-horizon"
              label="Horyzont rezerwacji (dni)"
              id="bookingHorizonDays"
              placeholder="np. 120"
              type="number"
              [formField]="store.salonForm.bookingHorizonDays"
            />
            <p class="text-xs text-surface-500 dark:text-surface-500 px-1">
              Zakres: 1–1826 dni (do 5 lat). Domyślnie 120 dni, czyli około 4 miesiące.
            </p>
          </div>

          <app-setting-choice
            data-tour="booking-access"
            label="Dostęp do rezerwacji online"
            description="Decyduje, kto może zarezerwować wizytę przez publiczną stronę salonu."
            [options]="accessOptions"
            [value]="store.salonModel().bookingAccessPolicy"
            (valueChange)="store.setBookingAccessPolicy($event)"
          />

          <app-setting-choice
            data-tour="booking-confirmation"
            label="Potwierdzanie wizyt"
            description="Decyduje, czy wizyta jest potwierdzona od razu po weryfikacji OTP, czy wymaga ręcznego zatwierdzenia."
            [options]="confirmationOptions"
            [value]="store.salonModel().appointmentConfirmationMode"
            (valueChange)="store.setAppointmentConfirmationMode($event)"
          />

          <app-setting-choice
            data-tour="booking-verification"
            label="Weryfikacja klienta przy rezerwacji"
            description="Jak klient potwierdza rezerwację online — kodem SMS na telefon (zalecane) albo kodem na e-mail."
            [options]="verificationOptions"
            [value]="store.salonModel().customerVerificationChannel"
            (valueChange)="store.setVerificationChannel($event)"
          />

          <app-setting-choice
            data-tour="booking-gap-filling"
            label="Wypełnianie luk między wizytami"
            description="Pomaga zminimalizować nieproduktywne przerwy — preferowane terminy sąsiadują z już zajętymi wizytami."
            [columns]="3"
            [options]="gapOptions"
            [value]="store.gapFillingMode()"
            (valueChange)="store.setGapFillingMode($event)"
          >
            @if (store.gapFillingMode() !== GapFillingMode.Disabled) {
              <div class="grid gap-4 sm:grid-cols-2 pt-1">
                <div class="flex flex-col gap-1.5">
                  <label
                    for="gap-buffer"
                    class="text-xs font-bold text-surface-600 dark:text-surface-400 uppercase tracking-wider"
                    >Bufor (min)</label
                  >
                  <input
                    id="gap-buffer"
                    type="number"
                    min="0"
                    max="60"
                    step="5"
                    [(ngModel)]="store.gapFillingBuffer"
                    class="w-full py-2.5 px-3 rounded-xl border border-surface-300 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 text-sm"
                  />
                  <p class="text-xs text-surface-500">Przerwa przed/po wizycie wliczana do sąsiedztwa.</p>
                </div>
                <div class="flex flex-col gap-1.5">
                  <label
                    for="gap-lookahead"
                    class="text-xs font-bold text-surface-600 dark:text-surface-400 uppercase tracking-wider"
                    >Szerokość sąsiedztwa</label
                  >
                  <input
                    id="gap-lookahead"
                    type="number"
                    min="1"
                    max="10"
                    step="1"
                    [(ngModel)]="store.gapFillingLookahead"
                    class="w-full py-2.5 px-3 rounded-xl border border-surface-300 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 text-sm"
                  />
                  <p class="text-xs text-surface-500">Ile slotów poza bezpośrednio sąsiadującym jest preferowanych.</p>
                </div>
              </div>
            }
          </app-setting-choice>

          <app-setting-choice
            data-tour="booking-team-calendar"
            label="Kalendarz pracowników"
            description="Decyduje co widzi i może zrobić zwykły pracownik w panelu wizyt. Owner i Manager mają pełen dostęp niezależnie od tego ustawienia."
            [columns]="3"
            [options]="staffCalendarOptions"
            [value]="store.salonModel().staffCalendarVisibilityPolicy"
            (valueChange)="store.setStaffCalendarVisibilityPolicy($event)"
          />
        </div>
      </app-form-layout>
    </app-settings-subpage>
  `,
})
export class BookingRulesPageComponent {
  protected readonly store = inject(SalonSettingsStore);
  private readonly router = inject(Router);

  protected readonly GapFillingMode = GapFillingMode;

  protected readonly accessOptions: readonly SettingChoiceOption[] = [
    { value: 'open', title: 'Otwarte', description: 'Każdy klient może umówić wizytę online.' },
    {
      value: 'invite_only',
      title: 'Tylko zaproszeni',
      description: 'Rezerwują wyłącznie klienci z whitelisty (CRM).',
    },
  ];

  protected readonly confirmationOptions: readonly SettingChoiceOption[] = [
    {
      value: 'automatic',
      title: 'Automatyczne',
      description: 'Wizyta potwierdzona natychmiast po wpisaniu kodu OTP.',
    },
    { value: 'manual', title: 'Ręczne', description: 'Personel musi ręcznie zatwierdzić każdą wizytę.' },
  ];

  protected readonly verificationOptions: readonly SettingChoiceOption[] = [
    {
      value: CustomerVerificationChannel.Phone,
      title: 'Kod SMS',
      description: 'Klient podaje numer telefonu i przepisuje kod z SMS-a.',
      testId: 'verification-channel-phone',
    },
    {
      value: CustomerVerificationChannel.Email,
      title: 'Kod e-mail',
      description: 'Klient podaje adres e-mail i przepisuje kod z wiadomości.',
      testId: 'verification-channel-email',
    },
  ];

  protected readonly gapOptions: readonly SettingChoiceOption[] = [
    {
      value: GapFillingMode.Disabled,
      title: 'Wyłączone',
      description: 'Klient widzi wszystkie wolne godziny, bez podpowiedzi.',
    },
    {
      value: GapFillingMode.PreferAdjacent,
      title: 'Wyróżnij sąsiednie',
      description: 'Klient widzi wszystkie godziny, ale te tuż przy zajętych wizytach są wyróżnione.',
    },
    {
      value: GapFillingMode.AdjacentOnly,
      title: 'Tylko sąsiednie',
      description: 'Klient widzi tylko godziny tuż przy zajętych wizytach; te tworzące luki są ukryte.',
    },
  ];

  protected readonly staffCalendarOptions: readonly SettingChoiceOption[] = [
    { value: 'own', title: 'Tylko swój', description: 'Pracownik widzi i edytuje wyłącznie własne wizyty.' },
    {
      value: 'team_read',
      title: 'Zespół (read-only)',
      description: 'Widzi cały zespół, ale edytuje tylko swoje wizyty.',
    },
    {
      value: 'team_full',
      title: 'Zespół (pełny)',
      description: 'Pracownik może edytować/anulować wizyty kolegów.',
    },
  ];

  protected onCancel(): void {
    void this.router.navigateByUrl('/admin/settings');
  }
}
