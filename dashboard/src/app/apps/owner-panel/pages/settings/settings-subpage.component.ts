import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SalonSettingsStore } from './salon-settings.store';

/**
 * Wspólny shell pod-strony Ustawień salonu: hero z linkiem powrotu do huba, tytuł/podtytuł oraz
 * guard ładowania/błędu wokół treści (rzutowanej). Dzięki temu 5 pod-stron (Dane salonu, Zasady,
 * Dane przy rezerwacji, Wygląd, Prywatność) nie powiela nagłówka i obsługi stanu resource'a.
 */
@Component({
  selector: 'app-settings-subpage',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ProgressSpinnerModule],
  template: `
    <div class="min-h-screen px-4 pb-12 pt-4 sm:px-8 sm:pt-8">
      <div class="max-w-3xl mx-auto space-y-6">
        <header class="admin-glass-card rounded-4xl px-6 py-8 sm:px-8 sm:py-9">
          <a
            routerLink="/admin/settings"
            class="inline-flex items-center gap-1.5 text-sm font-bold text-primary hover:gap-2 transition-all mb-3"
          >
            <i class="pi pi-arrow-left text-xs" aria-hidden="true"></i>
            Ustawienia
          </a>
          <h1 class="text-3xl sm:text-4xl font-black tracking-tight text-surface-900 leading-tight mb-2">
            {{ title() }}
          </h1>
          @if (subtitle()) {
            <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">{{ subtitle() }}</p>
          }
        </header>

        @if (store.settings.isLoading()) {
          <div class="flex flex-col items-center justify-center py-20 gap-4">
            <p-progressSpinner styleClass="w-12 h-12" />
            <span class="text-surface-500 text-sm">Ładowanie ustawień…</span>
          </div>
        } @else if (store.settings.error()) {
          <div
            class="rounded-xl border border-red-200 dark:border-red-900/50 bg-red-50 dark:bg-red-950/20 p-6 text-red-800 dark:text-red-200 text-sm"
          >
            Nie udało się wczytać ustawień. Sprawdź połączenie z API albo uprawnienia (wymagana rola
            właściciela lub managera).
          </div>
        } @else {
          <ng-content />
        }
      </div>
    </div>
  `,
})
export class SettingsSubpageComponent {
  readonly store = inject(SalonSettingsStore);
  readonly title = input.required<string>();
  readonly subtitle = input<string>('');
}
