import { NgClass } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SlotGenerationMode } from '@core/api/api-client';
import { WizardShellComponent } from '../ui/wizard-shell.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

/**
 * Krok „Jak wyznaczasz terminy?" — Elastyczny (Grid) vs Stałe godziny (FixedStartTimes). Bufor wyboru.
 *
 * Tytuł ŚWIADOMIE nie mówi „grafik": to własność pojedynczego DNIA pracy, nie tygodnia, a o sam
 * grafik powtarzalny pytamy dopiero na następnym kroku. Pytanie „jaki grafik wolisz?" tuż przed
 * „czy w ogóle ustawić grafik?" byłoby inwersją — jak pytanie o kolor auta przed pytaniem, czy
 * w ogóle chcesz auto.
 *
 * Wybór dotyczy OBU ścieżek z następnego kroku: dni z grafiku powtarzalnego i dni wklikanych
 * ręcznie. `Employee.SlotGenerationMode` jest podpowiedzią startową dla każdego nowego dnia
 * specjalnego (patrz `employeeMode` w `employee-special-days.component.ts`), a sam tryb jest
 * rozstrzygany PER DZIEŃ — wyjątek niesie własny `IsFixed`.
 */
@Component({
  selector: 'app-onboarding-slot-mode-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass, WizardShellComponent],
  template: `
    <app-wizard-shell
      step="slot-mode"
      title="Jak wyznaczasz terminy?"
      subtitle="Klientka nie wpisuje godziny sama — wybiera z tych, które jej pokażemy. Tutaj decydujesz, skąd się one biorą."
      (next)="onNext()"
    >
      <div class="grid gap-4">
        <div class="grid gap-3 sm:grid-cols-2">
          <!-- Elastyczny / Grid -->
          <button
            type="button"
            data-testid="setup-slot-mode-grid"
            (click)="select(Grid)"
            [attr.aria-pressed]="mode() === Grid"
            class="flex flex-col gap-3 rounded-2xl border px-4 py-4 text-left transition-colors"
            [class.border-primary]="mode() === Grid"
            [class.ring-2]="mode() === Grid"
            [class.ring-primary]="mode() === Grid"
            [class.border-surface-200]="mode() !== Grid"
            [class.hover:border-primary]="mode() !== Grid"
          >
            <!--
              Oba podglądy rysują TĘ SAMĄ oś (09:00–12:45 co 15 min) i różnią się wyłącznie tym, ile
              komórek jest zapalonych. Wcześniej lewa karta miała siatkę godzin, a prawa trzy pełne
              belki — dwa różne języki wizualne, między którymi oko nie miało czego porównać.
              Wygaszona komórka = godzina, której klientka w tym trybie w ogóle nie zobaczy.
            -->
            <div class="grid grid-cols-4 gap-1" aria-hidden="true">
              @for (cell of timeline; track cell) {
                <span
                  class="h-4 rounded-sm flex items-center justify-center text-[8px] font-bold bg-primary/25 text-primary"
                >
                  {{ cell }}
                </span>
              }
            </div>
            <div>
              <p class="text-sm font-bold">Elastyczny</p>
              <p class="text-xs text-surface-500 dark:text-surface-400 mt-1 leading-relaxed">
                Klientka wybiera dowolną godzinę z Twoich godzin pracy, co 15 minut.
              </p>
              <ul class="mt-2 space-y-1 text-xs leading-relaxed">
                <li class="flex gap-1.5">
                  <span class="text-emerald-700 dark:text-emerald-300 font-bold" aria-hidden="true">✓</span>
                  <span class="text-surface-600 dark:text-surface-300">
                    Rzadko odpada przez niepasującą godzinę
                  </span>
                </li>
                <li class="flex gap-1.5">
                  <span class="text-surface-400 dark:text-surface-400 font-bold" aria-hidden="true">−</span>
                  <span class="text-surface-500 dark:text-surface-400">Między wizytami zostają luki</span>
                </li>
              </ul>
            </div>
          </button>

          <!-- Stałe godziny / FixedStartTimes -->
          <button
            type="button"
            data-testid="setup-slot-mode-fixed"
            (click)="select(Fixed)"
            [attr.aria-pressed]="mode() === Fixed"
            class="flex flex-col gap-3 rounded-2xl border px-4 py-4 text-left transition-colors"
            [class.border-primary]="mode() === Fixed"
            [class.ring-2]="mode() === Fixed"
            [class.ring-primary]="mode() === Fixed"
            [class.border-surface-200]="mode() !== Fixed"
            [class.hover:border-primary]="mode() !== Fixed"
          >
            <div class="grid grid-cols-4 gap-1" aria-hidden="true">
              @for (cell of timeline; track cell) {
                <span
                  class="h-4 rounded-sm flex items-center justify-center text-[8px] font-bold"
                  [ngClass]="
                    isFixedPreview(cell)
                      ? 'bg-primary/25 text-primary'
                      : 'bg-surface-200/80 dark:bg-surface-200/60 text-surface-400 opacity-70'
                  "
                >
                  {{ cell }}
                </span>
              }
            </div>
            <div>
              <p class="text-sm font-bold">Stałe godziny</p>
              <p class="text-xs text-surface-500 dark:text-surface-400 mt-1 leading-relaxed">
                Klientka wybiera wyłącznie z godzin, które sama wyznaczysz.
              </p>
              <ul class="mt-2 space-y-1 text-xs leading-relaxed">
                <li class="flex gap-1.5">
                  <span class="text-emerald-700 dark:text-emerald-300 font-bold" aria-hidden="true">✓</span>
                  <span class="text-surface-600 dark:text-surface-300">Dzień poukładany i przewidywalny</span>
                </li>
                <li class="flex gap-1.5">
                  <span class="text-surface-400 dark:text-surface-400 font-bold" aria-hidden="true">−</span>
                  <span class="text-surface-500 dark:text-surface-400">
                    Nie pasuje żadna godzina — nie ma zapisu
                  </span>
                </li>
              </ul>
            </div>
          </button>
        </div>

        <!--
          Konkret pod kartami, a nie w nich: przykład wystarczy dla wybranego trybu, a wpisany w obie
          karty podwoiłby tekst i rozepchnął je na 390px. Zmienia się z wyborem, więc nie da się go
          przegapić przy przełączaniu.
        -->
        <div
          data-testid="setup-slot-mode-example"
          class="rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-50/60 dark:bg-surface-50/40 px-4 py-4 text-sm text-surface-600 dark:text-surface-300 leading-relaxed"
        >
          @if (mode() === Grid) {
            Pracujesz 9:00–17:00, a zabieg trwa godzinę. Klientka może zacząć o 9:00, 9:15, 9:30 —
            byle zabieg zmieścił się do końca pracy i nie nachodził na inną wizytę. Godziny tuż przy
            już zajętych pokazujemy jej na górze jako <strong>„Polecane"</strong>, żeby dzień nie
            robił się dziurawy.
          } @else {
            Wyznaczasz np. 9:00, 10:30 i 12:00 — tylko te trzy godziny klientka w ogóle zobaczy.
            Skończysz wcześniej? Następna i tak przyjdzie o wyznaczonej porze. Godziny podasz na
            następnym kroku, wspólnie dla wszystkich dni pracy.
          }
        </div>

        <p class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed px-1">
          To nie jest wybór na zawsze — tryb zmienisz później w grafiku, a dla pojedynczego dnia
          w kalendarzu.
        </p>
      </div>
    </app-wizard-shell>
  `,
})
export class OnboardingSlotModeStepComponent {
  private readonly store = inject(OnboardingWizardStore);
  private readonly router = inject(Router);

