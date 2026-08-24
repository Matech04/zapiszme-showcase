import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  Renderer2,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Drawer } from 'primeng/drawer';
import { AppointmentPreviewDto } from '@core/api/api-client';
import { BodyScrollLockService } from '@core/services/body-scroll-lock.service';
import {
  APPOINTMENT_STATUS_TOKENS,
  AppointmentStatusVariant,
  statusVariantFromIdOrName,
} from '@core/theme/status-tokens';
import {
  DayAvailability,
  leaveBadgeClass,
  leaveLabel,
  leaveStatusLabel,
  overrideBadgeClass,
} from './day-availability';

/** Opis dostępności dnia gotowy do wyświetlenia w sheecie (badge + podpis). */
interface AvailDescriptor {
  text: string;
  sub: string;
  cls: string;
}

interface MonthDayItem {
  raw: AppointmentPreviewDto;
  variant: AppointmentStatusVariant;
  startTime: string;
  endTime: string;
  serviceName: string;
  customerLine: string;
}

/**
 * Bottom sheet (mobile) / right drawer (desktop) z listą wizyt wybranego dnia z widoku miesiąca.
 * Tap w kafelek miesiąca otwiera ten sheet zamiast bezpośrednio przeskakiwać do widoku dnia —
 * użytkownik widzi listę wizyt, tap-em wybiera konkretną wizytę (→ szczegóły), albo przyciskiem
 * przechodzi do widoku dnia (kalendarz timeline) lub dodaje nową wizytę.
 */
@Component({
  selector: 'app-month-day-sheet',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, Drawer],
  template: `
    <p-drawer
      [visible]="isVisible()"
      (visibleChange)="onVisibleChange($event)"
      [position]="position()"
      [modal]="false"
      [showCloseIcon]="false"
      [dismissible]="false"
      [styleClass]="drawerClass()"
      [ariaCloseLabel]="'Zamknij listę dnia'"
    >
      @if (date(); as d) {
        <header class="px-5 pt-5 pb-3 border-b border-surface-200/70 dark:border-surface-200/70">
          @if (!isDesktop()) {
            <div class="flex justify-center pb-2 -mt-2" aria-hidden="true">
              <span class="block w-12 h-1.5 rounded-full bg-surface-300 dark:bg-surface-600"></span>
            </div>
          }
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <p class="admin-section-label text-primary">Dzień</p>
              <h2 class="text-xl font-black text-surface-900 truncate">
                {{ dateLabel(d) }}
              </h2>
              <p class="text-xs text-surface-500 dark:text-surface-400 mt-0.5">
                {{ summaryLabel() }}
              </p>
            </div>
            <button
              type="button"
              (click)="closeRequested.emit()"
              aria-label="Zamknij"
              class="w-8 h-8 rounded-full grid place-items-center text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors shrink-0"
            >
              <i class="pi pi-times text-sm" aria-hidden="true"></i>
            </button>
          </div>
        </header>

        <section class="px-5 py-4 flex-1 overflow-y-auto">
          @if (availDescriptor(); as av) {
            <div class="mb-4 rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-surface-50/60 dark:bg-surface-50/30 px-3 py-2.5 flex items-center gap-2 flex-wrap">
              <span class="admin-section-label text-surface-500 dark:text-surface-400 mr-1">Dostępność</span>
              <span
                class="inline-flex items-center px-2 py-0.5 rounded text-xs font-bold border"
                [ngClass]="av.cls"
              >
                {{ av.text }}
              </span>
              @if (av.sub) {
                <span class="text-xs text-surface-500 dark:text-surface-400">{{ av.sub }}</span>
              }
            </div>
          }
          @if (items().length === 0) {
            <div class="rounded-2xl border border-dashed border-surface-300 dark:border-surface-600 p-6 text-center">
              <i class="pi pi-calendar text-2xl text-surface-400 mb-2 block" aria-hidden="true"></i>
              <p class="text-sm text-surface-600 dark:text-surface-300">Brak wizyt tego dnia.</p>
            </div>
          } @else {
            <ul class="flex flex-col gap-2">
              @for (item of items(); track item.raw.id) {
                <li>
                  <button
                    type="button"
                    (click)="onPick(item.raw)"
                    class="w-full rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/70 dark:bg-surface-50/40 hover:border-primary/45 px-3 py-2.5 text-left flex items-start gap-3 transition-colors"
                  >
                    <span
                      class="shrink-0 inline-flex items-center justify-center w-10 h-10 rounded-xl text-[10px] font-bold leading-none font-mono"
                      [class]="surfaceFor(item.variant)"
                    >
                      {{ shortTime(item.startTime) }}
                    </span>
                    <span class="flex-1 min-w-0">
                      <span class="block text-sm font-black text-surface-900 truncate">
                        {{ item.serviceName }}
                      </span>
                      <span class="block text-xs text-surface-600 dark:text-surface-300 truncate">
                        {{ item.customerLine }}
                      </span>
                      <span class="block text-[11px] text-surface-500 dark:text-surface-400 mt-0.5 font-mono">
                        {{ shortTime(item.startTime) }} – {{ shortTime(item.endTime) }}
                      </span>
                    </span>
                    <span
                      class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-full border border-current/25 shrink-0 self-center"
                      [class]="chipFor(item.variant)"
                    >
                      {{ statusLabelOf(item.variant) }}
                    </span>
                  </button>
                </li>
              }
            </ul>
          }
        </section>

        <footer
          class="px-5 py-4 border-t border-surface-200/70 dark:border-surface-200/70 flex flex-col gap-2 pb-[calc(env(safe-area-inset-bottom)+1rem)] sm:pb-4"
        >
          <button
            type="button"
            (click)="openDayView.emit(d)"
            class="w-full rounded-xl bg-surface-900 dark:bg-surface-100 text-surface-0 dark:text-surface-900 font-bold py-2.5 px-4 transition-opacity hover:opacity-90"
          >
            <i class="pi pi-calendar mr-2" aria-hidden="true"></i>
            Otwórz widok dnia
          </button>
          @if (canCreate()) {
            <button
              type="button"
              (click)="addAppointment.emit(d)"
              class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors"
            >
              <i class="pi pi-plus mr-2" aria-hidden="true"></i>
              Dodaj wizytę
            </button>
          }

          @if (canEditAvailability()) {
            <div class="mt-1 pt-3 border-t border-surface-200/70 dark:border-surface-200/70 flex flex-col gap-2">
              <p class="admin-section-label text-surface-500 dark:text-surface-400">Dostępność</p>
              <button
                type="button"
                (click)="setDayHours.emit(d)"
                class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-sky-500/50 font-semibold py-2.5 px-4 text-left text-sm text-surface-700 transition-colors flex items-center gap-3"
              >
                <i class="pi pi-star text-violet-500" aria-hidden="true"></i>
                Ustaw godziny na ten dzień
              </button>
              <button
                type="button"
                (click)="addLeave.emit(d)"
                class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-amber-500/50 font-semibold py-2.5 px-4 text-left text-sm text-surface-700 transition-colors flex items-center gap-3"
              >
                <i class="pi pi-calendar-minus text-amber-500" aria-hidden="true"></i>
                Dodaj urlop / chorobowe
              </button>
            </div>
          }
        </footer>
      }
    </p-drawer>
  `,
})
export class MonthDaySheetComponent {
  /** Data otwartego dnia. `null` → sheet jest niewidoczny. */
  readonly date = input<Date | null>(null);
  /** Wizyty dnia (już przefiltrowane przez filtry kalendarza w parent-u). */
  readonly appointments = input<AppointmentPreviewDto[]>([]);
  readonly isDesktop = input<boolean>(false);
  /** Czy pokazywać przycisk „Dodaj wizytę" — odpowiada uprawnieniom roli. */
  readonly canCreate = input<boolean>(true);
  /** Dostępność dnia (grafik/urlop/dzień specjalny) — `null` gdy brak jednego pracownika w widoku. */
  readonly availability = input<DayAvailability | null>(null);
  /** Czy pokazywać akcje edycji dostępności (urlop / godziny dnia) — self lub zarządzanie personelem. */
  readonly canEditAvailability = input<boolean>(false);

