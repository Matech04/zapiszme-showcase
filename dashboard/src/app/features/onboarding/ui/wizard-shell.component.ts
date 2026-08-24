import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { Router } from '@angular/router';
import { ONBOARDING_STEPS, previousStepRoute, stepIndex } from '../onboarding-steps';

/**
 * Lekki „stepper" kreatora — pasek postępu + Wstecz/Dalej. Bez PrimeNG Stepper (nie zainstalowany).
 * Treść kroku wstrzykiwana przez `<ng-content>`. Krok woła własną logikę zapisu w handlerze `(next)`.
 */
@Component({
  selector: 'app-wizard-shell',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-4 py-8 sm:px-6 sm:py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div
          class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"
        ></div>
      </div>

      <div class="admin-glass-card relative z-10 w-full max-w-2xl rounded-4xl p-6 sm:p-8 space-y-6">
        <!-- Pasek postępu -->
        <div class="space-y-2">
          <div class="flex items-center justify-between text-xs font-bold text-surface-500 dark:text-surface-400">
            <span class="admin-section-label text-primary">Konfiguracja salonu</span>
            <span class="tabular-nums">Krok {{ humanIndex() }} z {{ total }}</span>
          </div>
          <div class="h-2 rounded-full bg-surface-200/80 dark:bg-surface-200/60 overflow-hidden">
            <div
              class="h-full bg-gradient-to-r from-amber-400 to-amber-600 transition-all duration-300"
              [style.width.%]="progressPct()"
            ></div>
          </div>
        </div>

        <!-- Nagłówek kroku -->
        <div class="space-y-1.5">
          <h1 class="text-2xl sm:text-3xl font-black tracking-tight">{{ title() }}</h1>
          @if (subtitle()) {
            <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
              {{ subtitle() }}
            </p>
          }
        </div>

        <!-- Treść kroku -->
        <div>
          <ng-content></ng-content>
        </div>

        @if (error()) {
          <div
            class="rounded-xl border border-red-400/35 bg-red-500/10 px-4 py-3 text-sm text-red-800 dark:text-red-200 whitespace-pre-line"
            role="alert"
            aria-live="polite"
          >
            {{ error() }}
          </div>
        }

        <!-- Nawigacja -->
        <div class="flex items-center gap-3 pt-2">
          @if (!hideBack() && previousRoute()) {
            <button
              type="button"
              data-testid="wizard-back"
              (click)="goBack()"
              class="px-5 py-3 rounded-xl border border-surface-300 dark:border-surface-200 text-sm font-semibold hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
            >
              Wstecz
            </button>
          }
          <div class="flex-1"></div>
          @if (!hideNext()) {
            <button
              type="button"
              data-testid="wizard-next"
              (click)="next.emit()"
              [attr.aria-busy]="nextPending() || null"
              [disabled]="nextDisabled() || nextPending()"
              class="px-8 py-3 rounded-xl bg-primary text-primary-contrast font-semibold shadow-lg hover:opacity-95 disabled:opacity-50 transition-opacity inline-flex items-center justify-center gap-2"
            >
              @if (nextPending()) {
                <svg
                  class="animate-spin h-5 w-5"
                  xmlns="http://www.w3.org/2000/svg"
                  fill="none"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                >
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"></path>
                </svg>
              }
              <span>{{ nextLabel() }}</span>
            </button>
          }
        </div>
      </div>
    </div>
  `,
})
export class WizardShellComponent {
  private readonly router = inject(Router);

  readonly step = input.required<string>();
  readonly title = input.required<string>();
  readonly subtitle = input<string | undefined>(undefined);
  readonly nextLabel = input<string>('Dalej');
  readonly nextDisabled = input<boolean>(false);
  readonly nextPending = input<boolean>(false);
  readonly hideNext = input<boolean>(false);
  readonly hideBack = input<boolean>(false);
  readonly error = input<string>('');

  readonly next = output<void>();

  protected readonly total = ONBOARDING_STEPS.length;
  protected readonly humanIndex = computed(() => stepIndex(this.step()) + 1);
  protected readonly progressPct = computed(() =>
    Math.round((this.humanIndex() / this.total) * 100),
  );
  protected readonly previousRoute = computed(() => previousStepRoute(this.step()));

  protected goBack(): void {
    const route = this.previousRoute();
    if (route) {
      void this.router.navigate([route]);
    }
  }
}
