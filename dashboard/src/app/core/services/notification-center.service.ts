import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { AppointmentPreviewDto, CustomerChangedAppointmentDto, API_BASE_URL } from '@core/api/api-client';
import { catchError, of } from 'rxjs';
import { statusVariantFromIdOrName } from '@core/theme/status-tokens';
import { fetchAppointmentsRange, formatDateOnly, SILENT_ERRORS } from '@core/api/appointments-range';
import {
  RealtimeNotificationsService,
  isPersistedNotificationId,
} from '@core/services/realtime-notifications.service';
import { AuthSessionService } from '@core/auth/auth-session.service';
import type { NotificationCategory } from '@core/services/notification-taxonomy';

interface OutsideScheduleItem {
  appointmentId: string;
  employeeId?: string;
  employeeName?: string;
  serviceName?: string;
  date?: string;
  startTime?: string;
  endTime?: string;
}

function silentContext(): HttpContext {
  return new HttpContext().set(SILENT_ERRORS, true);
}

export interface AppNotification {
  id: string;
  /**
   * Kanał źródłowy — steruje logiką (stan „przeczytane", licznik pendingu). NIE myl z
   * `category`, która steruje wyłącznie wyglądem (ikona/kolor/etykieta).
   */
  kind: 'pending' | 'reminder' | 'system' | 'outside-schedule' | 'customer-change';
  /** Kategoria prezentacyjna (patrz `notification-taxonomy.ts`) — rozróżnia typy w UI. */
  category: NotificationCategory;
  /**
   * Id wizyty, której dotyczy powiadomienie (jeśli dotyczy). Klucz sklejania kopii tego
   * samego zdarzenia z różnych kanałów: `category`+`referenceId` (patrz `activityItems`).
   */
  referenceId?: string;
  title: string;
  description: string;
  occurredAt: Date;
  routerLink?: unknown[];
  /** Query params dla nawigacji (np. `{ appointment: id }` → drawer wizyty w kalendarzu). */
  queryParams?: Record<string, string>;
  read: boolean;
}

/**
 * Cel kliknięcia powiadomienia o wizycie. Osobna strona `/admin/appointment/:id` została
 * usunięta — wizytę otwiera drawer w kalendarzu przez deep-link `?appointment=<id>`.
 *
 * Czy kalendarz ma przeskoczyć na dzień wizyty, zależy od intencji użytkownika przy danym kanale:
 *
 * - `reveal: false` (wizyta do potwierdzenia) — personel chce ją POTWIERDZIĆ, nie oglądać grafik.
 *   Przeskok gubiłby dzień, na który patrzył, i po zatwierdzeniu zostawiał go na obcej dacie.
 *   Panel otwiera się nad bieżącym widokiem, kalendarz stoi.
 * - `reveal: true` (poza grafikiem, anulowana/przełożona przez klienta) — tam kontekst grafiku
 *   jest treścią powiadomienia: co jest wokół tej wizyty, jaka dziura została po anulowaniu.
 *
 * `date` i `employeeId` idą w linku, żeby kalendarz wszedł od razu na właściwy dzień i pracownika.
 * Bez nich renderował klatkę „dziś", potem redirect na `/admin/schedule/:employeeId` (przeładowanie
 * komponentu), a dopiero po fetchu skakał na docelową datę — trzy widoczne skoki zamiast zera.
 */
export function appointmentNotificationTarget(
  id: string | null | undefined,
  opts?: { reveal?: boolean; date?: Date | string | null; employeeId?: string | null },
): Pick<AppNotification, 'routerLink' | 'queryParams'> {
  if (!id) return {};

  const employeeId = opts?.employeeId ?? null;
  const routerLink = employeeId ? ['/admin/schedule', employeeId] : ['/admin/schedule'];
  const queryParams: Record<string, string> = { appointment: id };

  // Domyślnie przeskakujemy — deep-linki spoza powiadomień (np. profil klienta) trafiają tu
  // bez kontekstu kalendarza, więc pokazanie dnia wizyty jest tam jedynym sensownym zachowaniem.
  if (opts?.reveal === false) {
    queryParams['reveal'] = '0';
    return { routerLink, queryParams };
  }

  const day = opts?.date ? new Date(opts.date as string) : null;
  if (day && !Number.isNaN(day.getTime())) {
    queryParams['date'] = formatDateOnly(day);
    queryParams['view'] = 'day';
  }
  return { routerLink, queryParams };
}