  readonly closeRequested = output<void>();
  readonly openDayView = output<Date>();
  readonly addAppointment = output<Date>();
  readonly appointmentSelected = output<AppointmentPreviewDto>();
  readonly addLeave = output<Date>();
  readonly setDayHours = output<Date>();

  private readonly renderer = inject(Renderer2);
  private readonly destroyRef = inject(DestroyRef);
  private readonly scrollLock = inject(BodyScrollLockService);

  protected readonly isVisible = computed(() => this.date() != null);

  private clickOutsideUnsubscribe?: () => void;
  private bodyLocked = false;

  constructor() {
    effect(() => {
      if (this.isVisible()) {
        this.lockBodyScroll();
        const t = setTimeout(() => this.bindClickOutside(), 0);
        this.destroyRef.onDestroy(() => clearTimeout(t));
      } else {
        this.unbindClickOutside();
        this.unlockBodyScroll();
      }
    });

    this.destroyRef.onDestroy(() => {
      this.unbindClickOutside();
      this.unlockBodyScroll();
    });
  }

  protected readonly position = computed<'bottom' | 'right'>(() =>
    this.isDesktop() ? 'right' : 'bottom',
  );

  protected readonly drawerClass = computed(() => {
    // `admin-drawer-shell` — bez tego markera `.p-drawer-content` zostaje `display:block`
    // (PrimeNG v21 nie ma `contentStyleClass`), `flex-1` na sekcji jest martwe, a stopka
    // z akcjami dnia pływa za listą wizyt zamiast stać na dole.
    const base = 'admin-drawer-shell !bg-surface-0 dark:!bg-surface-50 !border-l-0 !border-t-0';
    return this.isDesktop()
      ? `${base} !w-[420px] !max-w-[92vw] !border-l !border-surface-200 dark:!border-surface-200`
      : `${base} !h-auto !max-h-[80vh] !rounded-t-3xl shadow-2xl`;
  });

