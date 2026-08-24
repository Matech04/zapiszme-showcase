import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AppointmentPreviewDto,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  ScheduleOverrideDto,
} from '@core/api/api-client';
import {
  AppointmentStatusVariant,
  statusTokens,
  statusVariantFromIdOrName,
} from '@core/theme/status-tokens';
import type { CalendarFiltersValue } from '../filters/calendar-filters.component';
import { filterAppointments } from '../shared/filters';
import { resolveFixedStartTimesForDate } from '../shared/schedule-resolution';
import { formatYyyyMmDd } from '../shared/date-utils';
import {
  DayAvailability,
  dayAvailabilityAccentClass,
  leaveBadgeClass,
  leaveLabel,
  overrideBadgeClass,
  resolveDayAvailability,
} from '../shared/day-availability';

interface MonthChip {
  time: string;
  serviceName: string;
  startMin: number;
  variant: AppointmentStatusVariant;
}

/** Slot grafiku statycznego (stała godzina startu) z oznaczeniem zajętości. */
interface MonthSlot {
  time: string;
  taken: boolean;
}

/**
 * Kontekst do wyliczenia slotów grafiku STATYCZNEGO w komórkach miesiąca: konfiguracja grafiku
 * jednoznacznie oglądanego pracownika + mapa zajętości (`yyyy-MM-dd` → przedziały minutowe
 * niezanulowanych wizyt). `null` w widoku, gdy nie ma jednego pracownika (agregat) — wtedy
 * komórki pokazują chipy wizyt jak dotąd.
 */
export interface MonthStaticSlots {
  cfg: {
    sched?: EmployeeScheduleDto[];
    overrides?: ScheduleOverrideDto[];
    leaves?: EmployeeLeaveDto[];
  };
  busy: Map<string, { s: number; e: number }[]>;
}

interface MonthCell {
  date: Date;
  inMonth: boolean;
  isToday: boolean;
  isSelected: boolean;
  total: number;
  pending: number;
  items: MonthChip[];
  intensityClass: string;
  /** Sloty grafiku statycznego dnia (override > grafik) lub `null` gdy dzień nie jest statyczny. */
  fixedSlots: MonthSlot[] | null;
  /** Tekstowy badge dostępności (desktop): urlop / dzień specjalny / godziny pracy / wolne. */
  availBadge: { text: string; cls: string } | null;
  /** Kolor akcentu dostępności (mobile, pasek z lewej) lub `null` gdy brak kontekstu pracownika. */
  availAccent: string | null;
  /** Dzień zamknięty (poza grafikiem, bez wizyt) — wygaszamy komórkę, by odróżnić od dnia pracy. */
  isClosed: boolean;
}

const PL_WEEKDAYS = ['Pn', 'Wt', 'Śr', 'Cz', 'Pt', 'So', 'Nd'] as const;
/** Ile chipów wizyt pokazać w komórce zanim pojawi się „+N więcej". */
const DESKTOP_CHIP_LIMIT = 3;
/** Ile kafelków slotów grafiku stałego pokazać w komórce zanim pojawi się „+N". */
const SLOT_LIMIT = 6;
/** Tło komórki dnia zamkniętego (poza grafikiem, bez wizyt) — wygaszone, odróżnia od dnia pracy. */
const CLOSED_CELL_CLASS =
  'border-surface-200/50 dark:border-surface-100/40 bg-surface-100/70 dark:bg-surface-100/30 text-surface-400 dark:text-surface-500';

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

function parseHm(t: string | undefined): number {
  if (!t) return 0;
  const [h = '0', m = '0'] = t.split(':');
  return Number(h) * 60 + Number(m);
}

