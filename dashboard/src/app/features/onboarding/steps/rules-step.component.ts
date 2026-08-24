import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AppointmentConfirmationMode } from '@core/api/api-client';
import { WizardShellComponent } from '../ui/wizard-shell.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

/**
 * Krok „Jak przyjmujesz zapisy?" — automatyczne vs ręczne potwierdzanie.
 *
 * Krok NIE woła backendu: jedyna komenda, która zapisuje tryb potwierdzania
 * (`CompleteOnboardingCommand`), przy okazji domyka onboarding, a domknięcie tutaj — na piątym
 * z ośmiu kroków — kazałoby `setupGuard` odesłać właścicielkę do panelu w połowie kreatora.
 * Wybór czeka więc w buforze (`OnboardingWizardStore` + draft w localStorage) i leci do bazy
 * z ostatniego kroku „Jak układasz grafik?".
 */
@Component({
  selector: 'app-onboarding-rules-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WizardShellComponent],
  template: `
    <app-wizard-shell
      step="rules"
      title="Jak przyjmujesz zapisy?"
      subtitle="Zdecyduj, czy rezerwacje klientów mają potwierdzać się od razu, czy po Twojej akceptacji."
      (next)="onNext()"
    >
      <div class="grid gap-3">
        <button
          type="button"
          data-testid="setup-rules-auto"
          (click)="select(Automatic)"
          class="flex items-start gap-3 rounded-2xl border px-4 py-4 text-left transition-colors"
          [class.border-primary]="mode() === Automatic"
          [class.ring-2]="mode() === Automatic"
          [class.ring-primary]="mode() === Automatic"
          [class.border-surface-200]="mode() !== Automatic"
          [class.hover:border-primary]="mode() !== Automatic"
        >
          <span class="grid size-8 shrink-0 place-items-center rounded-full bg-primary/10 text-primary">
            <i class="pi pi-bolt text-sm"></i>
          </span>
          <div>
            <p class="text-sm font-bold">Automatyczne</p>
            <p class="text-xs text-surface-500 dark:text-surface-400 mt-1 leading-relaxed">
              Rezerwacja klienta od razu trafia do kalendarza. Najmniej pracy — polecane na start.
            </p>
          </div>
        </button>

        <button
          type="button"
          data-testid="setup-rules-manual"
          (click)="select(Manual)"
          class="flex items-start gap-3 rounded-2xl border px-4 py-4 text-left transition-colors"
          [class.border-primary]="mode() === Manual"
          [class.ring-2]="mode() === Manual"
          [class.ring-primary]="mode() === Manual"
          [class.border-surface-200]="mode() !== Manual"
          [class.hover:border-primary]="mode() !== Manual"
        >
          <span class="grid size-8 shrink-0 place-items-center rounded-full bg-primary/10 text-primary">
            <i class="pi pi-check-circle text-sm"></i>
          </span>
          <div>
            <p class="text-sm font-bold">Ręczne</p>
            <p class="text-xs text-surface-500 dark:text-surface-400 mt-1 leading-relaxed">
              Każdą rezerwację potwierdzasz sam. Pełna kontrola kosztem dodatkowego kroku.
            </p>
          </div>
        </button>
      </div>
    </app-wizard-shell>
  `,
})
export class OnboardingRulesStepComponent {
  private readonly store = inject(OnboardingWizardStore);
  private readonly router = inject(Router);

  protected readonly Automatic = AppointmentConfirmationMode.Automatic;
  protected readonly Manual = AppointmentConfirmationMode.Manual;
  protected readonly mode = signal<AppointmentConfirmationMode>(this.store.confirmationMode());

  protected select(mode: AppointmentConfirmationMode): void {
    this.mode.set(mode);
    this.store.confirmationMode.set(mode);
  }

  protected onNext(): void {
    this.store.confirmationMode.set(this.mode());
    this.store.persistRules();
    void this.router.navigate(['/setup/slot-mode']);
  }
}
