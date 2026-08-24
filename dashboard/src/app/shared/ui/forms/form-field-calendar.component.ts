import { Component, computed, input, model } from '@angular/core';
import { FormValueControl, ValidationError } from '@angular/forms/signals';
import { DatePicker } from 'primeng/datepicker';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-form-field-calendar',
  standalone: true,
  imports: [DatePicker, FormsModule],
  template: `
<div class="flex flex-col gap-2 w-full">
  <label [for]="id()" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
    {{ label() }}
  </label>

  <div
    class="rounded-xl transition-all duration-200"
    [class.ring-2]="showInvalid()"
    [class.ring-red-500]="showInvalid()"
    [class.dark:ring-red-400]="showInvalid()"
  >
    <p-date-picker
      [inputId]="id()"
      [attr.data-testid]="testId()"
      [showIcon]="true"
      [readonlyInput]="true"
      dateFormat="dd.mm.yy"
      [ngModel]="dateValue()"
      (ngModelChange)="onDateChange($event)"
      [minDate]="minDateBound()"
      [disabled]="disabled()"
      [fluid]="true"
      styleClass="w-full"
    />
  </div>

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
  `
})
export class FormFieldCalendarComponent implements FormValueControl<string | null> {
  readonly value = model<string | null>(null);
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  readonly touched = model<boolean>(false);

  label = input<string>();
  id = input.required<string>();
  testId = input<string>();
  /** Minimalna data (np. początek urlopu) — `yyyy-MM-dd`. */
  notBefore = input<string | undefined>();
  disabled = input<boolean>(false);

  showInvalid = computed(() => this.touched() && this.errors().length > 0);

  minDateBound = computed(() => this.parseYyyyMmDd(this.notBefore()));

  dateValue = computed(() => this.parseYyyyMmDd(this.value() ?? undefined));

  onDateChange(d: Date | null | undefined) {
    this.value.set(d ? this.toYyyyMmDd(d) : '');
    this.markAsTouched();
  }

  markAsTouched() {
    this.touched.set(true);
  }

  private parseYyyyMmDd(s: string | undefined): Date | undefined {
    if (s == null || s === '') {
      return undefined;
    }
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s.trim());
    if (!m) {
      return undefined;
    }
    const y = Number(m[1]);
    const mo = Number(m[2]);
    const d = Number(m[3]);
    return new Date(y, mo - 1, d);
  }

  private toYyyyMmDd(d: Date): string {
    const y = d.getFullYear();
    const mo = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${mo}-${day}`;
  }
}
