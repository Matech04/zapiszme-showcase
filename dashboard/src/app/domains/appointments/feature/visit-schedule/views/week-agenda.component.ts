import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AppointmentPreviewDto,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  ScheduleOverrideDto,
} from '@core/api/api-client';
import type { CalendarFiltersValue } from '../filters/calendar-filters.component';
import { sameCalendarDay, startOfDay } from '../shared/date-utils';
import { filterAppointments } from '../shared/filters';
import {
  findBlockingLeaveForDate,
  resolveRawScheduleDayForDate,
} from '../shared/schedule-resolution';

interface WeekDay {
  date: Date;
  dayKey: string;
  weekday: string;
  dayLabel: string;
  isToday: boolean;
  /** Liczba wizyt (po filtrach) w tym dniu. */
  count: number;
  /**
   * Podpis pod datą: dla grafiku elastycznego okno pracy „HH:MM–HH:MM"; dla stałego liczba
   * terminów + start („5 terminów · od 10:00" — 14:00 to ostatni START, nie koniec pracy);
   * albo „Dzień wolny" / „Nieobecność"; null = brak danych grafiku.
   */
  workLabel: string | null;
  /** Ikona podpisu — zegar (okno godzin) albo kalendarz (grafik stały / liczba terminów). */
  workIcon: string;
  /** Dzień bez pracy (wolne / urlop) — do wyszarzenia karty. */
  isDayOff: boolean;
}

const PL_WEEKDAYS_LONG = [
  'Niedziela',
  'Poniedziałek',
  'Wtorek',
  'Środa',
  'Czwartek',
  'Piątek',
  'Sobota',
] as const;

function trimHm(t: string | undefined): string {
  if (!t) return '';
  return t.substring(0, 5);
}

function deklinacjaWizyt(n: number): string {
  if (n === 1) return 'wizyta';
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14)) return 'wizyty';
  return 'wizyt';
}

function deklinacjaTerminow(n: number): string {
  if (n === 1) return 'termin';
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14)) return 'terminy';
  return 'terminów';
}

/**
 * Mobilny widok tygodnia jako pionowy PRZEGLĄD 7 dni — bez szczegółów wizyt.
 * Każdy dzień pokazuje tylko to, co ważne na poziomie tygodnia: czas pracy i liczbę terminów.
 * Szczegóły (klient, usługa, status) są w widoku dnia — tap w dzień tam prowadzi.
 *
 * Desktop dalej renderuje 7-kolumnową siatkę z `week-view.component.ts`.
 */
@Component({
  selector: 'app-week-agenda',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  template: `
    <div
      class="admin-glass-card rounded-3xl p-3 sm:p-4 flex flex-col"
      [style.height.px]="cardHeight()"
    >
      <div class="flex items-center justify-between mb-3 gap-2 shrink-0">
        <h2 class="admin-h3 text-surface-900 truncate min-w-0">{{ rangeLabel() }}</h2>
        <div class="flex items-center gap-1.5 shrink-0">
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

      <ul class="flex flex-col gap-2 flex-1 min-h-0">
        @for (d of days(); track d.dayKey) {
          <li
            class="rounded-2xl border overflow-hidden transition-colors flex-1 min-h-0 flex"
            [class.border-amber-400]="d.isToday"
            [class.border-surface-200/70]="!d.isToday"
            [class.dark:border-surface-200/70]="!d.isToday"
          >
            <button
              type="button"
              (click)="dayClick.emit(d.date)"
              class="w-full h-full flex items-center justify-between gap-3 px-3.5 py-2.5 text-left transition-colors hover:bg-surface-50/50 dark:hover:bg-surface-100/30"
              [class.opacity-60]="d.isDayOff && d.count === 0"
              [attr.aria-label]="
                d.weekday + ' ' + d.dayLabel + ', ' + d.count + ' ' + count(d.count) +
                (d.workLabel ? ', ' + d.workLabel : '') + ' — otwórz widok dnia'
              "
            >
              <span class="min-w-0 flex flex-col gap-1">
                <span class="flex items-baseline gap-2">
                  <span
                    class="text-xs font-black uppercase tracking-wider"
                    [class.text-amber-700]="d.isToday"
                    [class.dark:text-amber-300]="d.isToday"
                    [class.text-surface-800]="!d.isToday"
                  >{{ d.weekday }}</span>
                  <span class="text-xs font-bold tabular-nums text-surface-500 dark:text-surface-400">{{ d.dayLabel }}</span>
                </span>
                @if (d.workLabel) {
                  <span class="flex items-center gap-1.5 text-xs">
                    <i class="{{ d.workIcon }} text-[11px] text-surface-400" aria-hidden="true"></i>
                    <span
                      class="font-semibold"
                      [class.text-surface-600]="!d.isDayOff"
                      [class.dark:text-surface-300]="!d.isDayOff"
                      [class.text-surface-400]="d.isDayOff"
                      [class.dark:text-surface-500]="d.isDayOff"
                    >{{ d.workLabel }}</span>
                  </span>
                }
              </span>

              <span
                class="shrink-0 inline-flex items-center rounded-full px-3 py-1 text-xs font-bold tabular-nums"
                [class.bg-primary/10]="d.count > 0"
                [class.text-primary]="d.count > 0"
                [class.dark:bg-primary/15]="d.count > 0"
                [class.bg-surface-100]="d.count === 0"
                [class.text-surface-400]="d.count === 0"
                [class.dark:bg-surface-100]="d.count === 0"
                [class.dark:text-surface-500]="d.count === 0"
              >
                {{ d.count }} {{ count(d.count) }}
              </span>
            </button>
          </li>
        }
      </ul>
    </div>
  `,
})
export class WeekAgendaComponent {
  readonly anchor = input.required<Date>();
  readonly appointments = input.required<AppointmentPreviewDto[]>();
  readonly filters = input<CalendarFiltersValue>({
    employeeIds: [],
    statuses: [],
    searchQuery: '',
  });
  /** Grafik / dni specjalne / nieobecności oglądanego pracownika — do wyliczenia czasu pracy per dzień. */
  readonly schedules = input<EmployeeScheduleDto[] | undefined>(undefined);
  readonly overrides = input<ScheduleOverrideDto[] | undefined>(undefined);
  readonly leaves = input<EmployeeLeaveDto[] | undefined>(undefined);