  protected readonly Grid = SlotGenerationMode.Grid;
  protected readonly Fixed = SlotGenerationMode.FixedStartTimes;

  /**
   * Wspólna oś obu podglądów: 09:00–12:45 co 15 minut (domyślny `Tenant.AppointmentSlotStepMinutes`).
   * Generowana, a nie wpisana ręcznie, żeby siatka nie rozjechała się z opisem („co 15 minut")
   * przy edycji jednej z komórek. Jedna tablica dla obu kart — inaczej podglądy przestałyby
   * pokazywać tę samą dobę i porównanie straciłoby sens.
   */
  protected readonly timeline = Array.from({ length: 16 }, (_, i) => {
    const minutes = 9 * 60 + i * 15;
    const hh = String(Math.floor(minutes / 60)).padStart(2, '0');
    const mm = String(minutes % 60).padStart(2, '0');
    return `${hh}:${mm}`;
  });

  /**
   * Godziny startu zapalone w podglądzie trybu stałego — te same, które padają w przykładzie pod
   * kartami. Muszą leżeć NA `timeline`, inaczej podgląd pokazałby pustą siatkę.
   */
  private readonly fixedPreview = new Set(['09:00', '10:30', '12:00']);

  protected isFixedPreview(cell: string): boolean {
    return this.fixedPreview.has(cell);
  }

  protected readonly mode = signal<SlotGenerationMode>(this.store.slotMode());

  protected select(mode: SlotGenerationMode): void {
    this.mode.set(mode);
  }

  protected onNext(): void {
    this.store.slotMode.set(this.mode());
    this.store.persistSlotMode();
    void this.router.navigate(['/setup/schedule']);
  }
}
