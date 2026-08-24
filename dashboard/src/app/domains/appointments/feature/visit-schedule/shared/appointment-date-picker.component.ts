import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { DatePicker } from 'primeng/datepicker';
import { PrimeTemplate } from 'primeng/api';
import { AppointmentsClient } from '@core/api/api-client';
import { formatYyyyMmDd, startOfDay } from './date-utils';
import {
  AvailabilityLevel,
  buildAvailabilityMap,
  dateMetaKey,
} from './month-availability.utils';

interface MonthChangeEvent {
  month?: number; // 1-12 wg PrimeNG
  year?: number;
}

interface DateMeta {
  day: number;
  month: number; // 0-11 wg PrimeNG
  year: number;
  otherMonth?: boolean;
  today?: boolean;
  selectable?: boolean;
}

/**
 * Wrapper na `p-date-picker` z indykatorami dostępności (czerwony / żółty / zielony).
 *
 * Pobiera `GET /api/Appointments/month-availability` dla aktualnie wyświetlanego miesiąca
 * (start = miesiąc z `value` lub bieżący) i przy każdej zmianie miesiąca w panelu.
 * Kolor kropki pod numerem dnia → poziom dostępności:
 *   none   = czerwony (brak miejsc),
 *   low    = pomarańczowy (1-2 sloty),
 *   medium = żółty (3-5),
 *   high   = zielony (≥6).
 *
 * Gdy `employeeId` lub `serviceId` puste — komponent działa jako zwykły date-picker bez
 * indykatorów (nie ma sensu fetchować bez kontekstu pracownik+usługa).
 */
@Component({
  selector: 'app-appointment-date-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, DatePicker, PrimeTemplate],
  template: `
    <p-date-picker
      [inputId]="inputId()"
      [ngModel]="datePickerValue()"
      (ngModelChange)="onDateChange($event)"
      [minDate]="minDate()"
      [maxDate]="maxDate()"
      [disabled]="disabled()"
      [disabledDates]="disabledDates()"
      [showIcon]="true"
      [readonlyInput]="true"
      dateFormat="dd.mm.yy"
      [firstDayOfWeek]="1"
      [fluid]="true"
      appendTo="body"
      (onMonthChange)="onMonthChange($event)"
    >
      <ng-template pTemplate="date" let-date>
        <span
          class="apt-date-cell relative inline-flex flex-col items-center justify-center"
          [class.opacity-40]="levelFor(date) === 'none'"
          [class.line-through]="levelFor(date) === 'none'"
        >
          <span>{{ date.day }}</span>
          @if (levelFor(date); as level) {
            <span
              class="absolute -bottom-1 w-1.5 h-1.5 rounded-full"
              [class.bg-red-500]="level === 'none'"
              [class.bg-orange-500]="level === 'low'"
              [class.bg-amber-400]="level === 'medium'"
              [class.bg-emerald-500]="level === 'high'"
              [attr.aria-label]="legendLabel(level)"
            ></span>
          }
        </span>
      </ng-template>
    </p-date-picker>

    @if (showLegend() && hasData()) {
      <div class="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-surface-600 dark:text-surface-300">
        <span class="inline-flex items-center gap-1">
          <span class="w-2 h-2 rounded-full bg-emerald-500" aria-hidden="true"></span>dużo miejsc
        </span>
        <span class="inline-flex items-center gap-1">
          <span class="w-2 h-2 rounded-full bg-amber-400" aria-hidden="true"></span>średnio
        </span>
        <span class="inline-flex items-center gap-1">
          <span class="w-2 h-2 rounded-full bg-orange-500" aria-hidden="true"></span>mało
        </span>
        <span class="inline-flex items-center gap-1">
          <span class="w-2 h-2 rounded-full bg-red-500" aria-hidden="true"></span>brak
        </span>
      </div>
    }
  `,
})
export class AppointmentDatePickerComponent {
  /** Pracownik wybrany w formularzu — jeśli puste, indykatory nie są pobierane. */
  readonly employeeId = input<string>('');
  /** Usługa wybrana w formularzu — jeśli puste, indykatory nie są pobierane. */
  readonly serviceId = input<string>('');
  /** Wartość daty jako `yyyy-MM-dd` (lub puste). */
  readonly value = input<string>('');
  readonly minDate = input<Date | undefined>(startOfDay(new Date()));
  readonly maxDate = input<Date | undefined>(undefined);
  readonly disabled = input<boolean>(false);
  readonly inputId = input<string>('');
  readonly showLegend = input<boolean>(true);

