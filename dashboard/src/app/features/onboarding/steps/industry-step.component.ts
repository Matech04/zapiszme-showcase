import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, of } from 'rxjs';
import { IndustryTemplateDto, OnboardingClient } from '@core/api/api-client';
import { WizardShellComponent } from '../ui/wizard-shell.component';
import { OnboardingWizardStore, DraftService } from '../onboarding-wizard.store';

/** Krok „Czym się zajmujesz?" — kafle branż. Klik NIC nie zapisuje; wybór buforujemy do kroku usług. */
@Component({
  selector: 'app-onboarding-industry-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WizardShellComponent],
  template: `
    <app-wizard-shell
      step="industry"
      title="Czym się zajmujesz?"
      subtitle="Podpowiemy typowe usługi dla Twojej branży. Wszystko dopracujesz w następnym kroku."
      [nextDisabled]="!selectedKey()"
      [error]="loadError()"
      (next)="onNext()"
    >
      @if (loading()) {
        <p class="text-sm text-surface-500 dark:text-surface-400 py-8 text-center">Wczytywanie branż…</p>
      } @else {
        <div class="grid gap-3 sm:grid-cols-2">
          @for (industry of industries(); track industry.key) {
            <button
              type="button"
              [attr.data-testid]="'setup-industry-' + industry.key"
              (click)="select(industry)"
              class="flex flex-col items-start gap-1 rounded-2xl border px-4 py-3.5 text-left transition-colors"
              [class.border-primary]="selectedKey() === industry.key"
              [class.ring-2]="selectedKey() === industry.key"
              [class.ring-primary]="selectedKey() === industry.key"
              [class.bg-primary]="selectedKey() === industry.key"
              [class.text-primary-contrast]="selectedKey() === industry.key"
              [class.border-surface-200]="selectedKey() !== industry.key"
              [class.bg-white]="selectedKey() !== industry.key"
              [class.dark:bg-surface-50]="selectedKey() !== industry.key"
              [class.hover:border-primary]="selectedKey() !== industry.key"
            >
              <span class="text-sm font-bold">{{ industry.label }}</span>
              <!--
                Bez licznika „N przykładowych usług": wybierasz tu swoją branżę, a nie liczbę
                propozycji — 5 vs 6 niczego nie rozstrzyga, a same usługi i tak zobaczysz
                (i poprawisz) w następnym kroku.

                Podpis zostaje tylko przy pustym szablonie („Inne"), bo tam niesie realną różnicę
                w zachowaniu: ta opcja świadomie NIC nie podpowie.
              -->
              @if ((industry.services?.length ?? 0) === 0) {
                <span
                  class="text-xs"
                  [class.opacity-80]="selectedKey() === industry.key"
                  [class.text-surface-500]="selectedKey() !== industry.key"
                >
                  Zacznij od pustej listy
                </span>
              }
            </button>
          }
        </div>
      }
    </app-wizard-shell>
  `,
})
export class OnboardingIndustryStepComponent implements OnInit {
  private readonly store = inject(OnboardingWizardStore);
  private readonly client = inject(OnboardingClient);
  private readonly router = inject(Router);

  protected readonly loading = signal(false);
  protected readonly loadError = signal('');

  protected readonly industries = computed(() => this.store.industries());
  protected readonly selectedKey = signal<string | null>(this.store.selectedIndustryKey());

  ngOnInit(): void {
    if (this.store.industries().length > 0) {
      return;
    }
    this.loading.set(true);
    this.client
      .getIndustryTemplates()
      .pipe(catchError(() => of({ industries: [] })))
      .subscribe((res) => {
        this.store.industries.set(res.industries ?? []);
        this.loading.set(false);
        if ((res.industries?.length ?? 0) === 0) {
          this.loadError.set('Nie udało się wczytać branż. Spróbuj odświeżyć stronę.');
        }
      });
  }

  protected select(industry: IndustryTemplateDto): void {
    const key = industry.key ?? null;
    this.selectedKey.set(key);
    // Zmiana branży → nowy prefill usług (nadpisuje wcześniejsze edycje tylko przy realnej zmianie).
    if (this.store.selectedIndustryKey() !== key) {
      const services: DraftService[] = (industry.services ?? []).map((s) => ({
        name: s.name ?? '',
        price: s.price ?? 0,
        durationMinutes: s.durationMinutes ?? 0,
        selected: true,
      }));
      this.store.draftServices.set(services);
    }
    this.store.selectedIndustryKey.set(key);
  }

  protected onNext(): void {
    if (!this.selectedKey()) {
      return;
    }
    this.store.persistIndustry();
    void this.router.navigate(['/setup/services']);
  }
}
