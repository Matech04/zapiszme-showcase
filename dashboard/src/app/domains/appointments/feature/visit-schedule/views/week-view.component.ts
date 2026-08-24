import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AppointmentPreviewDto } from '@core/api/api-client';
import {
  AppointmentStatusVariant,
  statusTokens,
  statusVariantFromIdOrName,
} from '@core/theme/status-tokens';
import type { CalendarFiltersValue } from '../filters/calendar-filters.component';
import { filterAppointments } from '../shared/filters';

interface WeekDay {
  date: Date;
  dayKey: string;
  weekday: string;
  dayLabel: string;
  isToday: boolean;
  appointments: AgendaChip[];
}

interface AgendaChip {
  id: string;
  time: string;
  serviceName: string;
  customerLine: string;
  startMin: number;
  variant: AppointmentStatusVariant;
}

const PL_WEEKDAYS = ['Niedz.', 'Pon.', 'Wt.', 'Śr.', 'Czw.', 'Pt.', 'Sob.'] as const;

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function parseHm(t: string | undefined): number {
  if (!t) return 0;
  const [h = '0', m = '0'] = t.split(':');
  return Number(h) * 60 + Number(m);
}

function trimHm(t: string | undefined): string {
  return (t ?? '').substring(0, 5);
}

function deklinacjaWizyt(n: number): string {
  if (n === 1) return 'wizyta';
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14)) return 'wizyty';
  return 'wizyt';
}

/**
 * Tygodniowy przegląd desktop — 7 kolumn dni, w każdej pionowa lista czytelnych chipów wizyt
 * (godzina + usługa + klient, kolor statusu). Zastąpiło wcześniejszą mikro-oś czasu (16 px/h),
 * gdzie wizyty były nieczytelne. Tap w kolumnę → drill-down do widoku dnia.
 */
