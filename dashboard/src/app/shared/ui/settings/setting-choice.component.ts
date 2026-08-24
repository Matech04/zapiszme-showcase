import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';

/** Pojedyncza opcja w segmentowanym wyborze ustawienia. `value` porównywane przez ścisłą równość. */
export interface SettingChoiceOption {
  value: unknown;
  title: string;
  description?: string;
  /** 'accent' (domyślny, bursztyn) lub 'danger' (czerwony, dla opcji destrukcyjnych, np. „nie przechowuj"). */
  tone?: 'accent' | 'danger';
  testId?: string;
}

/**
 * Wspólny wiersz ustawienia typu „wybór z kart": label + opis + siatka segmentowanych przycisków.
 * Zastępuje powtarzany 6+ razy inline-Tailwind wzorzec z ustawień salonu (dostęp, potwierdzanie,
 * weryfikacja, dane klienta, Instagram, inspiracje, historia, tryb luk, widoczność kalendarza).
 *
 * Użycie:
 * ```html
 * <app-setting-choice
 *   label="Dostęp do rezerwacji online"
 *   description="Kto może zarezerwować wizytę przez publiczną stronę salonu."
 *   [options]="[{ value: 'open', title: 'Otwarte', description: '…' }, …]"
 *   [value]="store.salonModel().bookingAccessPolicy"
 *   (valueChange)="store.setBookingAccessPolicy($event)"
 * />
 * ```
 * Dodatkową treść (ostrzeżenia, pola zależne) można wrzucić przez rzutowanie do środka boxa (`<ng-content>`).
 */
@Component({
  selector: 'app-setting-choice',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50 dark:bg-surface-50/40 px-4 py-4 space-y-3"
    >
      @if (label()) {
        <div>
          <span
            class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider block mb-1"
            >{{ label() }}</span
          >
          @if (description()) {
            <p class="text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
              {{ description() }}
            </p>
          }
        </div>
      }
      <div class="grid gap-2" [class.sm:grid-cols-2]="columns() === 2" [class.sm:grid-cols-3]="columns() === 3">
        @for (opt of options(); track opt.value) {
          <button
            type="button"
            [attr.data-testid]="opt.testId ?? null"
            [disabled]="disabled()"
            class="rounded-xl border-2 px-4 py-3 text-left transition-colors disabled:opacity-60"
            [class.border-amber-500]="isActive(opt) && toneOf(opt) === 'accent'"
            [class.bg-amber-50]="isActive(opt) && toneOf(opt) === 'accent'"
            [class.dark:bg-amber-950/30]="isActive(opt) && toneOf(opt) === 'accent'"
            [class.border-red-500]="isActive(opt) && toneOf(opt) === 'danger'"
            [class.bg-red-50]="isActive(opt) && toneOf(opt) === 'danger'"
            [class.dark:bg-red-950/30]="isActive(opt) && toneOf(opt) === 'danger'"
            [class.border-surface-200]="!isActive(opt)"
            [class.dark:border-surface-200]="!isActive(opt)"
            (click)="select(opt)"
          >
            <span class="block text-sm font-black text-surface-900">{{ opt.title }}</span>
            @if (opt.description) {
              <span class="block text-xs text-surface-500 mt-1">{{ opt.description }}</span>
            }
          </button>
        }
      </div>
      <ng-content />
    </div>
  `,
})
export class SettingChoiceComponent {
  readonly label = input<string>('');
  readonly description = input<string>('');
  readonly options = input.required<readonly SettingChoiceOption[]>();
  readonly value = model<unknown>();
  readonly columns = input<2 | 3>(2);
  readonly disabled = input<boolean>(false);

  protected readonly selectedValue = computed(() => this.value());

  protected isActive(opt: SettingChoiceOption): boolean {
    return this.selectedValue() === opt.value;
  }

  protected toneOf(opt: SettingChoiceOption): 'accent' | 'danger' {
    return opt.tone ?? 'accent';
  }

  protected select(opt: SettingChoiceOption): void {
    if (this.disabled()) return;
    this.value.set(opt.value);
  }
}
