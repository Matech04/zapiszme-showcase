import {
  DestroyRef,
  effect,
  inject,
  Injectable,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Params, Router } from '@angular/router';
import type { AppointmentStatusVariant } from '@core/theme/status-tokens';
import type { CalendarViewMode } from '../filters/calendar-filters.component';
import { formatYyyyMmDd, startOfDay } from './date-utils';

const VIEW_VALUES: readonly CalendarViewMode[] = ['day', 'week', 'month'];

const STATUS_VALUES: readonly AppointmentStatusVariant[] = [
  'pending',
  'booked',
  'inProgress',
  'completed',
  'canceled',
  'default',
];

const STORAGE_KEY = 'zapisz.calendar.prefs.v1';

interface StoredPrefs {
  view?: CalendarViewMode;
  statuses?: AppointmentStatusVariant[];
  date?: string;
  employees?: string[];
}

function readStoredPrefs(): StoredPrefs {
  try {
    const raw = globalThis.localStorage?.getItem(STORAGE_KEY);
    if (!raw) return {};
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object') return {};
    const out: StoredPrefs = {};
    const v = (parsed as Record<string, unknown>)['view'];
    if (typeof v === 'string' && (VIEW_VALUES as readonly string[]).includes(v)) {
      out.view = v as CalendarViewMode;
    }
    const s = (parsed as Record<string, unknown>)['statuses'];
    if (Array.isArray(s)) {
      out.statuses = s.filter(
        (x): x is AppointmentStatusVariant =>
          typeof x === 'string' && (STATUS_VALUES as readonly string[]).includes(x),
      );
    }
    const d = (parsed as Record<string, unknown>)['date'];
    if (typeof d === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(d)) {
      out.date = d;
    }
    const e = (parsed as Record<string, unknown>)['employees'];
    if (Array.isArray(e)) {
      out.employees = e.filter((x): x is string => typeof x === 'string' && x.length > 0);
    }
    return out;
  } catch {
    // Storage zablokowany / quota — silent: kalendarz dalej działa, tylko bez persystencji.
    return {};
  }
}

function writeStoredPrefs(prefs: StoredPrefs): void {
  try {
    globalThis.localStorage?.setItem(STORAGE_KEY, JSON.stringify(prefs));
  } catch {
    // Quota / private mode — ignorujemy.
  }
}

/**
 * Trzyma stan kalendarza (data, widok, filtry) i synchronizuje go dwukierunkowo z URL-em.
 * Deep-link: `/admin/schedule?date=2026-05-15&view=day&employees=id1,id2&status=pending,booked&q=imie`.
 *
 * Wzajemna pętla URL ↔ stan zerwana dwoma niezależnymi mechanizmami:
 *   1. `setX` porównują wartość — `signal.set` nie odpala efektu gdy nic się nie zmieniło,
 *   2. `Router` z domyślnym `onSameUrlNavigation: 'ignore'` nie emituje `queryParams` dla
 *      identycznego URL-a (więc URL → stan → URL nie wywoła kolejnego stan → URL).
 *
 * Komponent-scoped (`providers: [CalendarStateService]`) — żywotność jak ekran kalendarza.
 */
@Injectable()
export class CalendarStateService {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly date = signal<Date>(startOfDay(new Date()));
  readonly view = signal<CalendarViewMode>('day');
  readonly employees = signal<string[]>([]);
  readonly statuses = signal<AppointmentStatusVariant[]>([]);
  readonly searchQuery = signal<string>('');

  constructor() {
    // 1) localStorage jako fallback dla `view`/`statuses` (F3.5) — przywraca preferencję
    //    między sesjami; URL z deep-linka nadpisuje storage w kroku 2.
    this.applyFromStorage(readStoredPrefs());
    // 2) URL ma priorytet wyższy niż storage — przepuszczamy queryParams po storage.
    this.applyFromUrl(this.route.snapshot.queryParams);
    // 3) Reakcja na zewnętrzne nawigacje (back/forward, ręczne wpisanie URL).
    this.route.queryParams
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.applyFromUrl(params));
    // 4) Zapis URL przy zmianach stanu.
    effect(() => {
      const queryParams = this.buildParams();
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams,
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    });
    // 5) Zapis preferencji do localStorage — przywraca pełny kontekst kalendarza
    //    gdy użytkownik wróci po krótkim odejściu (np. ustawienia → kalendarz).
    //    `searchQuery` celowo pomijamy — stale zapytanie po powrocie byłoby mylące.
    effect(() => {
      writeStoredPrefs({
        view: this.view(),
        statuses: this.statuses(),
        date: formatYyyyMmDd(this.date()),
        employees: this.employees(),
      });
    });
  }

  setDate(d: Date): void {
    const next = startOfDay(d);
    if (next.getTime() !== this.date().getTime()) this.date.set(next);
  }

  setView(v: CalendarViewMode): void {
    if (v !== this.view()) this.view.set(v);
  }

  setEmployees(ids: readonly string[]): void {
    const next = [...new Set(ids.filter((x): x is string => !!x))].sort();
    const cur = this.employees();
    if (next.length === cur.length && next.every((x, i) => x === cur[i])) return;
    this.employees.set(next);
  }

  setStatuses(ss: readonly AppointmentStatusVariant[]): void {
    const next = [...new Set(ss)].sort() as AppointmentStatusVariant[];
    const cur = this.statuses();
    if (next.length === cur.length && next.every((x, i) => x === cur[i])) return;
    this.statuses.set(next);
  }

  setSearchQuery(q: string): void {
    const next = q.trim();
    if (next !== this.searchQuery()) this.searchQuery.set(next);
  }

  private buildParams(): Params {
    return {
      date: formatYyyyMmDd(this.date()),
      view: this.view(),
      // `null` w queryParams + `merge` = usunięcie klucza z URL.
      employees: this.employees().length ? this.employees().join(',') : null,
      status: this.statuses().length ? this.statuses().join(',') : null,
      q: this.searchQuery() || null,
    };
  }

  private applyFromStorage(prefs: StoredPrefs): void {
    if (prefs.view) this.setView(prefs.view);
    if (prefs.statuses) this.setStatuses(prefs.statuses);
    if (prefs.date) {
      const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(prefs.date);
      if (m) {
        const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
        if (!Number.isNaN(d.getTime())) this.setDate(d);
      }
    }
    if (prefs.employees) this.setEmployees(prefs.employees);
  }

  private applyFromUrl(params: Params): void {
    const dateStr = typeof params['date'] === 'string' ? params['date'] : null;
    if (dateStr) {
      const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateStr);
      if (m) {
        const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
        if (!Number.isNaN(d.getTime())) this.setDate(d);
      }
    }

    const view = params['view'];
    if (typeof view === 'string' && (VIEW_VALUES as readonly string[]).includes(view)) {
      this.setView(view as CalendarViewMode);
    }

    const employees = params['employees'];
    if (typeof employees === 'string') {
      this.setEmployees(employees.length ? employees.split(',') : []);
    }

    const status = params['status'];
    if (typeof status === 'string') {
      const parsed = (status.length ? status.split(',') : []).filter(
        (s): s is AppointmentStatusVariant => (STATUS_VALUES as readonly string[]).includes(s),
      );
      this.setStatuses(parsed);
    }

    const q = params['q'];
    if (typeof q === 'string') this.setSearchQuery(q);
  }
}
