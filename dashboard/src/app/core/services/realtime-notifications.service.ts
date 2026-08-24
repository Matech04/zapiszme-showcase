import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { API_BASE_URL } from '@core/api/api-client';
import { SILENT_ERRORS } from '@core/api/appointments-range';
import { catchError, of } from 'rxjs';
import type { HubConnection } from '@microsoft/signalr';
import { appointmentNotificationTarget, type AppNotification } from './notification-center.service';
import { categoryFromNotificationType } from './notification-taxonomy';

/** Kontrakt zgodny z RealtimeNotificationDto / NotificationDto z backendu. */
interface NotificationApiDto {
  id: string;
  type: number;
  subject: string;
  shortText: string;
  referenceId?: string | null;
  createdAtUtc: string;
  read: boolean;
}

const PERSISTED_PREFIX = 'persisted:';
const FALLBACK_POLL_MS = 15000;

function silentContext(): HttpContext {
  return new HttpContext().set(SILENT_ERRORS, true);
}

function toNotification(dto: NotificationApiDto): AppNotification {
  const category = categoryFromNotificationType(dto.type);
  return {
    id: PERSISTED_PREFIX + dto.id,
    kind: 'system',
    // `type` (NotificationType) był dotąd ignorowany — wszystkie trwałe powiadomienia
    // wyglądały tak samo. Teraz mapujemy go na kategorię prezentacyjną.
    category,
    // referenceId = id wizyty — pozwala skleić trwały wpis z żywym pollerem tej samej wizyty.
    referenceId: dto.referenceId ?? undefined,
    title: dto.subject,
    description: dto.shortText,
    occurredAt: dto.createdAtUtc ? new Date(dto.createdAtUtc) : new Date(),
    // Ta sama reguła co w pollerach: „do potwierdzenia" nie rusza kalendarza, reszta pokazuje
    // wizytę w grafiku. NotificationDto nie niesie daty ani pracownika, więc dla pozostałych
    // kategorii dzień ustawia dopiero deep-link po fetchu (jedno przejście, nie trzy).
    ...appointmentNotificationTarget(dto.referenceId, {
      reveal: category !== 'awaiting-confirmation',
    }),
    read: dto.read,
  };
}

/**
 * Powiadomienia in-app w czasie rzeczywistym: ładuje początkową listę z GET /api/notifications,
 * a następnie odbiera pushe SignalR z huba /hubs/notifications. Gdy socket padnie — fallback
 * polling co 15 s; po reconnect re-fetch listy (domyka lukę missed-events). Stan "przeczytane"
 * jest serwerowy (mark-read endpoints).
 */
@Injectable({ providedIn: 'root' })
export class RealtimeNotificationsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  private readonly _persisted = signal<AppNotification[]>([]);
  readonly persisted = this._persisted.asReadonly();

  private connection: HubConnection | null = null;
  private fallbackTimer: number | null = null;
  private started = false;

  start(): void {
    if (this.started) return;
    this.started = true;
    this.loadInitial();
    void this.connect();
  }

  stop(): void {
    this.started = false;
    this.clearFallback();
    void this.connection?.stop();
    this.connection = null;
  }

  /** id w formacie "persisted:{guid}" — przychodzi z AppNotification.id. */
  markRead(notificationId: string): void {
    const rawId = stripPrefix(notificationId);
    this._persisted.update((list) =>
      list.map((n) => (n.id === notificationId ? { ...n, read: true } : n)),
    );
    this.http
      .post(`${this.apiBaseUrl}/api/notifications/${rawId}/read`, null, { context: silentContext() })
      .pipe(catchError(() => of(null)))
      .subscribe();
  }

  markAllRead(): void {
    this._persisted.update((list) => list.map((n) => ({ ...n, read: true })));
    this.http
      .post(`${this.apiBaseUrl}/api/notifications/read-all`, null, { context: silentContext() })
      .pipe(catchError(() => of(null)))
      .subscribe();
  }

  private loadInitial(): void {
    this.http
      .get<NotificationApiDto[]>(`${this.apiBaseUrl}/api/notifications`, { context: silentContext() })
      .pipe(catchError(() => of([] as NotificationApiDto[])))
      .subscribe((list) => this._persisted.set((list ?? []).map(toNotification)));
  }

  private async connect(): Promise<void> {
    // Dynamiczny import — @microsoft/signalr trafia do osobnego chunku, nie do initial bundle.
    const { HubConnectionBuilder } = await import('@microsoft/signalr');
    if (!this.started) return;

    const conn = new HubConnectionBuilder()
      .withUrl(`${this.apiBaseUrl}/hubs/notifications`, { withCredentials: true })
      .withAutomaticReconnect()
      .build();

    conn.on('ReceiveNotification', (dto: NotificationApiDto) => {
      const item = toNotification(dto);
      this._persisted.update((list) =>
        list.some((n) => n.id === item.id) ? list : [item, ...list],
      );
    });

    conn.onreconnected(() => {
      this.clearFallback();
      this.loadInitial();
    });
    conn.onclose(() => {
      if (this.started) this.startFallback();
    });

    this.connection = conn;

    try {
      await conn.start();
      this.clearFallback();
    } catch {
      this.startFallback();
    }
  }

  private startFallback(): void {
    if (this.fallbackTimer != null) return;
    this.fallbackTimer = window.setInterval(() => this.loadInitial(), FALLBACK_POLL_MS);
  }

  private clearFallback(): void {
    if (this.fallbackTimer != null) {
      window.clearInterval(this.fallbackTimer);
      this.fallbackTimer = null;
    }
  }
}

function stripPrefix(id: string): string {
  return id.startsWith(PERSISTED_PREFIX) ? id.slice(PERSISTED_PREFIX.length) : id;
}

/** Czy dany AppNotification.id pochodzi z trwałego (real-time) źródła. */
export function isPersistedNotificationId(id: string): boolean {
  return id.startsWith(PERSISTED_PREFIX);
}
