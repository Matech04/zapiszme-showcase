import { Component, computed, input, model } from '@angular/core';
import { FormValueControl, ValidationError } from '@angular/forms/signals';
import { FormsModule } from '@angular/forms';
import type { OverlayListenerOptions, OverlayOptions } from 'primeng/api';
import { Select } from 'primeng/select';

export interface SelectOption {
  label: string;
  value: string;
}

/** Sekcja opcji z nagłówkiem — patrz `optionGroups`. */
export interface SelectOptionGroup {
  label: string;
  items: SelectOption[];
}

@Component({
  selector: 'app-form-field-select',
  standalone: true,
  imports: [FormsModule, Select],
  template: `
    <div class="flex flex-col gap-2 w-full">
      <label
        [for]="id()"
        class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1"
      >
        {{ label() }}
      </label>

      <p-select
        [inputId]="id()"
        [attr.data-testid]="testId()"
        [options]="resolvedOptions()"
        optionLabel="label"
        optionValue="value"
        [group]="isGrouped()"
        optionGroupLabel="label"
        optionGroupChildren="items"
        [filter]="filter()"
        [resetFilterOnHide]="true"
        [placeholder]="placeholder()"
        [ngModel]="value()"
        (ngModelChange)="onChange($event)"
        [showClear]="showClear()"
        [appendTo]="'body'"
        [overlayOptions]="selectOverlayOptions"
        scrollHeight="280px"
        styleClass="w-full"
        [class.ring-2]="showInvalidRing()"
        [class.ring-red-500]="showInvalidRing()"
        [class.dark:ring-red-400]="showInvalidRing()"
      />

      @if (touched() && errors().length > 0) {
        <div class="flex flex-col gap-1 px-1">
          @for (error of errors(); track error) {
            <small class="text-red-500 dark:text-red-400 text-xs font-medium animate-fadein">
              {{ $any(error).message || 'Pole jest niepoprawne' }}
            </small>
          }
        </div>
      }
    </div>
  `,
})
export class FormFieldSelectComponent implements FormValueControl<string | null> {
  /**
   * Domyślny overlay zamyka się przy scrollu rodzica (np. `overflow-y-auto` w formularzu).
   * Wyłączamy tylko `scroll`; pozostałe zachowania pozostają jak w PrimeNG (`options.valid`).
   */
  readonly selectOverlayOptions: OverlayOptions = {
    listener: (_event: Event, options?: OverlayListenerOptions) => {
      if (options?.type === 'scroll') {
        return false;
      }
      return options?.valid ?? true;
    },
  };

  readonly value = model<string | null>(null);
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  readonly touched = model<boolean>(false);

  label = input<string>();
  id = input.required<string>();
  testId = input<string>();
  placeholder = input<string>('Wybierz…');
  options = input<SelectOption[]>([]);
  /**
   * Opcje pogrupowane w sekcje z nagłówkami. Gdy podane (niepuste), mają pierwszeństwo
   * przed `options`. Wołający decyduje, czy podział ma sens — przy jednej sekcji lepiej
   * zostać przy płaskim `options`, bo samotny nagłówek to szum.
   */
  optionGroups = input<SelectOptionGroup[] | null>(null);
  showClear = input<boolean>(false);
  /** Domyślnie wyłączone — włączony filtr często daje „No results found” przy pustym polu wyszukiwania. */
  filter = input<boolean>(false);

  readonly isGrouped = computed(() => (this.optionGroups()?.length ?? 0) > 0);

  readonly resolvedOptions = computed<SelectOption[] | SelectOptionGroup[]>(() =>
    this.isGrouped() ? this.optionGroups()! : this.options(),
  );

  /** Nie nazywać `invalid` — koliduje z `FormUiControl.invalid` (input z frameworka). */
  showInvalidRing = computed(() => this.touched() && this.errors().length > 0);

  onChange(v: string | null) {
    this.value.set(v);
    this.touched.set(true);
  }
}
