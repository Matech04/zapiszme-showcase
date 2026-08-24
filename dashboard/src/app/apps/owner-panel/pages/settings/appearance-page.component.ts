import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { SettingsSubpageComponent } from './settings-subpage.component';
import { SalonSettingsStore } from './salon-settings.store';

/**
 * Ustawienia → Wygląd: kolory publicznego kalendarza rezerwacji (zapisywane na tenancie) oraz kolor akcentu
 * dashboardu (zapisywany lokalnie w przeglądarce, niezależnie od „Zapisz").
 */
@Component({
  selector: 'app-appearance-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SettingsSubpageComponent, FormLayoutComponent],
  template: `
    <app-settings-subpage
      title="Wygląd"
      subtitle="Kolory publicznego kalendarza rezerwacji (widoczne dla klientów) oraz akcent tego panelu."
    >
      <app-form-layout
        title="Kolory kalendarza rezerwacji"
        [isEdit]="true"
        [confirmOnCancel]="true"
        testId="appearance"
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
                >Kolor kalendarza rezerwacji</span
              >
              <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
                Akcent publicznego kalendarza (przyciski, zaznaczenia, wyróżnienia). Zapisywany z
                ustawieniami salonu i widoczny dla klientów. Z wybranego koloru wyprowadzamy spójną,
                czytelną paletę.
              </p>
            </div>
            <div class="flex flex-wrap items-center gap-3">
              <input
                type="color"
                class="h-11 w-16 cursor-pointer rounded-lg border border-surface-300 bg-transparent p-0.5 dark:border-surface-600"
                [value]="store.salonModel().bookingCalendarColorHex || '#7C3AED'"
                (input)="store.onBookingColorInput($event)"
                aria-label="Kolor kalendarza rezerwacji"
              />
              <span class="font-mono text-sm text-surface-600 dark:text-surface-400">{{
                store.salonModel().bookingCalendarColorHex || 'domyślny motyw'
              }}</span>
              @if (store.salonModel().bookingCalendarColorHex) {
                <button
                  type="button"
                  class="rounded-lg border border-transparent px-3 py-2 text-sm font-semibold text-primary underline-offset-4 hover:underline"
                  (click)="store.setBookingCalendarColor('')"
                >
                  Wyczyść
                </button>
              }
            </div>
            <div class="flex flex-wrap items-center gap-2 pt-1">
              <span class="text-xs font-bold uppercase tracking-wider text-surface-500">Gotowe kolory</span>
              @for (p of store.bookingColorPresets; track p.hex) {
                <button
                  type="button"
                  class="h-9 w-9 shrink-0 rounded-full border-2 shadow-sm transition hover:scale-105 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  [class.border-primary]="(store.salonModel().bookingCalendarColorHex || '').toUpperCase() === p.hex"
                  [class.border-surface-200]="(store.salonModel().bookingCalendarColorHex || '').toUpperCase() !== p.hex"
                  [style.background-color]="p.hex"
                  [attr.aria-label]="'Kolor: ' + p.label"
                  [title]="p.label"
                  (click)="store.setBookingCalendarColor(p.hex)"
                ></button>
              }
            </div>
          </div>

          <div
            class="rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50 dark:bg-surface-50/40 px-4 py-4 space-y-3"
          >
            <div>
              <span
                class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider block mb-1"
                >Kolory kalendarza (zaawansowane)</span
              >
              <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
                Tło strony, tło karty i kolor cen publicznego kalendarza. Puste = motyw domyślny.
              </p>
            </div>
            @for (f of store.bookingThemeFields; track f.field) {
              <div class="flex flex-wrap items-center gap-3">
                <span class="w-24 shrink-0 text-sm font-semibold text-surface-700 dark:text-surface-300">{{
                  f.label
                }}</span>
                <input
                  type="color"
                  class="h-10 w-14 cursor-pointer rounded-lg border border-surface-300 bg-transparent p-0.5 dark:border-surface-600"
                  [value]="store.salonModel()[f.field] || f.fallback"
                  (input)="store.onThemeColorInput(f.field, $event)"
                  [attr.aria-label]="f.label"
                />
                <span class="font-mono text-xs text-surface-500">{{
                  store.salonModel()[f.field] || 'domyślny'
                }}</span>
                @if (store.salonModel()[f.field]) {
                  <button
                    type="button"
                    class="text-xs font-semibold text-primary underline-offset-4 hover:underline"
                    (click)="store.setThemeColor(f.field, '')"
                  >
                    Wyczyść
                  </button>
                }
                <span class="basis-full text-xs text-surface-500 dark:text-surface-400">{{ f.hint }}</span>
              </div>
            }
          </div>
        </div>
      </app-form-layout>

      <!-- Kolor akcentu dashboardu — zapisywany lokalnie (localStorage), niezależnie od „Zapisz" -->
      <div
        class="admin-glass-card rounded-2xl border border-surface-200 dark:border-surface-200 px-5 py-5 space-y-3"
      >
        <div>
          <span
            class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider block mb-1"
            >Kolor dashboardu</span
          >
          <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
            Akcent interfejsu (pasek boczny, przyciski, linki). Ustawienie jest zapisane
            <strong>w tej przeglądarce</strong> — nie synchronizuje się między urządzeniami.
          </p>
        </div>
        <div class="flex flex-wrap items-center gap-3">
          <input
            type="color"
            class="h-11 w-16 cursor-pointer rounded-lg border border-surface-300 bg-transparent p-0.5 dark:border-surface-600"
            [value]="store.pickHex()"
            (input)="store.onAccentColorInput($event)"
            aria-label="Kolor akcentu dashboardu"
          />
          <button
            type="button"
            class="rounded-lg border border-surface-300 bg-surface-0 px-4 py-2 text-sm font-bold uppercase tracking-wide text-surface-800 shadow-sm hover:border-primary/40 dark:border-surface-600 dark:bg-surface-50"
            (click)="store.applyAccentColor()"
          >
            Zastosuj
          </button>
          <button
            type="button"
            class="rounded-lg border border-transparent px-4 py-2 text-sm font-semibold text-primary underline-offset-4 hover:underline"
            (click)="store.resetAccentColor()"
          >
            Przywróć domyślny
          </button>
        </div>
        <div class="flex flex-wrap items-center gap-2 pt-1">
          <span class="text-xs font-bold uppercase tracking-wider text-surface-500">Gotowe zestawy</span>
          @for (p of store.accentPresets; track p.hex) {
            <button
              type="button"
              class="h-9 w-9 shrink-0 rounded-full border-2 border-surface-200 shadow-sm ring-offset-2 transition hover:scale-105 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary dark:border-surface-200"
              [style.background-color]="p.hex"
              [attr.aria-label]="'Kolor: ' + p.label"
              [title]="p.label"
              (click)="store.applyPresetAccent(p.hex)"
            ></button>
          }
        </div>
      </div>
    </app-settings-subpage>
  `,
})
export class AppearancePageComponent {
  protected readonly store = inject(SalonSettingsStore);
  private readonly router = inject(Router);

  protected onCancel(): void {
    void this.router.navigateByUrl('/admin/settings');
  }
}