  protected readonly items = computed<MonthDayItem[]>(() => {
    const list = this.appointments();
    return list
      .map<MonthDayItem>((raw) => ({
        raw,
        variant: statusVariantFromIdOrName(raw.status?.id, raw.status?.name),
        startTime: raw.startTime ?? '',
        endTime: raw.endTime ?? '',
        serviceName: this.resolveServiceName(raw),
        customerLine: this.resolveCustomerLine(raw),
      }))
      .sort((a, b) => a.startTime.localeCompare(b.startTime));
  });

  /** Opis dostępności dnia (badge + podpis) na podstawie {@link availability}. */
  protected readonly availDescriptor = computed<AvailDescriptor | null>(() => {
    const av = this.availability();
    if (!av) return null;
    if (av.leave) {
      return {
        text: leaveLabel(av.leave.type),
        sub: leaveStatusLabel(av.leave.status),
        cls: leaveBadgeClass(av.leave.type, av.leave.status),
      };
    }
    if (av.override) {
      return {
        text: av.override.isDayOff ? 'Dzień wolny (wyjątek)' : `Godziny dnia: ${av.override.summary}`,
        sub: '',
        cls: overrideBadgeClass(av.override.isDayOff),
      };
    }
    if (av.status === 'work') {
      const t = av.scheduleSummary ?? (av.fixedTimes?.join(', ') ?? '');
      return {
        text: t ? `Praca: ${t}` : 'Praca',
        sub: '',
        cls: 'bg-emerald-500/12 text-emerald-800 dark:text-emerald-200 border-emerald-500/35',
      };
    }
    return { text: 'Wolne', sub: '', cls: 'bg-surface-200/50 text-surface-600 border-surface-300/60' };
  });

  protected readonly summaryLabel = computed(() => {
    const n = this.items().length;
    const pending = this.items().filter((i) => i.variant === 'pending').length;
    if (n === 0) return 'Brak wizyt';
    const base = `${n} ${this.declension(n)}`;
    return pending > 0 ? `${base} • ${pending} do zatwierdzenia` : base;
  });

  protected readonly dateLabel = (d: Date): string =>
    d.toLocaleDateString('pl-PL', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });

  protected readonly shortTime = (t: string): string => (t ?? '').substring(0, 5);

  protected surfaceFor(variant: AppointmentStatusVariant): string {
    const tokens = APPOINTMENT_STATUS_TOKENS[variant];
    return `${tokens.surfaceClasses} ${tokens.textClasses}`;
  }

  protected chipFor(variant: AppointmentStatusVariant): string {
    return APPOINTMENT_STATUS_TOKENS[variant].textClasses;
  }

  protected statusLabelOf(variant: AppointmentStatusVariant): string {
    return APPOINTMENT_STATUS_TOKENS[variant].label;
  }

  protected onPick(raw: AppointmentPreviewDto): void {
    this.appointmentSelected.emit(raw);
  }

  protected onVisibleChange(next: boolean): void {
    if (!next && this.date() != null) {
      this.closeRequested.emit();
    }
  }

  private lockBodyScroll(): void {
    if (this.bodyLocked) return;
    this.bodyLocked = true;
    this.scrollLock.lock();
  }

  private unlockBodyScroll(): void {
    if (!this.bodyLocked) return;
    this.bodyLocked = false;
    this.scrollLock.unlock();
  }

  private bindClickOutside(): void {
    if (this.clickOutsideUnsubscribe) return;
    this.clickOutsideUnsubscribe = this.renderer.listen(
      'document',
      'pointerdown',
      (ev: Event) => {
        if (!this.isVisible()) return;
        const target = ev.target as HTMLElement | null;
        if (!target) return;
        if (this.isInsideOverlay(target)) return;
        this.closeRequested.emit();
      },
    );
  }

  private unbindClickOutside(): void {
    this.clickOutsideUnsubscribe?.();
    this.clickOutsideUnsubscribe = undefined;
  }

  private isInsideOverlay(target: HTMLElement): boolean {
    return !!(
      target.closest('.p-drawer') ||
      target.closest('.p-dialog') ||
      target.closest('.p-confirm-dialog') ||
      target.closest('.p-overlay-mask') ||
      target.closest('.p-toast') ||
      target.closest('.p-tooltip')
    );
  }

  private resolveServiceName(p: AppointmentPreviewDto): string {
    const x = p as unknown as Record<string, unknown>;
    const n = p.serviceName ?? x['ServiceName'];
    return typeof n === 'string' && n.trim() !== '' ? n.trim() : 'Wizyta';
  }

  private resolveCustomerLine(p: AppointmentPreviewDto): string {
    if (p.isGuest === true) return 'Gość';
    const fn = (p.customerFirstName ?? '') as string;
    const ln = (p.customerLastName ?? '') as string;
    const name = [fn, ln].map((s) => s.trim()).filter(Boolean).join(' ');
    if (name) return name;
    return p.customerPhoneNumber?.trim() || '—';
  }

  private declension(n: number): string {
    if (n === 1) return 'wizyta';
    const mod10 = n % 10;
    const mod100 = n % 100;
    if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14)) return 'wizyty';
    return 'wizyt';
  }
}
