import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Popover } from 'primeng/popover';
import {
  NotificationCenterService,
  type AppNotification,
} from '@core/services/notification-center.service';
import { notificationVisual, type NotificationVisual } from '@core/services/notification-taxonomy';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { AppointmentFocusService } from '@core/services/appointment-focus.service';

@Component({
  selector: 'app-notifications-bell',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, Popover],
  template: `
    @if (isSystemAdmin()) {
      <!-- Admin: bez tenanta = endpoint notifications zwraca 403; ukrywamy komponent w całości. -->
    } @else {
    <button
      type="button"
      (click)="popover().toggle($event)"
      class="relative grid size-10 place-items-center rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/85 dark:bg-surface-50/55 text-surface-700 hover:border-primary/45 transition-colors"
      aria-label="Powiadomienia"
    >
      <i class="pi pi-bell text-lg" aria-hidden="true"></i>
      @if (unread() > 0) {
        <span
          class="absolute -top-1 -right-1 min-w-5 h-5 px-1 rounded-full bg-primary text-primary-contrast text-[10px] font-black grid place-items-center border-2 border-white dark:border-surface-50"
          aria-live="polite"
        >
          {{ unread() > 9 ? '9+' : unread() }}
        </span>
      }
    </button>

    <p-popover #pop styleClass="!rounded-3xl !p-0 notif-popover">
      <div class="w-[22rem] max-w-[88vw] flex flex-col">
        <header class="px-5 py-4 flex items-center justify-between border-b border-surface-200/70 dark:border-surface-200/70">
          <div>
            <p class="admin-section-label text-primary">Powiadomienia</p>
            <p class="text-sm font-bold text-surface-900">Centrum aktywności</p>
          </div>
          @if (unread() > 0) {
            <button
              type="button"
              class="text-xs font-bold text-primary hover:underline"
              (click)="markAll()"
            >
              Oznacz przeczytane
            </button>
          }
        </header>
        <div class="max-h-[60vh] overflow-y-auto py-1">
          @if (actionItems().length === 0 && activityItems().length === 0) {
            <div class="px-5 py-10 text-center">
              <i class="pi pi-inbox text-2xl text-surface-400 mb-2 block"></i>
              <p class="text-sm text-surface-500">Brak nowych zdarzeń.</p>
            </div>
          } @else {
            @if (actionItems().length > 0) {
              <p class="px-5 pt-3 pb-1 admin-section-label text-amber-600 dark:text-amber-300">
                Wymaga uwagi
              </p>
              @for (n of actionItems(); track n.id) {
                <ng-container *ngTemplateOutlet="row; context: { $implicit: n }" />
              }
            }
            @if (activityItems().length > 0) {
              <p class="px-5 pt-3 pb-1 admin-section-label text-surface-400 dark:text-surface-500"
                 [ngClass]="actionItems().length > 0 ? 'border-t border-surface-200/70 mt-1' : ''">
                Ostatnie zdarzenia
              </p>
              @for (n of activityItems(); track n.id) {
                <ng-container *ngTemplateOutlet="row; context: { $implicit: n }" />
              }
            }
          }
        </div>
      </div>
    </p-popover>
    }

    <!-- Wiersz powiadomienia — współdzielony przez obie sekcje. Wygląd (ikona/kolor/etykieta)
         wynika z kategorii przez taksonomię, więc typy są rozróżnialne na pierwszy rzut oka. -->
    <ng-template #row let-n>
      @let v = visual(n);
      <button
        type="button"
        class="w-full flex items-start gap-3 px-4 py-3 text-left hover:bg-surface-50/80 dark:hover:bg-surface-100/40 transition-colors border-l-4"
        [class]="n.read ? 'border-transparent' : v.borderClass"
        (click)="open(n)"
      >
        <span
          class="grid size-9 place-items-center rounded-xl shrink-0"
          [class]="v.chipClass"
          aria-hidden="true"
        >
          <i [class]="v.icon + ' text-sm'"></i>
        </span>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span
              class="shrink-0 rounded-full px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide"
              [class]="v.tagClass"
            >
              {{ v.label }}
            </span>
            <time
              class="ml-auto shrink-0 text-[11px] font-semibold tabular-nums text-surface-400 dark:text-surface-500"
              [attr.datetime]="n.occurredAt.toISOString()"
              [title]="formatAbsoluteTime(n.occurredAt)"
            >
              {{ formatRelativeTime(n.occurredAt) }}
            </time>
          </div>
          <p
            class="text-sm leading-tight truncate mt-1"
            [class.font-bold]="!n.read"
            [class.text-surface-900]="!n.read"
            [class.font-semibold]="n.read"
            [class.text-surface-600]="n.read"
            [class.dark:text-surface-400]="n.read"
          >
            {{ n.title }}
          </p>
          <p class="text-xs text-surface-500 dark:text-surface-400 truncate mt-0.5">
            {{ n.description }}
          </p>
        </div>
      </button>
    </ng-template>
  `,
})
export class NotificationsBellComponent implements OnInit, OnDestroy {
  private readonly notifs = inject(NotificationCenterService);
  private readonly router = inject(Router);
  private readonly focus = inject(AppointmentFocusService);
  private readonly auth = inject(AuthSessionService);
  protected readonly popover = viewChild.required<Popover>('pop');

