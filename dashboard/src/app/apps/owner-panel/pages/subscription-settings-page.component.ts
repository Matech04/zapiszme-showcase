import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ButtonModule } from 'primeng/button';

/**
 * Zakładka „Subskrypcja" w ustawieniach salonu.
 *
 * Docelowo: integracja ze Stripe (Billing / Customer Portal) — zarządzanie planem,
 * metodą płatności i fakturami właściciela salonu. Na razie scaffold UI:
 * pokazuje bieżący plan + miejsce na podłączenie płatności (CTA wyłączone).
 */
@Component({
  selector: 'app-subscription-settings-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ButtonModule],
  template: `
    <div class="min-h-screen px-4 pb-14 pt-4 sm:px-8 sm:pt-8">
      <div class="max-w-3xl mx-auto space-y-6">
        <header class="admin-glass-card rounded-4xl px-6 py-8 sm:px-8 sm:py-9">
          <span class="admin-section-label text-primary mb-3 block">
            Konfiguracja salonu
          </span>
          <h1
            class="text-3xl sm:text-4xl font-black tracking-tight text-surface-900 leading-tight mb-2"
          >
            Subskrypcja
          </h1>
          <p class="text-surface-700 dark:text-surface-300 text-sm sm:text-base leading-relaxed">
            Twój plan, metoda płatności i faktury. Płatności online obsłuży Stripe — pojawią się tutaj
            wkrótce.
          </p>
        </header>

        <!-- Bieżący plan -->
        <section class="admin-glass-card rounded-4xl p-5 sm:p-6">
          <div class="flex flex-wrap items-start justify-between gap-4">
            <div>
              <h2 class="text-lg font-bold text-surface-900 mb-1">Plan Start</h2>
              <p class="text-sm text-surface-600 dark:text-surface-300">
                Okres próbny — 30 dni bez karty
              </p>
            </div>
            <span
              class="inline-flex items-center rounded-full bg-emerald-500/15 px-3 py-1 text-xs font-bold uppercase tracking-wide text-emerald-700 dark:text-emerald-300"
            >
              Trial aktywny
            </span>
          </div>

          <dl class="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div class="rounded-2xl bg-surface-100/60 dark:bg-surface-100/40 px-4 py-3">
              <dt class="text-xs font-bold uppercase tracking-wider text-surface-500">Cena</dt>
              <dd class="mt-1 text-surface-900 font-semibold">
                79&nbsp;zł / mies. + 35&nbsp;zł za pracownika
              </dd>
            </div>
            <div class="rounded-2xl bg-surface-100/60 dark:bg-surface-100/40 px-4 py-3">
              <dt class="text-xs font-bold uppercase tracking-wider text-surface-500">SMS w pakiecie</dt>
              <dd class="mt-1 text-surface-900 font-semibold">
                200 + 150 / pracownik
              </dd>
            </div>
          </dl>
        </section>

        <!-- Płatność (Stripe — wkrótce) -->
        <section class="admin-glass-card rounded-4xl p-5 sm:p-6">
          <h2 class="text-lg font-bold text-surface-900 mb-1">Metoda płatności</h2>
          <p class="text-sm text-surface-600 dark:text-surface-300 mb-5">
            Po zakończeniu okresu próbnego podłączysz tu kartę przez bezpieczny portal Stripe. Faktury i
            historia płatności będą dostępne w jednym miejscu.
          </p>

          <div
            class="flex flex-col items-center justify-center gap-3 rounded-3xl border border-dashed border-surface-300 dark:border-surface-200 px-6 py-10 text-center"
          >
            <i class="pi pi-credit-card text-3xl text-amber-500" aria-hidden="true"></i>
            <p class="text-surface-700 dark:text-surface-300 max-w-md text-sm">
              Integracja ze Stripe jest w przygotowaniu. Na razie nie musisz nic robić — masz pełny dostęp
              w ramach okresu próbnego.
            </p>
            <p-button
              label="Połącz ze Stripe"
              icon="pi pi-lock"
              severity="secondary"
              [disabled]="true"
            />
            <span class="text-xs font-bold uppercase tracking-wide text-surface-400">Wkrótce</span>
          </div>
        </section>
      </div>
    </div>
  `,
})
export class SubscriptionSettingsPageComponent {}
