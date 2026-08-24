import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AppointmentSlotDto } from '@core/api/api-client';

/**
 * Wybór slotu czasowego z listy zwracanej przez `GET /api/Appointments/available-slots`.
 *
 * Dzieli sloty na dwie sekcje:
 *  - „Sugerowane" — `isPreferred = true` z gap-filling-u backendu (sloty przylegające do
 *    istniejących wizyt, wypełniają luki w grafiku).
 *  - „Pozostałe" — `isPreferred = false`/undefined.
 *
 * Gdy żaden slot nie jest preferred, sekcja sugerowanych znika i nagłówek drugiej zmienia
 * się na „Dostępne terminy". Komponent jest stateless poza wyborem (kontrola w parent przez
 * `value` + `valueChange`).
 *
 * Używany przez:
 *  - `reschedule-appointment-dialog` (F3.1)
 *  - planowane: appointment-form przy tworzeniu/edycji wizyty
 */
@Component({
  selector: 'app-slot-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  template: `
    <div class="flex flex-col gap-3">
      @if (loadError()) {
        <p class="text-sm text-red-600 dark:text-red-400">
          Nie udało się pobrać slotów. Spróbuj ponownie później.
        </p>
      } @else if (loading()) {
        <div class="flex items-center gap-2 text-xs text-surface-500">
          <i class="pi pi-spin pi-spinner" aria-hidden="true"></i>
          Ładowanie wolnych terminów…
        </div>
      } @else if (slots().length === 0) {
        <p class="text-sm text-surface-500 dark:text-surface-400">
          {{ emptyLabel() }}
        </p>
      } @else {
        @if (preferredSlots().length > 0) {
          <div>
            <div class="flex items-center gap-2 mb-2">
              <p class="text-xs font-bold uppercase tracking-wider text-amber-700 dark:text-amber-300">
                Sugerowane terminy
              </p>
              <i
                class="pi pi-info-circle text-[10px] text-surface-400"
                title="Sloty przylegające do innych wizyt — wypełniają luki w grafiku."
                aria-hidden="true"
              ></i>
            </div>
            <div
              class="grid grid-cols-3 sm:grid-cols-4 gap-1.5 p-0.5"
              role="listbox"
              aria-label="Sugerowane sloty"
            >
              @for (slot of preferredSlots(); track slot.slot) {
                <button
                  type="button"
                  role="option"
                  [attr.aria-selected]="isSelected(slot)"
                  [disabled]="disabled()"
                  (click)="select(slot)"
                  class="relative rounded-xl border px-2 py-1.5 text-sm font-bold tabular-nums transition-colors"
                  [class.border-surface-900]="isSelected(slot)"
                  [class.bg-surface-900]="isSelected(slot)"
                  [class.text-surface-0]="isSelected(slot)"
                  [class.dark:bg-surface-0]="isSelected(slot)"
                  [class.dark:text-surface-900]="isSelected(slot)"
                  [class.dark:border-surface-0]="isSelected(slot)"
                  [class.border-amber-300]="!isSelected(slot)"
                  [class.dark:border-amber-700/55]="!isSelected(slot)"
                  [class.text-amber-700]="!isSelected(slot)"
                  [class.dark:text-amber-300]="!isSelected(slot)"
                  [class.bg-amber-50]="!isSelected(slot)"
                  [class.dark:bg-amber-950/35]="!isSelected(slot)"
                >
                  <span
                    class="absolute top-0.5 right-0.5 w-1.5 h-1.5 rounded-full bg-amber-500"
                    [class.opacity-0]="isSelected(slot)"
                    aria-hidden="true"
                  ></span>
                  {{ shortTime(slot.slot) }}
                </button>
              }
            </div>
          </div>
        }

        @if (otherSlots().length > 0) {
          <div>
            <p class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 mb-2">
              {{ preferredSlots().length > 0 ? 'Pozostałe terminy' : 'Dostępne terminy' }}
            </p>
            <div
              class="grid grid-cols-3 sm:grid-cols-4 gap-1.5 max-h-64 overflow-y-auto p-0.5"
              role="listbox"
              aria-label="Pozostałe sloty"
            >
              @for (slot of otherSlots(); track slot.slot) {
                <button
                  type="button"
                  role="option"
                  [attr.aria-selected]="isSelected(slot)"
                  [disabled]="disabled()"
                  (click)="select(slot)"
                  class="rounded-xl border px-2 py-1.5 text-sm font-bold tabular-nums transition-colors"
                  [class.border-surface-900]="isSelected(slot)"
                  [class.bg-surface-900]="isSelected(slot)"
                  [class.text-surface-0]="isSelected(slot)"
                  [class.dark:bg-surface-0]="isSelected(slot)"
                  [class.dark:text-surface-900]="isSelected(slot)"
                  [class.dark:border-surface-0]="isSelected(slot)"
                  [class.border-surface-300]="!isSelected(slot)"
                  [class.dark:border-surface-600]="!isSelected(slot)"
                  [class.text-surface-700]="!isSelected(slot)"
                  [class.dark:text-surface-300]="!isSelected(slot)"
                >
                  {{ shortTime(slot.slot) }}
                </button>
              }
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class SlotPickerComponent {
  /** Surowa lista slotów z backendu — komponent sam dzieli na preferred/other. */
  readonly slots = input<AppointmentSlotDto[]>([]);
  /** Aktualnie wybrany slot (`AppointmentSlotDto.slot` ↔ `'HH:mm'` lub `'HH:mm:ss'`). */
  readonly value = input<string | null>(null);
  /** Disable całej listy — np. gdy form jest w trakcie submitu. */
  readonly disabled = input<boolean>(false);
  readonly loading = input<boolean>(false);
  readonly loadError = input<boolean>(false);
  /** Komunikat gdy backend zwraca pusty array. */
  readonly emptyLabel = input<string>('Brak wolnych terminów w wybranym dniu — wybierz inną datę.');

  readonly valueChange = output<string>();

  protected readonly preferredSlots = computed(() =>
    this.slots().filter((s) => s.isPreferred === true),
  );

  protected readonly otherSlots = computed(() =>
    this.slots().filter((s) => s.isPreferred !== true),
  );

  protected isSelected(slot: AppointmentSlotDto): boolean {
    return this.value() === slot.slot;
  }

  protected select(slot: AppointmentSlotDto): void {
    if (!slot.slot) return;
    this.valueChange.emit(slot.slot);
  }

  protected shortTime(t: string | undefined): string {
    return (t ?? '').substring(0, 5);
  }
}