  protected readonly isSystemAdmin = computed(() => this.auth.currentRole() === 'systemAdmin');
  readonly actionItems = this.notifs.actionItems;
  readonly activityItems = this.notifs.activityItems;
  readonly unread = this.notifs.unreadCount;

  protected hasOpened = signal(false);

  /** Wygląd (ikona/kolor/etykieta) dla danego powiadomienia — sterowany przez `category`. */
  protected visual(n: AppNotification): NotificationVisual {
    return notificationVisual(n.category);
  }

  private destroyed = false;

  ngOnInit(): void {
    // Admin (systemAdmin) nie ma tenanta — endpointy notyfikacji wymagają policy StaffManagement
    // (Owner/Manager/Employee). Bez tego guarda dostawalibyśmy spam błędów w logach.
    //
    // KRYTYCZNE: nie wolno czytać roli SYNCHRONICZNIE w ngOnInit. Ten komponent montuje się razem
    // z MainLayout, czyli ZANIM `/api/auth/me` wróci — a `currentRole()` jest wtedy `null`.
    // Warunek `if (isSystemAdmin()) return` widział więc `null !== 'systemAdmin'` i przepuszczał
    // admina platformy: szło negotiate huba SignalR oraz /api/notifications/*, każde kończone
    // 400 `tenant.missing`. Czekamy na zhydratowaną sesję i dopiero wtedy decydujemy.
    this.auth.hydrate().subscribe(() => {
      if (this.destroyed || this.isSystemAdmin()) return;
      this.notifs.start();
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    // Bezwarunkowo — `stop()` jest bezpieczne także wtedy, gdy nigdy nie wystartowaliśmy, a rola
    // przy niszczeniu komponentu może być inna niż przy starcie (albo wciąż nieznana).
    this.notifs.stop();
  }

  markAll(): void {
    this.notifs.markAllRead();
  }

  /**
   * Czas „kiedy przyszło" w formie względnej (pl). Źródłem jest `occurredAt`
   * (= CreatedAtUtc powiadomienia trwałego / czas zdarzenia). Dla pozycji z datą
   * w przyszłości (np. 'pending' niosące datę wizyty) zwraca datę bezwzględną,
   * żeby nie pokazywać mylącego „temu".
   */
  protected formatRelativeTime(date: Date): string {
    const diffSec = Math.round((Date.now() - date.getTime()) / 1000);
    if (diffSec < 0) {
      return date.toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' });
    }
    if (diffSec < 60) return 'przed chwilą';
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin} min temu`;
    const diffH = Math.floor(diffMin / 60);
    if (diffH < 24) return `${diffH} godz. temu`;
    const diffD = Math.floor(diffH / 24);
    if (diffD === 1) return 'wczoraj';
    if (diffD < 7) return `${diffD} dni temu`;
    return date.toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' });
  }

  /** Pełna data+godzina do tooltipa (precyzja na żądanie). */
  protected formatAbsoluteTime(date: Date): string {
    return date.toLocaleString('pl-PL', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  open(n: AppNotification): void {
    this.notifs.markRead(n.id);
    if (!n.routerLink || !Array.isArray(n.routerLink)) return;

    // Kalendarz już na ekranie → otwieramy panel bez routera. Nawigacja kosztowałaby redirect
    // na `/admin/schedule/:employeeId`, który odtwarza komponent (dwa montowania, ~46 requestów
    // i migające URL-e), a dla `reveal=0` i tak nie zmieniamy ani dnia, ani pracownika.
    const appointmentId = n.queryParams?.['appointment'];
    if (appointmentId && this.router.url.startsWith('/admin/schedule')) {
      this.focus.request(appointmentId, n.queryParams?.['reveal'] !== '0');
      this.popover().hide();
      return;
    }

    void this.router.navigate(
      n.routerLink as readonly unknown[] as string[],
      n.queryParams ? { queryParams: n.queryParams } : undefined,
    );
    this.popover().hide();
  }
}
