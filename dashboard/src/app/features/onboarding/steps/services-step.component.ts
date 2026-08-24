import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { lastValueFrom } from 'rxjs';
import { OnboardingClient } from '@core/api/api-client';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { formatAuthApiError } from '@core/errors/api-error-messages';
import { FormsModule } from '@angular/forms';
import { CheckboxModule } from 'primeng/checkbox';
import { WizardShellComponent } from '../ui/wizard-shell.component';
import { DraftService, OnboardingWizardStore } from '../onboarding-wizard.store';

/**
 * Krok „Twoje usługi" — prefill z szablonu branżowego do odznaczenia i poprawienia cen/czasu.
 * „Dalej" woła `applyIndustry` z FINALNĄ listą zaznaczonych (pusta = nic nie tworzymy).
 *
 * Etykieta przycisku: „Dalej" jak w pozostałych krokach (domyślna etykieta shella). Wcześniejsze
 * „Dodaj usługi i dalej" opisywało mechanikę zapisu, której użytkownik nie musi znać, i łamało się
 * na mobile na dwie linie. Wariant „Pomiń usługi" zostaje — przy pustym zaznaczeniu samo „Dalej"
 * nie mówiłoby, że salon powstanie BEZ usług.
 */
@Component({
  selector: 'app-onboarding-services-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WizardShellComponent, FormsModule, CheckboxModule],
  template: `
    <app-wizard-shell
      step="services"
      title="Twoje usługi"
      subtitle="Wybierz usługi i dowolnie je edytuj — resztę dodasz później w panelu."
      [nextPending]="saving()"
      [nextLabel]="selectedCount() > 0 ? 'Dalej' : 'Pomiń usługi'"
      [error]="globalError()"
      (next)="onNext()"
    >
      @if (services().length === 0) {
        <div class="rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-50/60 dark:bg-surface-50/40 px-4 py-6 text-center text-sm text-surface-500 dark:text-surface-400">
          Ta branża zaczyna od pustej listy — usługi dodasz w panelu po zakończeniu konfiguracji.
        </div>
      } @else {
        <div class="grid gap-2.5">
          @for (svc of services(); track $index) {
            <!--
              Na wąskim ekranie nazwa dostaje własny wiersz (w-full), a cena i czas schodzą pod nią.
              Wcześniej wszystko jechało w jednej linii: flex-wrap nigdy się nie uruchamiał, bo
              nazwa z "flex-1 min-w-0" kurczyła się do zera zamiast wypchnąć inputy niżej — a te mają
              sztywne szerokości (w-20/w-16). Efekt: "Manicure hybrydowy" → "Ma…", lista bezużyteczna.
              Od breakpointu sm wracamy do jednej linii (sm:w-auto sm:flex-1), gdzie miejsca wystarcza.
            -->
            <div
              class="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-2xl border border-surface-200 dark:border-surface-200 bg-white dark:bg-surface-50 px-4 py-3 transition-opacity"
              [class.opacity-55]="!svc.selected"
            >
              <!--
                p-checkbox, nie natywny input: natywne kontrolki renderują się stylem OS (tu:
                niebieskie) i odstają od amber — CLAUDE.md nazywa to nawracającą regresją.
                Etykieta przez atrybut for, a nie owinięcie inputa, zgodnie z wzorcem z tenants-page:
                owijający label podwójnie przełączałby p-checkbox.
              -->
              <div class="flex w-full min-w-0 items-center gap-3 sm:w-auto sm:flex-1">
                <p-checkbox
                  [binary]="true"
                  [inputId]="'setup-service-' + $index"
                  [ngModel]="svc.selected"
                  (ngModelChange)="toggle($index, $event)"
                />
                <!-- truncate dopiero od sm: w osobnym wierszu długa nazwa ma się zawinąć, a nie zniknąć. -->
                <label
                  [for]="'setup-service-' + $index"
                  class="text-sm font-bold sm:truncate cursor-pointer select-none"
                  [class.line-through]="!svc.selected"
                >
                  {{ svc.name }}
                </label>
              </div>
              <div class="flex items-center gap-2 ml-auto">
                <div class="flex items-center gap-1">
                  <input
                    type="number"
                    min="0"
                    step="1"
                    [value]="svc.price"
                    (input)="setPrice($index, $any($event.target).value)"
                    [disabled]="!svc.selected"
                    class="w-20 rounded-lg border border-surface-300 bg-surface-0 px-2 py-1.5 text-sm text-right dark:border-surface-200 dark:bg-surface-50 focus:outline-none focus:ring-2 focus:ring-primary disabled:opacity-50"
                  />
                  <span class="text-xs text-surface-500 dark:text-surface-400">zł</span>
                </div>
                <div class="flex items-center gap-1">
                  <input
                    type="number"
                    min="0"
                    step="5"
                    [value]="svc.durationMinutes"
                    (input)="setDuration($index, $any($event.target).value)"
                    [disabled]="!svc.selected"
                    class="w-16 rounded-lg border border-surface-300 bg-surface-0 px-2 py-1.5 text-sm text-right dark:border-surface-200 dark:bg-surface-50 focus:outline-none focus:ring-2 focus:ring-primary disabled:opacity-50"
                  />
                  <span class="text-xs text-surface-500 dark:text-surface-400">min</span>
                </div>
              </div>
            </div>
          }
        </div>
        <p class="mt-3 text-xs text-surface-500 dark:text-surface-400">
          Zaznaczonych usług: {{ selectedCount() }}
        </p>
      }
    </app-wizard-shell>
  `,
})
export class OnboardingServicesStepComponent {
  private readonly store = inject(OnboardingWizardStore);
  private readonly client = inject(OnboardingClient);
  private readonly onboardingState = inject(OnboardingStateService);
  private readonly router = inject(Router);

  protected readonly services = computed(() => this.store.draftServices());
  protected readonly selectedCount = computed(
    () => this.store.draftServices().filter((s) => s.selected).length,
  );
  protected readonly saving = signal(false);
  protected readonly globalError = signal('');

  protected toggle(index: number, checked: boolean): void {
    this.update(index, (s) => ({ ...s, selected: checked }));
  }

  protected setPrice(index: number, raw: string): void {
    const price = Math.max(0, Number(raw) || 0);
    this.update(index, (s) => ({ ...s, price }));
  }

  protected setDuration(index: number, raw: string): void {
    const durationMinutes = Math.max(0, Number(raw) || 0);
    this.update(index, (s) => ({ ...s, durationMinutes }));
  }

  private update(index: number, fn: (s: DraftService) => DraftService): void {
    this.store.draftServices.update((list) => list.map((s, i) => (i === index ? fn(s) : s)));
  }

  protected async onNext(): Promise<void> {
    if (this.saving()) {
      return;
    }
    const industryKey = this.store.selectedIndustryKey();
    if (!industryKey) {
      void this.router.navigate(['/setup/industry']);
      return;
    }

    this.saving.set(true);
    this.globalError.set('');
    this.store.persistIndustry();

    const services = this.store
      .draftServices()
      .filter((s) => s.selected)
      .map((s) => ({ name: s.name, price: s.price, durationMinutes: s.durationMinutes }));

    try {
      await lastValueFrom(
        this.client.applyIndustry({
          industryKey,
          services: services.length > 0 ? services : [],
        }),
      );
      this.onboardingState.markStale();
      void this.router.navigate(['/setup/rules']);
    } catch (err: unknown) {
      this.globalError.set(formatAuthApiError(err));
    } finally {
      this.saving.set(false);
    }
  }
}