  readonly dayClick = output<Date>();
  readonly prev = output<void>();
  readonly next = output<void>();
  readonly today = output<void>();

  protected readonly count = deklinacjaWizyt;

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Wysokość karty tygodnia w px — dobrana tak, by 7 dni + nagłówek wypełniały DOKŁADNIE
   * widoczny obszar scrollowanego `<main>` (od góry karty do jego dolnej krawędzi, tuż nad
   * mobilnym navbarem, który jest w przepływie). Dzięki temu widok tygodnia nigdy się nie
   * scrolluje góra-dół — 7 wierszy rozkłada się przez `flex-1` na tę wysokość.
   * `null` = brak wymuszenia (SSR/testy bez `<main>` albo bardzo niski ekran → naturalna wysokość).
   */
  protected readonly cardHeight = signal<number | null>(null);

  /** Odstęp karty od dolnej krawędzi <main> (żeby nie kleiła się do navbara). */
  private static readonly BOTTOM_GAP_PX = 8;
  /** Poniżej tej wysokości nie wymuszamy dopasowania — pozwalamy na scroll (mikro-ekrany). */
  private static readonly MIN_FIT_PX = 320;

  constructor() {
    afterNextRender(() => {
      const scroll = this.host.nativeElement.closest('main');
      // Bez kontenera scrolla (np. test bez layoutu) nie ma czego mierzyć.
      if (!scroll) return;

      let raf = 0;
      const schedule = (): void => {
        cancelAnimationFrame(raf);
        raf = requestAnimationFrame(() => this.measure(scroll));
      };
      schedule();

      // ResizeObserver na korzeniu strony łapie zmianę wysokości chrome nad kartą
      // (baner „do potwierdzenia", toolbar) oraz zmiany viewportu/orientacji.
      let ro: ResizeObserver | undefined;
      if (typeof ResizeObserver !== 'undefined') {
        ro = new ResizeObserver(schedule);
        ro.observe(scroll);
        const shell = this.host.nativeElement.closest('.admin-page-shell');
        if (shell && shell !== scroll) ro.observe(shell);
      }
      window.addEventListener('resize', schedule, { passive: true });

      this.destroyRef.onDestroy(() => {
        cancelAnimationFrame(raf);
        ro?.disconnect();
        window.removeEventListener('resize', schedule);
      });
    });
  }