@Component({
  selector: 'app-month-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  template: `
    <!-- Od ~640 px (sm): siatka miesiąca z pełnymi chipami wizyt / kafelkami slotów i tekstowym
         badge dostępności. W powłoce mobilnej (bez panelu bocznego) kolumny są szersze niż w
         desktopie z sidebarem, więc treść mieści się już od sm — nie ma sensu chować jej do lg.
         Poniżej sm (telefon w pionie) renderujemy agendę, bo w 7 kolumnach mieści się tylko numer. -->
    <div class="admin-glass-card rounded-3xl p-3 sm:p-4 hidden sm:block">
      <div class="grid grid-cols-7 gap-1 mb-2">
        @for (label of weekdayLabels; track label) {
          <div class="text-[10px] font-bold uppercase tracking-wider text-surface-500 text-center py-1">
            {{ label }}
          </div>
        }
      </div>
      <div class="grid grid-cols-7 gap-1.5">
        @for (cell of cells(); track cell.date.getTime()) {
          <button
            type="button"
            (click)="cellClick.emit(cell.date)"
            class="relative rounded-xl border flex flex-col p-1.5 text-left transition-colors hover:border-primary/45 min-h-[6rem] md:min-h-[7rem] lg:min-h-[7.5rem] overflow-hidden focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
            [class.opacity-40]="!cell.inMonth"
            [class]="cell.intensityClass"
            [class.ring-2]="cell.isSelected"
            [class.ring-primary]="cell.isSelected"
            [class.border-primary]="cell.isSelected"
            [attr.aria-current]="cell.isToday ? 'date' : null"
          >
            @if (cell.availAccent) {
              <!-- Kolorowy pasek akcentu rodzaju dnia — górna krawędź komórki. -->
              <span
                class="lg:hidden absolute inset-x-0 top-0 h-1 rounded-t-xl"
                [ngClass]="cell.availAccent"
                aria-hidden="true"
              ></span>
            }
            <span class="flex items-center justify-center text-[11px] font-bold leading-none shrink-0">
              <span
                class="grid place-items-center w-5 h-5 rounded-full"
                [class.bg-amber-500]="cell.isToday"
                [class.text-white]="cell.isToday"
                [class.font-black]="cell.isToday"
                >{{ cell.date.getDate() }}</span
              >
            </span>
            @if (cell.pending > 0) {
              <span
                class="absolute top-1.5 right-1.5 w-1.5 h-1.5 rounded-full bg-amber-500"
                aria-label="Oczekujące"
              ></span>
            }

            @if (cell.availBadge) {
              <!-- Badge dostępności (od sm): urlop / dzień specjalny / godziny pracy / wolne. -->
              <span
                class="hidden sm:inline-flex items-center max-w-full mt-1 px-1 md:px-1.5 py-0.5 rounded text-[10px] font-semibold border truncate leading-none"
                [ngClass]="cell.availBadge.cls"
                [title]="cell.availBadge.text"
              >
                {{ cell.availBadge.text }}
              </span>
            }

            @if (cell.fixedSlots) {
              <!-- Dzień statyczny: kafelki godzin startu (wolne/zajęte) — od sm. -->
              <div class="flex flex-wrap gap-0.5 mt-1 min-w-0">
                @for (slot of cell.fixedSlots.slice(0, slotLimit); track slot.time) {
                  <span
                    class="text-center min-w-[2.25rem] px-1 py-0.5 rounded text-[10px] font-semibold border leading-none"
                    [class.bg-emerald-500/15]="!slot.taken"
                    [class.text-emerald-800]="!slot.taken"
                    [class.dark:text-emerald-200]="!slot.taken"
                    [class.border-emerald-500/40]="!slot.taken"
                    [class.bg-surface-200/50]="slot.taken"
                    [class.dark:bg-surface-200/40]="slot.taken"
                    [class.text-surface-500]="slot.taken"
                    [class.dark:text-surface-500]="slot.taken"
                    [class.border-surface-300]="slot.taken"
                    [class.dark:border-surface-300]="slot.taken"
                    [class.line-through]="slot.taken"
                    [title]="slot.time + (slot.taken ? ' — zajęty' : ' — wolny')"
                  >
                    {{ slot.time }}
                  </span>
                }
                @if (cell.fixedSlots.length > slotLimit) {
                  <span class="self-center px-1 text-[10px] font-bold text-surface-500 dark:text-surface-400">
                    +{{ cell.fixedSlots.length - slotLimit }}
                  </span>
                }
              </div>
            } @else if (cell.total > 0) {
              <!-- Podgląd wizyt jako chipy (godzina + usługa) — od sm. -->
              <div class="flex flex-col gap-0.5 mt-1 min-w-0">
                @for (chip of cell.items.slice(0, chipLimit); track $index) {
                  <span
                    class="rounded-md border px-1 md:px-1.5 py-0.5 text-[10px] font-semibold leading-tight truncate"
                    [class]="chipClass(chip.variant)"
                  >
                    <span class="tabular-nums">{{ chip.time }}</span> {{ chip.serviceName }}
                  </span>
                }
                @if (cell.total > chipLimit) {
                  <span class="px-1 text-[10px] font-bold text-surface-500 dark:text-surface-400">
                    +{{ cell.total - chipLimit }} więcej
                  </span>
                }
              </div>
            }
          </button>
        }
      </div>
    </div>

    <!-- Telefon w pionie (< sm): agenda miesiąca — czytelna lista dni (data + dostępność + wizyty)
         zamiast ciasnej siatki. Tap w wiersz otwiera ten sam sheet dnia co siatka. -->
    <div
      class="sm:hidden admin-glass-card rounded-3xl overflow-hidden divide-y divide-surface-200/70 dark:divide-surface-100"
    >
      @for (cell of agendaCells(); track cell.date.getTime()) {
        <button
          type="button"
          (click)="cellClick.emit(cell.date)"
          class="w-full text-left flex items-stretch gap-3 px-3 py-3 min-h-14 transition-colors hover:bg-surface-50 dark:hover:bg-surface-100/60 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-inset"
          [class.bg-primary/5]="cell.isSelected"
        >
          <span class="w-1 rounded-full shrink-0" [ngClass]="cell.availAccent ?? 'bg-surface-200 dark:bg-surface-100'" aria-hidden="true"></span>

          <div class="shrink-0 w-11 flex flex-col items-center justify-center">
            <span class="text-[11px] font-bold uppercase tracking-wide text-surface-500 dark:text-surface-400">
              {{ weekdayShort(cell.date) }}
            </span>
            <span
              class="text-lg font-black leading-none"
              [class.text-primary]="cell.isToday"
              [class.text-surface-900]="!cell.isToday"
            >
              {{ cell.date.getDate() }}
            </span>
          </div>

          <div class="flex-1 min-w-0 flex flex-col justify-center gap-1.5">
            @if (cell.availBadge) {
              <span
                class="self-start inline-flex items-center px-2 py-0.5 rounded-md text-xs font-bold border max-w-full truncate"
                [ngClass]="cell.availBadge.cls"
              >
                {{ cell.availBadge.text }}
              </span>
            }
            @if (cell.fixedSlots) {
              <!-- Grafik stały: wolne/zajęte godziny startu (terminy) — zielone=wolne, przekreślone=zajęte. -->
              <div class="flex flex-wrap gap-1">
                @for (slot of cell.fixedSlots; track slot.time) {
                  <span
                    class="px-1.5 py-0.5 rounded text-xs font-semibold border leading-none"
                    [class.bg-emerald-500/15]="!slot.taken"
                    [class.text-emerald-800]="!slot.taken"
                    [class.dark:text-emerald-200]="!slot.taken"
                    [class.border-emerald-500/40]="!slot.taken"
                    [class.bg-surface-200/50]="slot.taken"
                    [class.dark:bg-surface-200/40]="slot.taken"
                    [class.text-surface-500]="slot.taken"
                    [class.dark:text-surface-500]="slot.taken"
                    [class.border-surface-300]="slot.taken"
                    [class.dark:border-surface-300]="slot.taken"
                    [class.line-through]="slot.taken"
                    [title]="slot.time + (slot.taken ? ' — zajęty' : ' — wolny')"
                  >
                    {{ slot.time }}
                  </span>
                }
              </div>
            } @else if (cell.total > 0) {
              <!-- Tryb elastyczny: w dniu bywa wiele wizyt — pokazujemy tylko liczbę (szczegóły po wejściu w dzień). -->
              <span class="self-start inline-flex items-center gap-1.5 text-sm font-semibold text-surface-700 dark:text-surface-300">
                <i class="pi pi-calendar-clock text-surface-400 dark:text-surface-500 text-xs" aria-hidden="true"></i>
                {{ visitCountLabel(cell.total) }}
                @if (cell.pending > 0) {
                  <span class="text-[11px] font-bold text-amber-600 dark:text-amber-400">
                    · {{ cell.pending }} do zatwierdzenia
                  </span>
                }
              </span>
            } @else if (!cell.availBadge) {
              <span class="text-sm text-surface-400 dark:text-surface-500 italic">Brak wizyt</span>
            }
          </div>

          <span class="shrink-0 self-center text-surface-300 dark:text-surface-600">
            <i class="pi pi-chevron-right text-xs" aria-hidden="true"></i>
          </span>
        </button>
      }
    </div>
  `,
})
export class MonthViewComponent {
  readonly anchor = input.required<Date>();
  readonly selected = input.required<Date>();
  readonly appointments = input.required<AppointmentPreviewDto[]>();
  /** Filtry kalendarza — zliczanie i znacznik pending dotyczą wizyt przepuszczonych przez filtr. */
  readonly filters = input<CalendarFiltersValue>({
    employeeIds: [],
    statuses: [],
    searchQuery: '',
  });
  /** Kontekst slotów grafiku statycznego (jeden pracownik). `null` → komórki pokazują chipy wizyt. */
  readonly staticSlots = input<MonthStaticSlots | null>(null);

  readonly cellClick = output<Date>();

  protected readonly weekdayLabels = PL_WEEKDAYS;
  protected readonly chipLimit = DESKTOP_CHIP_LIMIT;
  protected readonly slotLimit = SLOT_LIMIT;

  /** Dni bieżącego miesiąca (bez paddingu sąsiednich) — źródło agendy mobilnej. */
  readonly agendaCells = computed<MonthCell[]>(() => this.cells().filter((c) => c.inMonth));

  /** Skrót dnia tygodnia (Pn..Nd) dla wiersza agendy. */
  protected weekdayShort(date: Date): string {
    return PL_WEEKDAYS[(date.getDay() + 6) % 7];
  }

  /** Liczba wizyt z polską odmianą: „1 wizyta" / „3 wizyty" / „5 wizyt". */
  protected visitCountLabel(n: number): string {
    if (n === 1) return '1 wizyta';
    const mod10 = n % 10;
    const mod100 = n % 100;
    if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14)) return `${n} wizyty`;
    return `${n} wizyt`;
  }

  readonly cells = computed<MonthCell[]>(() => {
    const month = startOfMonth(this.anchor());
    const today = startOfDay(new Date());
    const sel = startOfDay(this.selected());

    const firstWeekday = (month.getDay() + 6) % 7;
    const start = new Date(month);
    start.setDate(start.getDate() - firstWeekday);

    const cells: MonthCell[] = [];
    const slots = this.staticSlots();
    // Filtr stosowany RAZ na początku — `total`, `pending` i chipy reagują na chipy/frazę/pracowników.
    const filtered = filterAppointments(this.appointments(), this.filters());
    const byDay = new Map<string, AppointmentPreviewDto[]>();
    for (const a of filtered) {
      if (!a.date) continue;
      const d = startOfDay(new Date(a.date as unknown as string));
      const key = d.toISOString().slice(0, 10);
      const arr = byDay.get(key) ?? [];
      arr.push(a);
      byDay.set(key, arr);
    }

    for (let i = 0; i < 42; i++) {
      const d = new Date(start);
      d.setDate(start.getDate() + i);
      const key = d.toISOString().slice(0, 10);
      const list = byDay.get(key) ?? [];
      const total = list.length;
      let pending = 0;
      const items: MonthChip[] = list
        .map((x) => {
          const variant = statusVariantFromIdOrName(x.status?.id, x.status?.name);
          if (variant === 'pending') pending += 1;
          return {
            time: (x.startTime ?? '').substring(0, 5),
            serviceName: x.serviceName ?? 'Wizyta',
            startMin: parseHm(x.startTime),
            variant,
          };
        })
        .sort((a, b) => a.startMin - b.startMin);

      // Sloty grafiku statycznego (override > grafik tygodniowy). `null` = dzień dynamiczny/wolny → chipy wizyt.
      let fixedSlots: MonthSlot[] | null = null;
      if (slots) {
        const fixed = resolveFixedStartTimesForDate(slots.cfg.sched, slots.cfg.overrides, slots.cfg.leaves, d);
        if (fixed) {
          const busy = slots.busy.get(formatYyyyMmDd(d)) ?? [];
          fixedSlots = fixed
            .map((t) => {
              const hm = t.substring(0, 5);
              const m = parseHm(hm);
              const taken = busy.some((r) => r.s <= m && m < r.e);
              return { time: hm, taken, startMin: m };
            })
            .sort((a, b) => a.startMin - b.startMin)
            .map(({ time, taken }) => ({ time, taken }));
        }
      }

      // Warstwa dostępności (tylko gdy oglądamy jednego pracownika — mamy jego grafik/urlopy).
      // Wizyty renderujemy zawsze na wierzchu; badge dostępności to tło informacyjne.
      let availBadge: MonthCell['availBadge'] = null;
      let availAccent: string | null = null;
      let isClosed = false;
      if (slots) {
        const avail = resolveDayAvailability(slots.cfg.sched, slots.cfg.overrides, slots.cfg.leaves, d);
        availAccent = dayAvailabilityAccentClass(avail);
        availBadge = this.buildAvailBadge(avail, !!fixedSlots, total);
        isClosed = avail.status === 'off' && total === 0;
      }

      cells.push({
        date: d,
        inMonth: d.getMonth() === month.getMonth(),
        isToday: sameDay(d, today),
        isSelected: sameDay(d, sel),
        total,
        pending,
        items,
        intensityClass: isClosed ? CLOSED_CELL_CLASS : this.intensityClassFor(total),
        fixedSlots,
        availBadge,
        availAccent,
        isClosed,
      });
    }
    return cells;
  });

  protected chipClass(variant: AppointmentStatusVariant): string {
    const t = statusTokens(variant);
    return `${t.surfaceClasses} ${t.textClasses}`;
  }

  /**
   * Tekstowy badge dostępności komórki (desktop). Priorytet: urlop > dzień specjalny > godziny
   * pracy (grafik dynamiczny) > wolne. Dla dnia w trybie stałych slotów (`hasFixed`) pomijamy —
   * sloty wolne/zajęte już pokazują godziny. „Wolne" pokazujemy tylko gdy brak wizyt (mniej szumu).
   */
  private buildAvailBadge(
    av: DayAvailability,
    hasFixed: boolean,
    total: number,
  ): MonthCell['availBadge'] {
    // Dzień w trybie stałych slotów (bazowy grafik LUB dzień specjalny „fixed") — godziny pokazują
    // już same kafelki slotów; badge byłby duplikatem tych samych godzin. Pomijamy.
    if (hasFixed) {
      return null;
    }
    if (av.leave) {
      return { text: leaveLabel(av.leave.type), cls: leaveBadgeClass(av.leave.type, av.leave.status) };
    }
    if (av.override) {
      return {
        text: av.override.isDayOff ? 'Wolne (wyj.)' : av.override.summary,
        cls: overrideBadgeClass(av.override.isDayOff),
      };
    }
    if (av.status === 'work' && !hasFixed && av.scheduleSummary) {
      return {
        text: av.scheduleSummary,
        cls: 'bg-emerald-500/12 text-emerald-800 dark:text-emerald-200 border-emerald-500/35',
      };
    }
    if (av.status === 'off' && total === 0) {
      return { text: 'Wolne', cls: 'bg-surface-200/40 text-surface-500 border-surface-300/60' };
    }
    return null;
  }

  private intensityClassFor(count: number): string {
    if (count === 0) return 'border-surface-200/70 dark:border-surface-200/60 bg-white/45 dark:bg-surface-50/40 text-surface-700 dark:text-surface-300';
    if (count < 3) return 'border-emerald-200/60 dark:border-emerald-800/40 bg-emerald-50/60 dark:bg-emerald-950/25 text-surface-800';
    if (count < 6) return 'border-emerald-300/70 dark:border-emerald-700/50 bg-emerald-100/70 dark:bg-emerald-950/35 text-surface-900';
    return 'border-emerald-400/80 dark:border-emerald-600/55 bg-emerald-200/70 dark:bg-emerald-900/40 text-surface-900';
  }
}