  readonly valueChange = output<string>();

  private readonly appointmentsClient = inject(AppointmentsClient);

  /** Aktualnie wyświetlany w panelu kalendarza miesiąc (1-12) + rok. */
  private readonly viewport = signal<{ year: number; month: number }>(this.initialViewport());

  protected readonly datePickerValue = computed(() => {
    const iso = this.value();
    if (!iso) return null;
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
    if (!m) return null;
    return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  });

  protected readonly availabilityResource = rxResource({
    params: () => {
      const emp = this.employeeId();
      const svc = this.serviceId();
      if (!emp || !svc) return undefined;
      const vp = this.viewport();
      return `${vp.year}-${vp.month}\x1e${emp}\x1e${svc}`;
    },
    stream: () => {
      const emp = this.employeeId();
      const svc = this.serviceId();
      if (!emp || !svc) return of([]);
      const vp = this.viewport();
      return this.appointmentsClient
        .getMonthAvailability(vp.year, vp.month, emp, [svc])
        .pipe(catchError(() => of([])));
    },
  });

  protected readonly availabilityMap = computed(() =>
    buildAvailabilityMap(this.availabilityResource.value()),
  );

  protected readonly hasData = computed(() => this.availabilityMap().size > 0);

  /**
   * Dni z `availableCount = 0` przekazywane do `p-date-picker[disabledDates]` —
   * PrimeNG sam blokuje pointer events i klawiaturę dla tych komórek, a custom
   * `pTemplate="date"` dodaje wizualne wyciszenie (opacity + przekreślenie).
   */
  protected readonly disabledDates = computed<Date[]>(() => {
    const out: Date[] = [];
    for (const [key, level] of this.availabilityMap()) {
      if (level !== 'none') continue;
      const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(key);
      if (!m) continue;
      out.push(new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])));
    }
    return out;
  });

  constructor() {
    /**
     * Gdy nadrzędny formularz ustawi `value` na datę z innego miesiąca (np. context z
     * kalendarza dnia po kliknięciu konkretnego slotu), zsynchronizuj viewport — żeby
     * dane dostępności były pobrane dla tego widoku zaraz po otwarciu.
     */
    effect(() => {
      const v = this.value();
      if (!v) return;
      const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v);
      if (!m) return;
      const year = Number(m[1]);
      const month = Number(m[2]);
      const current = untracked(() => this.viewport());
      if (current.year !== year || current.month !== month) {
        this.viewport.set({ year, month });
      }
    });
  }

  protected onDateChange(value: Date | string | null | undefined): void {
    if (value == null || value === '') {
      this.valueChange.emit('');
      return;
    }
    if (typeof value === 'string') {
      this.valueChange.emit(value);
      return;
    }
    this.valueChange.emit(formatYyyyMmDd(value));
  }

  protected onMonthChange(event: MonthChangeEvent): void {
    if (event.year == null || event.month == null) return;
    this.viewport.set({ year: event.year, month: event.month });
  }

  protected levelFor(meta: DateMeta): AvailabilityLevel | null {
    if (meta.otherMonth) return null;
    return this.availabilityMap().get(dateMetaKey(meta)) ?? null;
  }

  protected legendLabel(level: AvailabilityLevel): string {
    switch (level) {
      case 'none': return 'Brak wolnych miejsc';
      case 'low': return 'Mało wolnych miejsc';
      case 'medium': return 'Średnio wolnych miejsc';
      case 'high': return 'Dużo wolnych miejsc';
    }
  }

  private initialViewport(): { year: number; month: number } {
    const now = new Date();
    return { year: now.getFullYear(), month: now.getMonth() + 1 };
  }
}