  /**
   * Zmierz dostępną wysokość i ustaw `cardHeight`.
   *
   * Mierzymy GEOMETRIĄ (`getBoundingClientRect`), nie `scrollHeight` — bo `scrollHeight` nigdy nie
   * spada poniżej `clientHeight` (gdy treść się mieści, jest do niego dociągany), co przy braku
   * przepełnienia dawało błędną „resztę" i pętlę zwijania/rozwijania.
   *
   *   above = od góry <main> do góry karty (nagłówek mobilny + bannery + toolbar + górny padding)
   *   below = padding shella POD kartą (shell.bottom − host.bottom) — stały, niezależny od wysokości
   *   cardHeight = clientHeight(<main>) − above − below − odstęp
   *
   * Wszystkie trzy składniki są niezależne od wysokości karty (top karty wyznacza chrome nad nią,
   * a `below` to padding kontenera), więc pomiar jest zbieżny: kolejny tick daje tę samą wartość →
   * `signal.set` no-op (Object.is) → koniec przeliczania.
   */
  private measure(scroll: Element): void {
    const cardEl = this.host.nativeElement.firstElementChild as HTMLElement | null;
    const shell = this.host.nativeElement.closest('.admin-page-shell');
    if (!cardEl || !shell) return;
    const main = scroll as HTMLElement;
    const mainRect = main.getBoundingClientRect();
    const cardRect = cardEl.getBoundingClientRect();
    const shellRect = shell.getBoundingClientRect();

    const above = cardRect.top - mainRect.top + main.scrollTop;
    const below = shellRect.bottom - cardRect.bottom;
    const target = Math.floor(
      main.clientHeight - above - below - WeekAgendaComponent.BOTTOM_GAP_PX,
    );
    const next = target >= WeekAgendaComponent.MIN_FIT_PX ? target : null;

    // Histereza: ignoruj różnice ≤1px (zaokrąglenia rect) — inaczej sub-pikselowy flip-flop
    // mógłby podtrzymywać pętlę pomiar→resize→pomiar.
    const current = this.cardHeight();
    if (current !== null && next !== null && Math.abs(next - current) <= 1) return;
    this.cardHeight.set(next);
  }

  readonly days = computed<WeekDay[]>(() => {
    const a = startOfDay(this.anchor());
    const monIdx = (a.getDay() + 6) % 7;
    const start = new Date(a);
    start.setDate(a.getDate() - monIdx);
    const todayDate = startOfDay(new Date());
    const filtered = filterAppointments(this.appointments(), this.filters());

    const schedules = this.schedules();
    const overrides = this.overrides();
    const leaves = this.leaves();
    // Czy w ogóle mamy kontekst grafiku — bez niego nie twierdzimy „Dzień wolny" (byłoby mylące
    // np. w widoku zespołu bez wybranego pracownika), tylko pomijamy linię czasu pracy.
    const hasScheduleContext = !!schedules?.length || !!overrides?.length || !!leaves?.length;

    const out: WeekDay[] = [];
    for (let i = 0; i < 7; i++) {
      const d = new Date(start);
      d.setDate(start.getDate() + i);

      const count = filtered.filter(
        (x) => !!x.date && sameCalendarDay(new Date(x.date as unknown as string), d),
      ).length;

      let workLabel: string | null = null;
      let workIcon = 'pi pi-clock';
      let isDayOff = false;

      if (findBlockingLeaveForDate(leaves, d)) {
        workLabel = 'Nieobecność';
        isDayOff = true;
      } else {
        const raw = resolveRawScheduleDayForDate(schedules, overrides, leaves, d);
        if (raw?.fixedStartTimes.length) {
          // Grafik stały: nie ma okna pracy, są sloty. Pokazujemy ich liczbę + start pierwszego
          // ("5 terminów · od 10:00") — bo ostatnia godzina to START, nie koniec pracy (usługa trwa).
          const n = raw.fixedStartTimes.length;
          const first = trimHm([...raw.fixedStartTimes].sort()[0]);
          workLabel = `${n} ${deklinacjaTerminow(n)} · od ${first}`;
          workIcon = 'pi pi-calendar';
        } else if (raw?.workRanges.length) {
          const starts = raw.workRanges.map((r) => r.startTime ?? '').sort();
          const ends = raw.workRanges.map((r) => r.endTime ?? '').sort();
          workLabel = `${trimHm(starts[0])}–${trimHm(ends[ends.length - 1])}`;
        } else if (hasScheduleContext) {
          workLabel = 'Dzień wolny';
          isDayOff = true;
        }
      }

      out.push({
        date: d,
        dayKey: d.toISOString().slice(0, 10),
        weekday: PL_WEEKDAYS_LONG[d.getDay()] ?? '—',
        dayLabel: `${d.getDate()}.${String(d.getMonth() + 1).padStart(2, '0')}`,
        isToday: sameCalendarDay(d, todayDate),
        count,
        workLabel,
        workIcon,
        isDayOff,
      });
    }
    return out;
  });

  readonly rangeLabel = computed(() => {
    const list = this.days();
    if (list.length === 0) return '';
    const fmt = (d: Date): string =>
      d.toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' });
    return `${fmt(list[0].date)} – ${fmt(list[6].date)}`;
  });
}