const STORAGE_KEY = 'zapisz.dashboard.read-notifications';
const POLL_MS = 8000;

/**
 * Centrum powiadomień: poolluje "oczekujące" wizyty (status pending) oraz wizyty
 * "poza grafikiem" (gdy endpoint backendu jest dostępny) co 8s. Stan "przeczytane"
 * przechowywany w localStorage, żeby nie pokazywał się powtarzalnie po refreshu.
 */
@Injectable({ providedIn: 'root' })
export class NotificationCenterService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);
  private readonly realtime = inject(RealtimeNotificationsService);
  private readonly auth = inject(AuthSessionService);

  private readonly _pending = signal<AppNotification[]>([]);
  private readonly _outside = signal<AppNotification[]>([]);
  private readonly _customerChanges = signal<AppNotification[]>([]);
  private readonly _read = signal<Set<string>>(this.readStored());
  private timer: number | null = null;

  /**
   * Nakłada stan „przeczytane": pozycje 'system' niosą serwerowy `read`; pochodne
   * (pending/outside/customer-change) używają lokalnego stanu z localStorage.
   */
  private readonly withRead = (list: readonly AppNotification[]): AppNotification[] =>
    list.map((n) => (n.kind === 'system' ? n : { ...n, read: this._read().has(n.id) }));

  /**
   * Klucz sklejania: kategoria + wizyta. To samo zdarzenie (np. anulowanie przez klienta)
   * potrafi trafić do dzwonka dwoma kanałami naraz — trwałym powiadomieniem in-app ORAZ
   * żywym pollerem stanu wizyty. Oba niosą tę samą wizytę i tę samą kategorię, więc tym
   * kluczem rozpoznajemy, że to jedna i ta sama rzecz. `null` = nie da się skleić.
   */
  private dedupKey(n: AppNotification): string | null {
    return n.referenceId ? `${n.category}:${n.referenceId}` : null;
  }

  /**
   * „Wymaga uwagi" — żywe pozycje z pollerów, na które personel powinien zareagować
   * (do potwierdzenia / anulowane / przełożone / poza grafikiem). Wyświetlane u góry panelu.
   */
  readonly actionItems = computed(() =>
    this.withRead([...this._pending(), ...this._customerChanges(), ...this._outside()]),
  );

  /**
   * „Ostatnie zdarzenia" — trwały strumień powiadomień in-app (real-time, stan serwerowy).
   * Odfiltrowujemy trwały wpis, którego żywy odpowiednik (ta sama kategoria + ta sama wizyta)
   * jest już w „Wymaga uwagi" — inaczej ta sama anulowana/oczekująca wizyta pokazałaby się
   * dwa razy. Wygrywa kopia z pollera (aktualna, „actionable"); log trwały wraca dopiero, gdy
   * żywa akcja zniknie (np. po potwierdzeniu wizyty albo po 48h okna zmian klienta).
   */
  readonly activityItems = computed(() => {
    const covered = new Set(
      this.actionItems()
        .map((n) => this.dedupKey(n))
        .filter((k): k is string => k !== null),
    );
    return this.withRead(this.realtime.persisted()).filter((n) => {
      const key = this.dedupKey(n);
      return key === null || !covered.has(key);
    });
  });

  /** Pełna, płaska lista (akcje najpierw) — zachowana dla licznika i kompatybilności. */
  readonly items = computed(() => [...this.actionItems(), ...this.activityItems()]);

  readonly unreadCount = computed(() => this.items().filter((x) => !x.read).length);

  /**
   * Licznik wizyt "pending" do zatwierdzenia — używany m.in. przez badge na bottom-nav
   * kalendarza (F3.3). Liczy się tylko z pendingu (poolling co 8s), bez 'outside-schedule'
   * i 'customer-change', bo te nie wymagają akcji-zatwierdzenia.
   */
  readonly pendingCount = computed(() => this._pending().length);

  start(): void {
    this.realtime.start();
    if (this.timer != null) return;
    this.poll();
    this.timer = window.setInterval(() => this.poll(), POLL_MS);
    document.addEventListener('visibilitychange', this.onVisibility);
  }

  stop(): void {
    this.realtime.stop();
    if (this.timer != null) {
      window.clearInterval(this.timer);
      this.timer = null;
    }
    document.removeEventListener('visibilitychange', this.onVisibility);
  }

  markAllRead(): void {
    this.realtime.markAllRead();
    const next = new Set(this._read());
    for (const item of this.items()) {
      if (item.kind !== 'system') next.add(item.id);
    }
    this._read.set(next);
    this.persistRead();
  }

  markRead(id: string): void {
    if (isPersistedNotificationId(id)) {
      this.realtime.markRead(id);
      return;
    }
    const next = new Set(this._read());
    next.add(id);
    this._read.set(next);
    this.persistRead();
  }

  private onVisibility = (): void => {
    if (document.visibilityState === 'visible') this.poll();
  };

  private poll(): void {
    if (document.visibilityState === 'hidden') return;
    this.pollPending();
    this.pollOutsideSchedule();
    this.pollCustomerChanges();
  }

  /**
   * Zakres powiadomień = ten sam model, co dzwonek na backendzie: każdy widzi wyłącznie wizyty
   * do siebie przypisane, a konto „Recepcja" (kiosk) — całego salonu (nie ma własnego kalendarza).
   * `undefined` znaczy „nie wiem jeszcze" → poller czeka, zamiast pytać o cudze wizyty
   * (przed hydratacją `currentRole()` = null, a `currentEmployeeId()` jeszcze nie istnieje).
   */
  private pendingScopeEmployeeId(): string | null | undefined {
    if (!this.auth.isHydrated()) return undefined;
    // Recepcja (kiosk) oraz tryb wsparcia (Admin w impersonacji) widzą cały salon — bez filtra
    // po pracowniku. Admin nie ma własnego rekordu Employee, więc osobisty dzwonek byłby pusty.
    if (this.auth.currentRole() === 'kiosk' || this.auth.isImpersonating()) return null;
    return this.auth.currentEmployeeId() ?? undefined;
  }

  private pollPending(): void {
    const scope = this.pendingScopeEmployeeId();
    if (scope === undefined) return;

    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    const end = new Date(start);
    end.setDate(end.getDate() + 7);
    fetchAppointmentsRange(this.http, this.apiBaseUrl, start, end, scope, { silent: true })
      .pipe(catchError(() => of([] as AppointmentPreviewDto[])))
      .subscribe((list) => {
        const next: AppNotification[] = (list ?? [])
          .filter((a) => statusVariantFromIdOrName(a.status?.id, a.status?.name) === 'pending')
          .slice(0, 12)
          .map((a) => ({
            id: a.id ?? `${a.date}-${a.startTime}`,
            kind: 'pending',
            category: 'awaiting-confirmation',
            referenceId: a.id ?? undefined,
            title: 'Wizyta do potwierdzenia',
            description: this.formatDescription(a),
            occurredAt: a.date ? new Date(a.date as unknown as string) : new Date(),
            // Bez przeskoku na dzień wizyty: intencją jest potwierdzenie, nie oglądanie grafiku.
            // `employeeId` mimo to przekazujemy — bez niego link celował w gołą `/admin/schedule`,
            // a ta jest przekierowywana na `/admin/schedule/:employeeId`, co NISZCZY i odtwarza
            // komponent kalendarza (zmierzone na produkcji: 37 requestów w 3 s, ~70% duplikatów).
            // Wpływa to tylko na wejście spoza kalendarza — gdy kalendarz jest już na ekranie,
            // dzwonek omija router przez AppointmentFocusService i nic się nie przestawia.
            ...appointmentNotificationTarget(a.id, { reveal: false, employeeId: a.employeeId }),
            read: false,
          }));
        this._pending.set(next);
      });
  }

  /**
   * Pollery wizyt poza grafikiem — backendowy endpoint dostarcza listę wizyt których
   * `[StartTime,EndTime]` nie mieści się w aktualnym schedule/override/leave dla danego dnia.
   * Wymaga GET /api/notifications/outside-schedule?from=&to=. Gdy endpoint zwróci błąd
   * (np. 404 w starszej wersji backendu), lista pozostaje pusta — bez toastów.
   */
  private pollOutsideSchedule(): void {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    const end = new Date(start);
    end.setDate(end.getDate() + 14);
    const from = formatDateOnly(start);
    const to = formatDateOnly(end);
    const url = `${this.apiBaseUrl}/api/notifications/outside-schedule?from=${from}&to=${to}`;
    this.http
      .get<OutsideScheduleItem[]>(url, {
        context: silentContext(),
      })
      .pipe(catchError(() => of([] as OutsideScheduleItem[])))
      .subscribe((list) => {
        const next: AppNotification[] = (list ?? []).slice(0, 20).map((it) => ({
          id: `outside:${it.appointmentId}`,
          kind: 'outside-schedule',
          category: 'outside-schedule',
          referenceId: it.appointmentId,
          title: 'Wizyta poza godzinami pracy',
          description: this.formatOutsideDescription(it),
          occurredAt: it.date ? new Date(it.date) : new Date(),
          // Z przeskokiem: sens powiadomienia to „zobacz, gdzie ta wizyta wypadła w grafiku".
          ...appointmentNotificationTarget(it.appointmentId, {
            date: it.date,
            employeeId: it.employeeId,
          }),
          read: false,
        }));
        this._outside.set(next);
      });
  }

  private pollCustomerChanges(): void {
    const url = `${this.apiBaseUrl}/api/Notifications/customer-changes`;
    this.http
      .get<CustomerChangedAppointmentDto[]>(url, { context: silentContext() })
      .pipe(catchError(() => of([] as CustomerChangedAppointmentDto[])))
      .subscribe((list) => {
        const next: AppNotification[] = (list ?? []).slice(0, 20).map((it) => ({
          id: `customer-change:${it.appointmentId}`,
          kind: 'customer-change',
          category: it.changeKind === 1 ? 'cancelled' : 'rescheduled',
          referenceId: it.appointmentId,
          title: it.changeKind === 1 ? 'Wizyta anulowana przez klienta' : 'Wizyta przełożona przez klienta',
          description: this.formatCustomerChangeDescription(it),
          occurredAt: it.changedAt ? new Date(it.changedAt) : new Date(),
          // Z przeskokiem: po anulowaniu liczy się dziura w grafiku, po przełożeniu — nowy termin
          // i jego sąsiedztwo. Data + pracownik w linku → kalendarz wchodzi na właściwy dzień
          // od pierwszej klatki, bez redirectu z gołej trasy.
          ...appointmentNotificationTarget(it.appointmentId, {
            date: it.date,
            employeeId: it.employeeId,
          }),
          read: false,
        }));
        this._customerChanges.set(next);
      });
  }

  private formatCustomerChangeDescription(it: CustomerChangedAppointmentDto): string {
    const parts: string[] = [];
    if (it.customerName) parts.push(it.customerName);
    if (it.serviceName) parts.push(it.serviceName);
    if (it.date && it.startTime) {
      const d = new Date(it.date as unknown as string);
      const dl = `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}`;
      parts.push(`${dl} ${String(it.startTime).slice(0, 5)}`);
    }
    return parts.join(' • ');
  }

  private formatOutsideDescription(it: OutsideScheduleItem): string {
    const parts: string[] = [];
    if (it.employeeName) parts.push(it.employeeName);
    if (it.serviceName) parts.push(it.serviceName);
    if (it.date && it.startTime) {
      const d = new Date(it.date);
      const dl = `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}`;
      parts.push(`${dl} ${String(it.startTime).slice(0, 5)}`);
    }
    return parts.join(' • ');
  }

  private formatDescription(a: AppointmentPreviewDto): string {
    const parts: string[] = [];
    if (a.serviceName) parts.push(a.serviceName);
    const customerLine = [a.customerFirstName, a.customerLastName]
      .filter((s): s is string => !!s && s.trim() !== '')
      .join(' ');
    if (customerLine) parts.push(customerLine);
    if (a.startTime) parts.push(String(a.startTime).slice(0, 5));
    return parts.join(' • ');
  }

  private readStored(): Set<string> {
    try {
      const raw = globalThis.localStorage?.getItem(STORAGE_KEY);
      if (!raw) return new Set();
      const parsed = JSON.parse(raw) as unknown;
      if (Array.isArray(parsed)) {
        return new Set(parsed.filter((x): x is string => typeof x === 'string'));
      }
    } catch {
      /* ignore */
    }
    return new Set();
  }

  private persistRead(): void {
    try {
      globalThis.localStorage?.setItem(
        STORAGE_KEY,
        JSON.stringify(Array.from(this._read()).slice(-200)),
      );
    } catch {
      /* ignore */
    }
  }
}