@Component({
  selector: 'app-week-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  template: `
    <div class="admin-glass-card rounded-3xl p-3 sm:p-4">
      <div class="flex items-center justify-between mb-3">
        <div>
          <p class="admin-section-label text-primary">Tydzień</p>
          <h2 class="admin-h3 text-surface-900">{{ rangeLabel() }}</h2>
        </div>
        <div class="flex items-center gap-1.5">
          <button
            type="button"
            (click)="prev.emit()"
            class="h-11 w-11 rounded-xl border border-surface-300 dark:border-surface-600 grid place-items-center text-surface-700 hover:border-primary/45 transition-colors"
            aria-label="Poprzedni tydzień"
          >
            <i class="pi pi-chevron-left text-sm"></i>
          </button>
          <button
            type="button"
            (click)="today.emit()"
            class="h-11 inline-flex items-center rounded-xl border border-surface-300 dark:border-surface-600 px-3 text-xs font-bold uppercase tracking-wider hover:border-primary/45 transition-colors"
          >
            Dziś
          </button>
          <button
            type="button"
            (click)="next.emit()"
            class="h-11 w-11 rounded-xl border border-surface-300 dark:border-surface-600 grid place-items-center text-surface-700 hover:border-primary/45 transition-colors"
            aria-label="Następny tydzień"
          >
            <i class="pi pi-chevron-right text-sm"></i>
          </button>
        </div>
      </div>

      <div class="grid grid-cols-7 gap-2 sm:gap-3">
        @for (d of days(); track d.dayKey) {
          <button
            type="button"
            (click)="dayClick.emit(d.date)"
            class="rounded-2xl border bg-white/60 dark:bg-surface-50/45 hover:border-primary/45 transition-colors flex flex-col text-left overflow-hidden focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
            [class.border-amber-400]="d.isToday"
            [class.ring-1]="d.isToday"
            [class.ring-amber-400]="d.isToday"
            [class.border-surface-200/70]="!d.isToday"
            [class.dark:border-surface-200/70]="!d.isToday"
            [attr.aria-label]="d.weekday + ' ' + d.dayLabel + ', ' + d.appointments.length + ' ' + count(d.appointments.length)"
          >
            <header
              class="px-2 py-2 border-b border-surface-200/70 dark:border-surface-200/70 flex items-center justify-between gap-1"
            >
              <span class="text-[10px] font-bold uppercase tracking-wider text-surface-500">{{ d.weekday }}</span>
              <span
                class="text-sm font-black"
                [class.text-amber-600]="d.isToday"
                [class.dark:text-amber-300]="d.isToday"
                [class.text-surface-900]="!d.isToday"
                [class.dark:text-surface-900]="!d.isToday"
                >{{ d.dayLabel }}</span
              >
            </header>

            <div class="flex flex-col gap-1 p-1.5 h-[20rem] sm:h-[23rem] overflow-y-auto scrollbar-thin">
              @for (a of d.appointments; track a.id) {
                <span
                  class="block rounded-lg border px-2 py-1 overflow-hidden"
                  [class]="surfaceFor(a.variant)"
                  [title]="a.time + ' • ' + a.serviceName + ' • ' + a.customerLine"
                >
                  <span class="flex items-baseline gap-1.5 min-w-0">
                    <span class="text-[11px] font-bold tabular-nums shrink-0">{{ a.time }}</span>
                    <span class="text-[11px] font-semibold truncate min-w-0">{{ a.serviceName }}</span>
                  </span>
                  <span class="block text-[10px] opacity-75 truncate leading-tight">{{ a.customerLine }}</span>
                </span>
              } @empty {
                <span class="flex-1 grid place-items-center text-[11px] text-surface-300 dark:text-surface-600">—</span>
              }
            </div>

            <footer class="px-2 py-1.5 border-t border-surface-200/70 dark:border-surface-200/70 text-[10px] font-semibold text-surface-500">
              {{ d.appointments.length }} {{ count(d.appointments.length) }}
            </footer>
          </button>
        }
      </div>
    </div>
  `,
})
export class WeekViewComponent {
  readonly anchor = input.required<Date>();
  readonly appointments = input.required<AppointmentPreviewDto[]>();
  /** Filtry kalendarza (statusy / pracownicy / fraza) — wspólne dla wszystkich widoków. */
  readonly filters = input<CalendarFiltersValue>({
    employeeIds: [],
    statuses: [],
    searchQuery: '',
  });

  readonly dayClick = output<Date>();
  readonly prev = output<void>();
  readonly next = output<void>();
  readonly today = output<void>();

  protected readonly count = deklinacjaWizyt;

  readonly days = computed<WeekDay[]>(() => {
    const a = startOfDay(this.anchor());
    // Poniedziałek = 0
    const monIdx = (a.getDay() + 6) % 7;
    const start = new Date(a);
    start.setDate(a.getDate() - monIdx);
    const today = startOfDay(new Date());
    // Wspólny filtr statusów/pracownika/frazy zamiast lokalnego ukrywania canceled.
    const filtered = filterAppointments(this.appointments(), this.filters());
    const days: WeekDay[] = [];
    for (let i = 0; i < 7; i++) {
      const d = new Date(start);
      d.setDate(start.getDate() + i);
      const dayKey = d.toISOString().slice(0, 10);
      const items: AgendaChip[] = filtered
        .filter((x) => {
          if (!x.date) return false;
          return startOfDay(new Date(x.date as unknown as string)).getTime() === d.getTime();
        })
        .map<AgendaChip>((x) => ({
          id: x.id ?? `${dayKey}-${x.startTime}`,
          time: trimHm(x.startTime),
          serviceName: x.serviceName ?? 'Wizyta',
          customerLine:
            x.isGuest === true
              ? 'Gość'
              : [x.customerFirstName, x.customerLastName]
                  .filter((s): s is string => !!s)
                  .join(' ') || (x.customerPhoneNumber ?? '—'),
          startMin: parseHm(x.startTime),
          variant: statusVariantFromIdOrName(x.status?.id, x.status?.name),
        }))
        .sort((x, y) => x.startMin - y.startMin);
      days.push({
        date: d,
        dayKey,
        weekday: PL_WEEKDAYS[d.getDay()] ?? '—',
        dayLabel: `${d.getDate()}.${String(d.getMonth() + 1).padStart(2, '0')}`,
        isToday: d.getTime() === today.getTime(),
        appointments: items,
      });
    }
    return days;
  });

  readonly rangeLabel = computed(() => {
    const list = this.days();
    if (list.length === 0) return '';
    const fmt = (d: Date): string =>
      d.toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' });
    return `${fmt(list[0].date)} – ${fmt(list[6].date)}`;
  });

  surfaceFor(v: AppointmentStatusVariant): string {
    const t = statusTokens(v);
    return `${t.surfaceClasses} ${t.textClasses}`;
  }
}
