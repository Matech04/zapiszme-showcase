import { Component, computed, effect, ElementRef, HostListener, inject, signal, untracked, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { map, catchError } from 'rxjs/operators';
import { of, forkJoin } from 'rxjs';
import { DatePicker } from 'primeng/datepicker';
import { Tooltip } from 'primeng/tooltip';
import {
  API_BASE_URL,
  AppointmentDto,
  AppointmentPreviewDto,
  AppointmentsClient,
  CustomerVerificationChannel,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  EmployeesClient,
  SalonSettingsClient,
  MonthPublicationDto,
  ScheduleOverrideDto,
  SlotGenerationMode,
  StaffCalendarVisibilityPolicy,
  TimeRangeDto,
} from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { StartHereCardComponent } from '@apps/owner-panel/components/start-here-card.component';
import { GuideLauncherComponent } from '@shared/ui/guide-launcher/guide-launcher.component';
import { AppointmentFocusService } from '@core/services/appointment-focus.service';
import { ConfirmationService, MessageService } from 'primeng/api';
import { AppointmentStatusVariant as AppointmentStatusVariantToken } from '@core/theme/status-tokens';
import {
  CalendarFiltersComponent,
  CalendarFiltersValue,
  CalendarViewMode,
} from './filters/calendar-filters.component';
import { EmployeeStripComponent } from './shared/employee-strip.component';
import { AppointmentDetailSheetComponent } from './shared/appointment-detail-sheet.component';
import { CalendarStateService } from './shared/calendar-state.service';
import { LastScheduleEmployeeStore } from '@domains/appointments/data-access/last-schedule-employee.store';
import { CreateAppointmentDrawerComponent, CreateAppointmentContext } from './shared/create-appointment-drawer.component';
import { MonthDaySheetComponent } from './shared/month-day-sheet.component';
import { DayAvailability, resolveDayAvailability } from './shared/day-availability';
import { EmployeeLeaveFormDrawerComponent } from '@domains/employees/feature/availability/leave-dashboard/employee-leave-form-drawer.component';
import { MonthBookingStatusComponent } from './shared/month-booking-status.component';
import { MonthPublicationDrawerComponent } from './shared/month-publication-drawer.component';
import { EmployeeSpecialDayDrawerComponent } from '@domains/employees/feature/availability/employee-special-day-drawer.component';
import { RescheduleAppointmentDialogComponent } from './shared/reschedule-appointment-dialog.component';
import { ChangeServiceDialogComponent } from './shared/change-service-dialog.component';
import { SwapAppointmentsDialogComponent } from './shared/swap-appointments-dialog.component';
import {
  appointmentsRequestKey,
  formatYyyyMmDd,
  sameCalendarDay,
  startOfDay,
  startOfMonth,
} from './shared/date-utils';
import { filterAppointments, statusVariantFromPreview } from './shared/filters';
import {
  canAddGridBreak,
  findBlockingLeaveForDate,
  isAppointmentOutsideWorkingHours,
  pickOverrideForDate,
  pickScheduleForDate,
  resolveBreaksForDate,
  resolveFixedStartTimesForDate,
  resolveRawScheduleDayForDate,
  resolveWorkingRangesForDate,
} from './shared/schedule-resolution';
import {
  BreakEditorDrawerComponent,
  BreakEditorContext,
} from './shared/break-editor-drawer.component';
import { AgendaTileComponent } from './shared/agenda-tile.component';
import { QuickAddSheetComponent } from './shared/quick-add-sheet.component';
import { MonthViewComponent, type MonthStaticSlots } from './views/month-view.component';
import { WeekAgendaComponent } from './views/week-agenda.component';
import { WeekViewComponent } from './views/week-view.component';

const PL_WEEKDAYS = ['NDZ', 'PON', 'WT', 'ŚR', 'CZW', 'PT', 'SOB'] as const;

/** Sentinel zakresu banera „do potwierdzenia" dla konta Recepcji — cały salon, bez `employeeId`. */
const DESK_PENDING_SCOPE = '__desk__';

/** Segment „przerwy" na osi dnia (luka między pasami pracy). `breakRange` ≠ null → usuwalna przerwa. */
interface BreakSegment {
  startMin: number;
  endMin: number;
  breakRange: TimeRangeDto | null;
}

function parseTimeToMinutes(t: string | undefined): number {
  if (!t) return 0;
  const parts = t.split(':').map((p) => Number(p));
  const h = parts[0] ?? 0;
  const m = parts[1] ?? 0;
  const s = parts[2] ? parts[2] / 60 : 0;
  return Math.round(h * 60 + m + s);
}

function formatHm(totalMinutes: number): string {
  const h = Math.floor(totalMinutes / 60);
  const m = totalMinutes % 60;
  return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
}

/**
 * Przydziela nakładającym się wizytom kolumny (lane) w ramach klastra kolizji, żeby na osi
 * czasu nie nachodziły na siebie. Greedy interval-partitioning: w obrębie spójnego klastra
 * (wizyty stykające się czasowo) każda dostaje najniższą wolną kolumnę; `laneCount` = liczba
 * kolumn w klastrze, używana do wyliczenia szerokości/offsetu kafelka. Wizyty bez kolizji
 * dostają `lane=0, laneCount=1` (pełna szerokość).
 */
function assignTimelineLanes<T extends { startMin: number; endMin: number }>(
  items: T[],
): Array<T & { lane: number; laneCount: number }> {
  const sorted = [...items].sort((a, b) => a.startMin - b.startMin || a.endMin - b.endMin);
  const out: Array<T & { lane: number; laneCount: number }> = [];
  let cluster: Array<T & { lane: number; laneCount: number }> = [];
  let clusterEnd = Number.NEGATIVE_INFINITY;
  const flush = (): void => {
    const laneEnds: number[] = [];
    for (const it of cluster) {
      let lane = laneEnds.findIndex((end) => end <= it.startMin);
      if (lane === -1) {
        lane = laneEnds.length;
        laneEnds.push(it.endMin);
      } else {
        laneEnds[lane] = it.endMin;
      }
      it.lane = lane;
    }
    const laneCount = Math.max(1, laneEnds.length);
    for (const it of cluster) it.laneCount = laneCount;
    out.push(...cluster);
    cluster = [];
  };
  for (const raw of sorted) {
    const it = { ...raw, lane: 0, laneCount: 1 } as T & { lane: number; laneCount: number };
    if (cluster.length && it.startMin >= clusterEnd) {
      flush();
      clusterEnd = Number.NEGATIVE_INFINITY;
    }
    cluster.push(it);
    clusterEnd = Math.max(clusterEnd, it.endMin);
  }
  flush();
  return out;
}

type AppointmentStatusVariant = AppointmentStatusVariantToken;

function appointmentDay(d: Date | string | undefined): Date | null {
  if (d == null) return null;
  if (typeof d === 'string') {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(d.trim());
    if (m) {
      return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
    }
  }
  const x = typeof d === 'string' ? new Date(d) : d;
  if (Number.isNaN(x.getTime())) return null;
  return new Date(x.getFullYear(), x.getMonth(), x.getDate());
}

@Component({
  selector: 'app-visit-schedule',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    DatePicker,
    Tooltip,
    AppointmentDetailSheetComponent,
    CreateAppointmentDrawerComponent,
    BreakEditorDrawerComponent,
    AgendaTileComponent,
    QuickAddSheetComponent,
    MonthDaySheetComponent,
    EmployeeLeaveFormDrawerComponent,
    EmployeeSpecialDayDrawerComponent,
    MonthBookingStatusComponent,
    MonthPublicationDrawerComponent,
    RescheduleAppointmentDialogComponent,
    ChangeServiceDialogComponent,
    SwapAppointmentsDialogComponent,
    CalendarFiltersComponent,
    EmployeeStripComponent,
    MonthViewComponent,
    WeekAgendaComponent,
    WeekViewComponent,
    StartHereCardComponent,
    GuideLauncherComponent,
  ],
  providers: [CalendarStateService],
  template: `
    <div class="admin-page-shell" [class.admin-page-pad-for-bottom-nav]="!isMobileWeek()">
      <div
        class="mx-auto w-full"
        [ngClass]="!showDesktopColumns() ? 'max-w-2xl' : isSingleEmployee() ? 'max-w-[72rem]' : 'max-w-[1800px]'"
      >
        <!-- Split layout desktop: lewa kolumna = kalendarz + filtry + KPI; prawa = sticky panel detail (jeśli SOLO/single-employee).
             Na multi-employee zostawiamy single-column bo widok kolumn pracowników już zużywa szerokość. -->
        <div [ngClass]="enableSplitDetailLayout() ? 'lg:grid lg:grid-cols-[minmax(0,1fr)_360px] lg:gap-5 lg:items-start' : ''">
          <div class="min-w-0">
        <!--
          Kontekstowe wejście w przewodniki tego ekranu. Kalendarz jako jedyny ekran z przewodnikiem
          go nie miał, mimo że „Dodajmy wizytę ręcznie" deklaruje entryRoute /admin/schedule/:me
          i trasa dopasowywała się poprawnie — brakowało samego osadzenia. Bez tego jedynym
          wejściem była karta „Zacznij tutaj", która znika po przejściu pozycji, więc obeznany
          salon zostawał na swoim głównym ekranie bez dostępu do pomocy.
          Launcher sam się nie renderuje, gdy dla trasy i roli nie ma żadnego przewodnika.
        -->
        <div class="flex justify-end mb-2">
          <app-guide-launcher />
        </div>

        <!-- Onboarding nad kalendarzem: link do rezerwacji (do pierwszej wizyty) i przewodniki
             dobrane do wyborów z kreatora. Karta znika, gdy nie ma czego pokazać, więc dla
             obeznanego salonu ta sekcja jest pusta. hasTeam podajemy z listy pracowników, którą
             kalendarz i tak pobiera — karta nie może dokładać własnego zapytania. Rola:
             konfiguracja salonu to nie sprawa pracownika ani recepcji. -->
        <div class="grid gap-4 mb-4">
          @if (isOwnerOrManager()) {
            <app-start-here-card [hasTeam]="hasTeamForGuides()" />
          }
        </div>

        <!-- Mobile: bez osobnej karty tytułu i bez liczników dnia. Dla pracownika pokazujemy tylko
             godziny zmiany; owner nie ma tu nic („Kalendarz" jest w górnym pasku aplikacji). -->
        @if (isEmployeeScoped() && employeeShiftLabel(); as shift) {
          <div class="sm:hidden mb-3">
            <span class="inline-flex items-center rounded-full bg-amber-50 dark:bg-amber-950/30 border border-amber-200/70 dark:border-amber-800/40 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wide text-amber-700 dark:text-amber-300">
              {{ shift }}
            </span>
          </div>
        }

        <!-- Nagłówek „Twoja zmiana" — tylko dla widoku pracownika (sm+). Owner/manager/kiosk nie
             mają tu panelu tytułu („Kalendarz" jest w górnym pasku aplikacji). -->
        @if (isEmployeeScoped()) {
          <header class="admin-glass-card rounded-4xl p-4 sm:p-5 mb-5 hidden sm:block">
            <div class="flex-1 lg:flex-none min-w-0">
              <p class="admin-section-label text-primary text-center lg:text-left">
                Twoja zmiana
              </p>
              <h1 class="text-xl sm:text-2xl font-black text-surface-900 tracking-tight text-center lg:text-left">
                {{ employeeBannerTitle() }}
              </h1>
              @if (employeeShiftLabel(); as shift) {
                <p class="text-xs text-surface-500 dark:text-surface-400 mt-1 text-center lg:text-left">
                  {{ shift }}
                </p>
              }
            </div>
          </header>
        }

        <!-- Baner: rezerwacje oczekujące na potwierdzenie (najbliższe 7 dni) — jednoliniowy.
             Na samym szczycie, bo wymaga akcji. Widzi go KAŻDY, kto ma własną wizytę do
             potwierdzenia (recepcja: cały salon). -->
        @if (pendingCount() > 0) {
          <!-- Akcent marki = primary (amber w light, fiolet w dark) — NIE zaszyty amber, bo w
               dark mode baner odcinał się pomarańczem od reszty panelu. -->
          <button
            type="button"
            (click)="goToPending()"
            class="w-full text-left mb-3 rounded-2xl border border-primary/40 bg-primary/10 px-3 py-2.5 flex items-center gap-2.5 hover:border-primary transition-colors"
          >
            <i class="pi pi-clock text-primary text-sm shrink-0" aria-hidden="true"></i>
            <span class="flex-1 min-w-0 text-sm font-bold text-surface-900 truncate">
              {{ pendingCount() }} do potwierdzenia
            </span>
            <span class="inline-flex items-center gap-1 text-xs font-bold text-primary uppercase tracking-wider shrink-0">
              Pokaż <i class="pi pi-arrow-right text-[10px]" aria-hidden="true"></i>
            </span>
          </button>
        }

        <!-- Wybór pracownika — pod banerem, wspólny dla widoku dnia/tygodnia/miesiąca.
             Odpowiada wyłącznie na pytanie „czyj kalendarz oglądamy"; nie jest filtrem, więc bez
             chipa „Wszyscy" (zawsze oglądamy dokładnie jednego pracownika). -->
        @if (showEmployeeStrip()) {
          <app-employee-strip
            [employees]="filterEmployees()"
            [selectedId]="stripSelectedId()"
            (select)="onEmployeeStripSelect($event)"
          />
        }

        <!-- Pasek filtrów / view-mode -->
        <div class="mb-4" data-tour="calendar-toolbar">
          <app-calendar-filters
            [viewMode]="viewMode()"
            [value]="filters()"
            [employees]="filterEmployees()"
            [showEmployeeFilter]="false"
            [isDesktop]="isDesktop()"
            (viewModeChange)="onViewModeChange($event)"
            (valueChange)="onFiltersChange($event)"
          />
        </div>

        @if (viewMode() === 'month') {
          <div class="admin-glass-card rounded-3xl p-2 mb-4">
            <div class="flex flex-wrap items-center gap-2">
            <!-- Bez osobnej etykiety miesiąca — picker (MM yy = „sierpień 2026") sam pokazuje datę.
                 Picker wypełnia środek i ma wysoki input, żeby łatwo w niego trafić. -->
            <button
              type="button"
              class="shrink-0 h-11 w-11 rounded-xl border border-surface-300 dark:border-surface-600 grid place-items-center text-surface-700 hover:border-primary/45 transition-colors"
              (click)="shiftMonth(-1)"
              aria-label="Poprzedni miesiąc"
            >
              <i class="pi pi-chevron-left text-sm"></i>
            </button>
            <p-date-picker
              [ngModel]="selectedMonthAnchor()"
              (ngModelChange)="onMonthPicked($event)"
              view="month"
              dateFormat="MM yy"
              [showIcon]="true"
              [readonlyInput]="true"
              [fluid]="true"
              appendTo="body"
              panelStyleClass="cal-month-panel"
              (onShow)="alignMonthPanel()"
              styleClass="flex-1 min-w-0"
              inputStyleClass="!h-11 !text-sm !font-bold !text-center first-letter:uppercase"
            />
            <button
              type="button"
              class="shrink-0 h-11 w-11 rounded-xl border border-surface-300 dark:border-surface-600 grid place-items-center text-surface-700 hover:border-primary/45 transition-colors"
              (click)="shiftMonth(1)"
              aria-label="Następny miesiąc"
            >
              <i class="pi pi-chevron-right text-sm"></i>
            </button>
            <!-- Przełącznik pracownika żyje w app-employee-strip na szczycie kalendarza. -->
            </div>
            <app-month-booking-status
            [opensOn]="browsedMonthPublicationOpensOn()"
            [hasPublication]="browsedMonthHasPublication()"
              [canEdit]="canEditPreviewedDayAvailability()"
              (edit)="openMonthPublicationDrawer()"
            />
          </div>
          <app-month-view
            [anchor]="selectedMonthAnchor()"
            [selected]="selectedDate()"
            [appointments]="monthAppointments.value()"
            [filters]="filters()"
            [staticSlots]="monthStaticSlots()"
            (cellClick)="onMonthCellClick($event)"
          />
        } @else if (viewMode() === 'week') {
          @if (isDesktop()) {
            <app-week-view
              [anchor]="selectedDate()"
              [appointments]="weekAppointments.value()"
              [filters]="filters()"
              (dayClick)="onWeekDayClick($event)"
              (prev)="shiftWeek(-1)"
              (next)="shiftWeek(1)"
              (today)="goToday()"
            />
          } @else {
            <!-- Na 360px 7-kolumnowa siatka nie mieści się — pionowa agenda kart dni. -->
            <app-week-agenda
              [anchor]="selectedDate()"
              [appointments]="weekAppointments.value()"
              [filters]="filters()"
              [schedules]="$any(weeklySchedule.value())"
              [overrides]="scheduleOverrides.value()"
              [leaves]="employeeLeaves.value()"
              (dayClick)="onWeekDayClick($event)"
              (prev)="shiftWeek(-1)"
              (next)="shiftWeek(1)"
              (today)="goToday()"
            />
          }
        } @else {

        <!-- Prosty przełącznik miesiąca (widok dnia): strzałki ‹ ›  przesuwają miesiąc, nazwa w środku,
             „Dziś" wraca do dzisiaj. Wybór konkretnego dnia odbywa się na pasku dni poniżej. -->
        <div class="admin-glass-card rounded-3xl p-2 mb-3">
          <div class="flex items-center gap-2">
          <button
            type="button"
            class="shrink-0 h-11 w-11 rounded-xl border border-surface-300 dark:border-surface-600 grid place-items-center text-surface-700 hover:border-primary/45 transition-colors"
            (click)="shiftMonth(-1)"
            aria-label="Poprzedni miesiąc"
          >
            <i class="pi pi-chevron-left text-sm" aria-hidden="true"></i>
          </button>
          <p class="flex-1 min-w-0 text-center text-base font-black text-surface-900 truncate first-letter:uppercase">
            {{ selectedMonthLabelShort() }}
          </p>
          <button
            type="button"
            class="shrink-0 h-11 w-11 rounded-xl border border-surface-300 dark:border-surface-600 grid place-items-center text-surface-700 hover:border-primary/45 transition-colors"
            (click)="shiftMonth(1)"
            aria-label="Następny miesiąc"
          >
            <i class="pi pi-chevron-right text-sm" aria-hidden="true"></i>
          </button>

          <!-- „Dziś" jest ZAWSZE widoczny, tak jak w widoku tygodnia — stałe miejsce w pasku
               nawigacji czyta się lepiej niż przycisk, który znika po powrocie na dzisiaj.
               Gdy już oglądasz dzisiaj, przycisk jest nieaktywny zamiast zniknąć. -->
          <button
            type="button"
            [disabled]="isViewingToday()"
            [class]="todayButtonClasses()"
            class="shrink-0 h-11 px-3 rounded-xl border text-xs font-bold uppercase tracking-wider transition-colors"
            (click)="goToday()"
          >
            Dziś
          </button>
          </div>

          <!-- Stan zapisów dla oglądanego miesiąca — drugi wiersz TEJ SAMEJ karty, bo dotyczy
               miesiąca z nawigacji wyżej. W trybie kolumn desktopowych POMIJANY: na ekranie jest
               wtedy cały zespół, a publikacja jest per pracownik — pasek mówiłby o jednej osobie
               z wielu i wprowadzał w błąd. -->
          @if (!showDesktopColumns()) {
            <app-month-booking-status
              [opensOn]="browsedMonthPublicationOpensOn()"
              [hasPublication]="browsedMonthHasPublication()"
              [canEdit]="canEditPreviewedDayAvailability()"
              (edit)="openMonthPublicationDrawer()"
            />
          }
        </div>

        <!-- Pasek dat -->
        <div
          #daySlider
          data-tour="calendar-day-strip"
          class="admin-glass-card rounded-3xl flex gap-2 overflow-x-auto p-2 mb-4 px-2 sm:px-3 scrollbar-thin snap-x snap-mandatory"
          role="listbox"
          aria-label="Wybór dnia"
        >
          @for (day of visibleDays(); track day.getTime()) {
            <button
              type="button"
              role="option"
              [attr.aria-selected]="sameCalendarDay(day, selectedDate())"
              (click)="selectDay(day)"
              class="snap-center shrink-0 min-w-13 rounded-2xl py-2.5 px-2 flex flex-col items-center gap-0.5 transition-all shadow-sm border"
              [class.bg-primary]="sameCalendarDay(day, selectedDate())"
              [class.text-primary-contrast]="sameCalendarDay(day, selectedDate())"
              [class.border-primary]="sameCalendarDay(day, selectedDate())"
              [class.bg-surface-0]="!sameCalendarDay(day, selectedDate())"
              [class.dark:bg-surface-50]="!sameCalendarDay(day, selectedDate())"
              [class.text-surface-700]="!sameCalendarDay(day, selectedDate())"
              [class.dark:text-surface-300]="!sameCalendarDay(day, selectedDate())"
              [class.border-surface-200]="!sameCalendarDay(day, selectedDate()) && !isToday(day)"
              [class.dark:border-surface-200]="!sameCalendarDay(day, selectedDate()) && !isToday(day)"
              [class.border-primary]="isToday(day) && !sameCalendarDay(day, selectedDate())"
              [class.ring-1]="isToday(day) && !sameCalendarDay(day, selectedDate())"
              [class.ring-primary]="isToday(day) && !sameCalendarDay(day, selectedDate())"
              [class.opacity-65]="!isDayInSelectedMonth(day)"
              [attr.data-selected-day]="sameCalendarDay(day, selectedDate()) ? 'true' : null"
            >
              <span class="text-[10px] font-bold uppercase tracking-wide opacity-90">{{
                weekdayShort(day)
              }}</span>
              <span class="text-sm font-bold leading-none">{{ day.getDate() }}</span>
              @if (dayStripCountFor(day); as c) {
                @if (c.total > 0) {
                  <span
                    class="text-[9px] font-bold leading-none mt-0.5 px-1.5 py-0.5 rounded-full inline-flex items-center gap-1 tabular-nums"
                    [class.bg-surface-0/15]="sameCalendarDay(day, selectedDate())"
                    [class.dark:bg-surface-50/15]="sameCalendarDay(day, selectedDate())"
                    [class.bg-surface-100]="!sameCalendarDay(day, selectedDate())"
                    [class.dark:bg-surface-100]="!sameCalendarDay(day, selectedDate())"
                    [attr.aria-label]="c.total + ' wizyt'"
                  >
                    {{ c.total }}
                    @if (c.pending > 0) {
                      <span
                        class="w-1 h-1 rounded-full"
                        [class.bg-current]="sameCalendarDay(day, selectedDate())"
                        [class.bg-amber-500]="!sameCalendarDay(day, selectedDate())"
                        aria-label="Oczekujące"
                      ></span>
                    }
                  </span>
                } @else {
                  <span
                    class="h-1 w-1 rounded-full"
                    [class.bg-primary]="isToday(day) && !sameCalendarDay(day, selectedDate())"
                    [class.bg-current]="isToday(day) && sameCalendarDay(day, selectedDate())"
                    [class.bg-transparent]="!isToday(day)"
                    aria-hidden="true"
                  ></span>
                }
              }
            </button>
          }
        </div>
        @if (swapMode()) {
          <div
            class="mb-4 rounded-2xl border border-amber-300/60 bg-amber-50/80 dark:border-amber-500/40 dark:bg-amber-950/30 px-4 py-2.5 flex items-center gap-2"
            data-testid="swap-mode-banner"
          >
            <i class="pi pi-arrow-right-arrow-left text-amber-600 dark:text-amber-400 text-sm shrink-0" aria-hidden="true"></i>
            <p class="flex-1 min-w-0 text-xs font-semibold text-amber-900 dark:text-amber-200">
              @if (swapFirst()) {
                Wskaż drugą wizytę, aby zamienić terminy.
              } @else {
                Tryb zamiany: wskaż pierwszą wizytę.
              }
            </p>
            <button
              type="button"
              (click)="toggleSwapMode()"
              data-testid="swap-mode-cancel"
              class="shrink-0 text-[11px] font-bold uppercase tracking-wider text-amber-700 dark:text-amber-300 hover:opacity-80 transition-opacity"
            >
              Anuluj
            </button>
          </div>
        }
        @if (showDesktopColumns()) {
          <div class="flex items-center justify-end gap-2 mb-4">
            @if (isSingleEmployee()) {
              <button
                type="button"
                (click)="openBreakEditor()"
                [disabled]="!canAddBreakForSelectedDay()"
                [pTooltip]="breakDisabledReason() || 'Zablokuj wolny termin jako przerwę'"
                tooltipPosition="bottom"
                data-testid="calendar-add-break"
                class="inline-flex items-center gap-1.5 rounded-full border border-surface-300 dark:border-surface-600 px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-surface-700 dark:text-surface-300 hover:border-primary/40 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:border-surface-300 dark:disabled:hover:border-surface-600"
              >
                <i class="pi pi-pause text-[10px]" aria-hidden="true"></i>
                Dodaj przerwę
              </button>
            }
            <button
              type="button"
              (click)="openCreateVisit()"
              data-tour="calendar-add"
              class="flex items-center gap-2 rounded-full bg-surface-900 dark:bg-surface-100 text-surface-0 dark:text-surface-900 px-4 py-1.5 text-xs font-bold uppercase tracking-wider hover:opacity-90 transition-opacity"
            >
              <i class="pi pi-plus text-xs" aria-hidden="true"></i>
              Dodaj wizytę
            </button>
          </div>
        }

        <!-- Salon bez zespołu — podpowiedź. Wybór pracownika żyje w app-employee-strip na
             szczycie kalendarza; ta karta nie dubluje już selektora. -->
        @if (showEmptyTeamHint()) {
          <div class="admin-glass-card rounded-3xl p-4 mb-4">
            <div class="flex items-center gap-3">
              <div
                class="shrink-0 w-11 h-11 rounded-xl bg-primary/10 text-primary flex items-center justify-center"
                aria-hidden="true"
              >
                <i class="pi pi-id-card text-lg"></i>
              </div>
              <p class="text-sm text-surface-500">Brak pracowników — dodaj zespół w zasobach.</p>
            </div>
          </div>
        }

        <!-- Dzień wolny + są wizyty (poza grafikiem): slim przypomnienie nad listą. Gdy brak wizyt,
             komunikat „Dzień wolny" niesie stan pusty niżej (bez dublowania). -->
        @if (isSelectedDayOff() && agendaItems().length > 0) {
          <div class="rounded-3xl border border-surface-200/80 dark:border-white/10 bg-surface-100/60 dark:bg-white/[0.03] px-4 py-4 mb-3 flex flex-col items-center text-center gap-2">
            <div class="w-11 h-11 rounded-full bg-surface-200/70 dark:bg-white/10 grid place-items-center">
              <i class="pi pi-calendar-times text-surface-500 dark:text-surface-300" aria-hidden="true"></i>
            </div>
            <div>
              <p class="text-sm font-bold text-surface-900">Dzień wolny</p>
              <p class="text-xs text-surface-500 dark:text-surface-400 mt-0.5">
                {{
                  isSelectedDayOnLeave()
                    ? 'Urlop lub nieobecność w tym dniu.'
                    : 'Brak godzin pracy w tym dniu.'
                }}
              </p>
            </div>
            <!-- Na urlopie ustawienie godzin i tak jest blokowane przez nieobecność — nie pokazujemy przycisku. -->
            @if (!isSelectedDayOnLeave() && canEditPreviewedDayAvailability()) {
              <!--
                Kotwica przewodnika „Otwórzmy dzień w kalendarzu". Ten sam data-tour wisi na
                trzech wariantach tego przycisku (podgląd dnia, karta dnia wolnego, agenda) —
                pickVisibleElement bierze ten aktualnie widoczny, więc przewodnik działa
                niezależnie od widoku i szerokości ekranu.
              -->
              <button
                type="button"
                data-tour="calendar-set-day-hours"
                (click)="onSetSelectedDayHours()"
                class="mt-1 inline-flex items-center gap-2 rounded-full border border-surface-300 dark:border-surface-600 px-4 py-2 text-xs font-bold uppercase tracking-wider text-surface-700 hover:border-primary/45 transition-colors"
              >
                <i class="pi pi-clock text-[11px]" aria-hidden="true"></i>
                Ustaw godziny na ten dzień
              </button>
            }
          </div>
        }

        <!-- Wiersz: „Dodaj wizytę" (zamiast FAB w trybie dnia) + przełącznik Lista/Oś. Tylko single-column.
             Sticky top-0: pasek zostaje na górze przy przewijaniu osi/agendy (kalendarz scrolluje pod nim),
             więc dodawanie i przełącznik zawsze pod ręką bez FAB. Mrożone tło zakrywa przewijaną treść. -->
        @if (!showDesktopColumns()) {
          <div class="sticky top-0 z-20 px-2 sm:px-3 py-2 mb-2 rounded-3xl flex items-center justify-between gap-2 bg-surface-0/80 dark:bg-surface-950/80 backdrop-blur-md">
            <button
              type="button"
              (click)="onFabClick()"
              data-tour="calendar-add"
              class="inline-flex items-center gap-2 rounded-2xl bg-primary text-primary-contrast min-h-11 px-4 text-xs font-bold uppercase tracking-wider shadow-sm hover:opacity-90 transition-opacity"
            >
              <i class="pi pi-plus text-xs" aria-hidden="true"></i> Dodaj wizytę
            </button>
            <div class="inline-flex p-0.5 rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45">
              <button
                type="button"
                (click)="dayView.set('agenda')"
                class="min-h-10 px-3.5 text-xs font-bold uppercase tracking-wider rounded-xl transition-colors inline-flex items-center gap-1.5"
                [class.bg-white]="dayView() === 'agenda'"
                [class.shadow-sm]="dayView() === 'agenda'"
                [class.text-surface-900]="dayView() === 'agenda'"
                [class.dark:bg-surface-200]="dayView() === 'agenda'"
                [class.dark:text-surface-900]="dayView() === 'agenda'"
                [class.text-surface-600]="dayView() !== 'agenda'"
                [class.dark:text-surface-300]="dayView() !== 'agenda'"
              >
                <i class="pi pi-list text-[11px]" aria-hidden="true"></i> Lista
              </button>
              <button
                type="button"
                (click)="dayView.set('timeline')"
                class="min-h-10 px-3.5 text-xs font-bold uppercase tracking-wider rounded-xl transition-colors inline-flex items-center gap-1.5"
                [class.bg-white]="dayView() === 'timeline'"
                [class.shadow-sm]="dayView() === 'timeline'"
                [class.text-surface-900]="dayView() === 'timeline'"
                [class.dark:bg-surface-200]="dayView() === 'timeline'"
                [class.dark:text-surface-900]="dayView() === 'timeline'"
                [class.text-surface-600]="dayView() !== 'timeline'"
                [class.dark:text-surface-300]="dayView() !== 'timeline'"
              >
                <i class="pi pi-clock text-[11px]" aria-hidden="true"></i> Oś
              </button>
            </div>
          </div>
        }

        <!-- Oś czasu -->
        <div class="admin-glass-card rounded-3xl shadow-sm overflow-hidden">
          @if (appointments.error()) {
            <div class="p-6 text-center">
              <p class="text-red-600 dark:text-red-400 text-sm mb-3">Nie udało się wczytać wizyt.</p>
              <button
                type="button"
                (click)="appointments.reload()"
                class="inline-flex items-center gap-1.5 rounded-full border border-surface-300 dark:border-surface-600 px-4 py-1.5 text-xs font-bold uppercase tracking-wider text-surface-700 hover:border-primary/45 transition-colors"
              >
                <i class="pi pi-refresh text-[11px]" aria-hidden="true"></i>
                Spróbuj ponownie
              </button>
            </div>
          } @else if (appointments.isLoading() && appointments.status() !== 'reloading') {
            <div class="p-10 flex justify-center">
              <i class="pi pi-spin pi-spinner text-2xl text-primary" aria-hidden="true"></i>
            </div>
          } @else {
            @if (showDesktopColumns()) {
              <!-- F2.5: jeden scroll-container (X+Y) — przy zagnieżdżonym overflow-x sticky
                   brał wewnętrzny kontener jako scrolling ancestor i nagłówki się nie przyklejały. -->
              <div
                class="relative overflow-auto max-h-[calc(100dvh-18rem)] min-h-[480px]"
              >
                <div class="relative flex min-w-full w-max" [style.min-height.px]="timelineHeightPx()">
                  <div
                    class="w-17 shrink-0 sticky left-0 z-30 border-r border-surface-200/70 dark:border-surface-100 bg-linear-to-b from-white/95 to-surface-50/85 dark:from-surface-50/90 dark:to-surface-950/85 backdrop-blur-sm pt-2"
                    aria-hidden="true"
                  >
                    <!-- 72 px spacer (8 grid p-2 + 64 section header h-16) wyrównuje
                         etykiety godzin do siatki w sekcjach po prawej. -->
                    <div class="h-[72px]"></div>
                    @for (h of hourLabels(); track h) {
                      <div [style.height.px]="hourHeightPx" class="text-sm font-bold tabular-nums text-surface-600 dark:text-surface-300 pr-2 text-right">
                        {{ formatHm(h * 60) }}
                      </div>
                    }
                  </div>
                  <div class="flex-1 min-w-0">
                    <div class="relative grid grid-flow-col gap-3 p-2 min-w-full auto-cols-[minmax(15rem,1fr)]">
                      @for (col of desktopColumns(); track col.id) {
                        <section class="rounded-b-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/70 dark:bg-surface-50/45">
                          <header class="sticky top-0 z-30 h-16 px-3 py-2.5 border-b border-surface-200/70 dark:border-surface-200/70 flex flex-col justify-center bg-surface-0/95 dark:bg-surface-50/95 backdrop-blur-sm">
                            <p class="text-[10px] font-bold uppercase tracking-wider text-surface-500 leading-tight">Pracownik</p>
                            <p class="text-sm font-black text-surface-900 truncate leading-tight">{{ col.label }}</p>
                          </header>
                        <div
                          class="relative pt-2 pr-2 pb-3 bg-[linear-gradient(to_bottom,rgba(248,250,252,0.72),rgba(241,245,249,0.45))] dark:bg-[linear-gradient(to_bottom,rgba(15,23,42,0.42),rgba(2,6,23,0.22))]"
                          [style.min-height.px]="timelineHeightPx()"
                          [attr.data-employee-id]="col.id"
                        >
                          @for (seg of desktopWorkingSegments()[col.id]; track $index) {
                            <div
                              class="absolute left-0 right-0 rounded-lg bg-emerald-200/35 dark:bg-emerald-500/12 border border-emerald-300/40 dark:border-emerald-500/25"
                              [style.top.px]="segmentTopPx(seg.startMin)"
                              [style.height.px]="segmentHeightPx(seg.startMin, seg.endMin)"
                            ></div>
                          }
                          <!-- Przerwy w kolumnie: realna przerwa = klikalny kafelek (edycja/usuwanie). -->
                          @for (seg of desktopBreakSegments()[col.id]; track $index) {
                            @if (seg.breakRange) {
                              <button
                                type="button"
                                (click)="openBreakEditForEmployee(col.id, seg.breakRange!, $event)"
                                pTooltip="Edytuj lub usuń przerwę"
                                tooltipPosition="left"
                                aria-label="Edytuj przerwę"
                                [ngClass]="breakTileSurfaceClasses"
                                class="absolute left-1 right-1 z-2 rounded-xl pl-3 pr-2 border shadow-[0_8px_18px_-14px_rgba(15,23,42,0.5)] overflow-hidden flex items-center gap-1.5 text-left cursor-pointer hover:-translate-y-px hover:shadow-[0_14px_28px_-18px_rgba(15,23,42,0.6)] transition-[box-shadow,transform] focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
                                [style.top.px]="segmentTopPx(seg.startMin)"
                                [style.height.px]="segmentHeightPx(seg.startMin, seg.endMin)"
                              >
                                <div class="absolute left-0 top-0 bottom-0 w-1 rounded-l-xl bg-surface-400/70 dark:bg-surface-500/55"></div>
                                <i class="pi pi-clock text-surface-500 text-[11px] leading-none shrink-0" aria-hidden="true"></i>
                                <span class="text-xs font-bold leading-none text-surface-700 shrink-0">Przerwa</span>
                                <span class="text-[11px] font-semibold font-mono tabular-nums leading-none text-surface-500 dark:text-surface-400 truncate">{{ formatHm(seg.startMin) }}–{{ formatHm(seg.endMin) }}</span>
                                <i class="pi pi-pencil text-surface-400 text-[10px] leading-none ml-auto shrink-0" aria-hidden="true"></i>
                              </button>
                            } @else {
                              <div
                                [ngClass]="breakTileSurfaceClasses"
                                class="absolute left-1 right-1 z-2 rounded-xl pl-3 pr-2 border shadow-[0_8px_18px_-14px_rgba(15,23,42,0.5)] overflow-hidden flex items-center gap-1.5"
                                [style.top.px]="segmentTopPx(seg.startMin)"
                                [style.height.px]="segmentHeightPx(seg.startMin, seg.endMin)"
                              >
                                <div class="absolute left-0 top-0 bottom-0 w-1 rounded-l-xl bg-surface-400/70 dark:bg-surface-500/55"></div>
                                <i class="pi pi-clock text-surface-500 text-[11px] leading-none shrink-0" aria-hidden="true"></i>
                                <span class="text-xs font-bold leading-none text-surface-700 shrink-0">Przerwa</span>
                                <span class="text-[11px] font-semibold font-mono tabular-nums leading-none text-surface-500 dark:text-surface-400 truncate">{{ formatHm(seg.startMin) }}–{{ formatHm(seg.endMin) }}</span>
                              </div>
                            }
                          }
                          <div class="absolute inset-0 left-0 top-2 pointer-events-none">
                            @for (h of hourLabels(); track h) {
                              <div
                                [style.top.px]="(h - rangeStartHour()) * hourHeightPx"
                                class="absolute left-0 right-0 border-t border-surface-200/80 dark:border-surface-200/65"
                              ></div>
                            }
                          </div>
                          @for (a of col.items; track a.raw.id) {
                            <div
                              role="button"
                              tabindex="0"
                              (click)="onAppointmentTap(a.raw, $event)"
                              (keydown.enter)="onAppointmentTap(a.raw, $event)"
                              (keydown.space)="$event.preventDefault(); onAppointmentTap(a.raw, $event)"
                              class="absolute z-2 rounded-2xl pl-4 pr-3 shadow-[0_10px_22px_-16px_rgba(15,23,42,0.55)] border overflow-hidden flex flex-col justify-start cursor-pointer hover:-translate-y-px hover:shadow-[0_14px_28px_-18px_rgba(15,23,42,0.65)] transition-[box-shadow,transform] focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
                              [class.py-2.5]="!a.compact"
                              [class.gap-1.5]="!a.compact"
                              [class.py-2]="a.compact"
                              [class.gap-0.5]="a.compact"
                              [class.ring-2]="isSwapSelected(a.raw)"
                              [class.ring-primary]="isSwapSelected(a.raw)"
                              [ngClass]="appointmentTimelineSurfaceClasses(a.statusVariant)"
                              [style.left]="laneLeftStyle(a)"
                              [style.width]="laneWidthStyle(a)"
                              [style.top.px]="segmentTopPx(a.startMin)"
                              [style.height.px]="visitBlockHeightPx(a.startMin, a.endMin)"
                            >
                              <div class="absolute left-0 top-0 bottom-0 w-1.5 rounded-l-2xl" [ngClass]="accentBarClasses(a.statusVariant)"></div>
                              @if (a.isOutsideSchedule) {
                                <span
                                  class="absolute top-1.5 right-1.5 inline-flex items-center justify-center w-2 h-2 rounded-full bg-red-500 ring-2 ring-white dark:ring-surface-900"
                                  pTooltip="Wizyta poza godzinami pracy"
                                  tooltipPosition="left"
                                  aria-label="Wizyta poza godzinami pracy"
                                ></span>
                              }
                              @if (a.compact) {
                                <div class="flex items-baseline gap-2 min-w-0">
                                  <span class="text-sm font-bold tabular-nums text-surface-700 dark:text-surface-300 shrink-0">{{ formatHm(a.startMin) }}</span>
                                  <span class="text-sm font-bold leading-tight text-surface-900 truncate min-w-0">{{ appointmentServiceName(a.raw) }}</span>
                                </div>
                                <p class="text-xs font-medium text-surface-500 dark:text-surface-400 truncate">{{ appointmentCustomerLine(a.raw) }}</p>
                              } @else {
                                <div class="flex items-start justify-between gap-2 min-w-0">
                                  <p class="text-base font-bold leading-tight text-surface-900 truncate min-w-0">{{ appointmentServiceName(a.raw) }}</p>
                                  <span
                                    class="inline-flex items-center justify-center w-6 h-6 rounded-full border shrink-0"
                                    [ngClass]="statusBadgeClasses(a.statusVariant)"
                                    [attr.aria-label]="statusLabel(a.statusVariant)"
                                    [title]="statusLabel(a.statusVariant)"
                                  >
                                    <i [class]="statusIconClass(a.statusVariant)" class="text-[10px]"></i>
                                  </span>
                                </div>
                                <p class="text-[13px] font-semibold font-mono tabular-nums text-surface-500 dark:text-surface-400">{{ formatHm(a.startMin) }} – {{ formatHm(a.endMin) }}</p>
                                <p class="text-sm text-surface-700 truncate">{{ appointmentCustomerLine(a.raw) }}</p>
                              }
                            </div>
                          }

                          @if (nowLineTopPx() !== null) {
                            <div
                              class="absolute left-0 right-0 z-10 pointer-events-none h-0.5 bg-emerald-600/90 shadow-[0_0_8px_rgba(5,150,105,0.45)]"
                              [style.top.px]="nowLineTopPx()!"
                            ></div>
                          }
                        </div>
                      </section>
                    }
                  </div>
                </div>
              </div>
              </div>
            } @else {
            @if (dayView() === 'agenda') {
              <!-- ===== Widok agendy (początek pracy + wizyty + przerwy + koniec pracy) ===== -->
              @if (agendaItems().length === 0) {
                <div class="p-8 text-center">
                  <div class="w-12 h-12 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mx-auto mb-3">
                    <i
                      class="text-xl text-surface-400"
                      [class.pi]="true"
                      [class.pi-calendar-times]="isSelectedDayOff()"
                      [class.pi-calendar]="!isSelectedDayOff()"
                      aria-hidden="true"
                    ></i>
                  </div>
                  <h3 class="text-base font-bold text-surface-900 mb-1">
                    {{ isSelectedDayOff() ? 'Dzień wolny' : 'Brak wizyt w tym dniu' }}
                  </h3>
                  <p class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed mb-4">
                    @if (isSelectedDayOnLeave()) {
                      Urlop lub nieobecność w tym dniu.
                    } @else if (isSelectedDayOff()) {
                      Brak godzin pracy w tym dniu. Możesz ustawić godziny albo dodać wizytę.
                    } @else {
                      Wybierz inny dzień w pasku dat powyżej albo dodaj nową wizytę dla tego dnia.
                    }
                  </p>
                  <!-- Na urlopie żadne akcje nie mają sensu (godziny i wizyty i tak zablokowane). -->
                  @if (!isSelectedDayOnLeave()) {
                    <div class="flex flex-col sm:flex-row items-center justify-center gap-2">
                      @if (isSelectedDayOff() && canEditPreviewedDayAvailability()) {
                        <button
                          type="button"
                          data-tour="calendar-set-day-hours"
                          (click)="onSetSelectedDayHours()"
                          class="inline-flex items-center gap-2 rounded-full px-4 py-2 text-sm font-bold border border-surface-300 dark:border-surface-600 text-surface-700 hover:border-primary/45 transition-colors"
                        >
                          <i class="pi pi-clock"></i> Ustaw godziny na ten dzień
                        </button>
                      }
                      <button
                        type="button"
                        (click)="openCreateVisit()"
                        class="inline-flex items-center gap-2 rounded-full px-4 py-2 text-sm font-bold bg-primary text-primary-contrast hover:opacity-90 transition-opacity"
                      >
                        <i class="pi pi-plus"></i> Dodaj wizytę
                      </button>
                    </div>
                  }
                </div>
              } @else {
                <ul class="divide-y divide-surface-200/70 dark:divide-surface-200/60">
                  <!-- Znacznik „Teraz" (tylko dziś) wstawiany chronologicznie między pozycje agendy. -->
                  <ng-template #agendaNowMarker>
                    <li class="flex items-center gap-2 px-3 sm:px-4 py-1" aria-label="Teraz">
                      <span class="w-14 shrink-0 text-right pr-1 text-[11px] font-black tabular-nums text-emerald-600 dark:text-emerald-400">{{ formatHm(nowMinutes()) }}</span>
                      <span class="relative flex-1 h-px bg-emerald-500/50">
                        <span class="absolute -left-0.5 top-1/2 -translate-y-1/2 h-2 w-2 rounded-full bg-emerald-500"></span>
                      </span>
                      <span class="shrink-0 text-[10px] font-bold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">Teraz</span>
                    </li>
                  </ng-template>
                  @for (item of agendaItems(); track item.key; let i = $index) {
                  @if (agendaNowIndex() === i) {
                    <ng-container *ngTemplateOutlet="agendaNowMarker" />
                  }
                  @if (item.kind === 'work-start' || item.kind === 'work-end') {
                    <li
                      appAgendaTile
                      [startLabel]="formatHm(item.startMin)"
                      [barClass]="'bg-surface-300 dark:bg-surface-500/50'"
                    >
                      <p class="font-bold text-surface-600 dark:text-surface-300 leading-tight">
                        {{ item.kind === 'work-start' ? 'Początek pracy' : 'Koniec pracy' }}
                      </p>
                    </li>
                  } @else if (item.kind === 'break') {
                    <li
                      appAgendaTile
                      [startLabel]="formatHm(item.startMin)"
                      [endLabel]="formatHm(item.endMin)"
                      [barClass]="'bg-surface-400/70 dark:bg-surface-500/55'"
                      [clickable]="!!item.breakRange"
                      [nowFraction]="item.key === agendaNowContainer()?.key ? agendaNowFraction() : null"
                      (activate)="item.breakRange && openBreakEditFor(item.breakRange, $event)"
                    >
                      <p class="font-bold text-surface-700 leading-tight">Przerwa</p>
                      <p class="text-sm text-surface-500 dark:text-surface-400 mt-0.5">{{ breakDurationLabel(item.startMin, item.endMin) }}</p>
                    </li>
                  } @else if (item.kind === 'visit') {
                    @let a = item.visit;
                    <!-- O statusie świadczy KOLOR PASKA (barClass) — bez tła statusowego, bez chipa,
                         bez „Anuluj" (anulowanie z drawera). aria-label niesie status dla SR. -->
                    <li
                      appAgendaTile
                      [startLabel]="formatHm(a.startMin)"
                      [endLabel]="formatHm(a.endMin)"
                      [barClass]="accentBarClasses(a.statusVariant)"
                      [clickable]="true"
                      [selected]="isSwapSelected(a.raw)"
                      [ariaLabel]="appointmentServiceName(a.raw) + ' — ' + statusLabel(a.statusVariant)"
                      [nowFraction]="item.key === agendaNowContainer()?.key ? agendaNowFraction() : null"
                      (activate)="onAppointmentTap(a.raw, $event)"
                    >
                      <p class="font-bold text-surface-900 leading-tight truncate">
                        {{ appointmentServiceName(a.raw) }}
                      </p>
                      <p class="text-sm text-surface-600 dark:text-surface-300 truncate mt-0.5">
                        {{ appointmentCustomerLine(a.raw) }}
                      </p>
                      @if (a.isOutsideSchedule) {
                        <span class="inline-flex items-center gap-1 mt-1 text-[10px] font-bold uppercase tracking-wide text-red-600 dark:text-red-400">
                          <span class="w-1.5 h-1.5 rounded-full bg-red-500"></span> Poza grafikiem
                        </span>
                      }
                      @if (canQuickConfirm(a.statusVariant) && canMutateAppointment(a.raw)) {
                        <div class="pt-2">
                          <button
                            type="button"
                            class="text-[11px] font-bold uppercase tracking-wider rounded-full px-3 py-1.5 border border-emerald-500/45 text-emerald-700 dark:text-emerald-300 bg-white/85 dark:bg-surface-50/65 hover:bg-emerald-50 dark:hover:bg-emerald-900/25 transition-colors"
                            [disabled]="isAppointmentActionLocked(a.raw.id)"
                            (click)="quickConfirm(a.raw.id, $event)"
                          >
                            Zatwierdź
                          </button>
                        </div>
                      }
                    </li>
                  } @else if (item.kind === 'slot') {
                    <!-- Wolny termin grafiku statycznego — kliknij, aby dodać wizytę. -->
                    <li
                      appAgendaTile
                      [startLabel]="formatHm(item.startMin)"
                      [endLabel]="formatHm(item.endMin)"
                      [barClass]="'bg-primary/35 dark:bg-primary/35'"
                      [clickable]="true"
                      ariaLabel="Wolny termin — dodaj wizytę"
                      [nowFraction]="item.key === agendaNowContainer()?.key ? agendaNowFraction() : null"
                      (activate)="openCreateVisitAt(item.startMin)"
                    >
                      <p class="font-bold text-surface-500 dark:text-surface-400 leading-tight">Wolny termin</p>
                      <p class="text-xs text-surface-400 dark:text-surface-500 mt-0.5">Dodaj wizytę</p>
                    </li>
                  }
                  }
                  @if (agendaNowIndex() === agendaItems().length) {
                    <ng-container *ngTemplateOutlet="agendaNowMarker" />
                  }
                </ul>
              }
            } @else {
            <div class="relative flex" [style.min-height.px]="timelineHeightPx()">
              <!-- Etykiety godzin -->
              <div
                class="w-17 shrink-0 border-r border-surface-200/70 dark:border-surface-100 bg-linear-to-b from-white/80 to-surface-50/60 dark:from-surface-50/70 dark:to-surface-950/60 pt-2"
                aria-hidden="true"
              >
                @for (h of hourLabels(); track h) {
                  <div [style.height.px]="hourHeightPx" class="text-sm font-bold tabular-nums text-surface-600 dark:text-surface-300 pr-2 text-right">
                    {{ formatHm(h * 60) }}
                  </div>
                }
              </div>

              <!-- Siatka + bloki -->
              <div
                class="flex-1 relative pt-2 pr-2 pb-3 bg-[linear-gradient(to_bottom,rgba(248,250,252,0.72),rgba(241,245,249,0.45))] dark:bg-[linear-gradient(to_bottom,rgba(15,23,42,0.42),rgba(2,6,23,0.22))]"
              >
                <!-- Pas godzin pracy z SUROWYCH godzin (ciągły, bez przerywania przez przerwy —
                     kafelek przerwy leży NA pasie). Neutralny (surface), by nie konkurował kolorem
                     z wizytami; zielony zostaje dla „Teraz"/„Zakończona". pointer-events-none. -->
                @for (seg of currentDayWorkBandSegments(); track $index) {
                  <div
                    class="absolute left-0 right-0 rounded-lg bg-surface-200/45 dark:bg-surface-100/10 border border-surface-300/50 dark:border-surface-200/15 pointer-events-none"
                    [style.top.px]="segmentTopPx(seg.startMin)"
                    [style.height.px]="segmentHeightPx(seg.startMin, seg.endMin)"
                  ></div>
                }

                <!-- linie godzin -->
                <div class="absolute inset-0 left-0 top-2 pointer-events-none">
                  @for (h of hourLabels(); track h) {
                    <div
                      [style.top.px]="(h - rangeStartHour()) * hourHeightPx"
                      class="absolute left-0 right-0 border-t border-surface-200/80 dark:border-surface-200/65"
                    ></div>
                  }
                </div>

                <!-- Przerwy (grafik). Ta sama „skorupa" co wizyty (timelineTile*), neutralny lewy
                     pasek. Realna przerwa = klikalny kafelek → drawer edycji/usunięcia. -->
                @for (seg of breakSegments(); track $index) {
                  @if (seg.breakRange) {
                    <button
                      type="button"
                      (click)="openBreakEditFor(seg.breakRange!, $event)"
                      pTooltip="Edytuj lub usuń przerwę"
                      tooltipPosition="left"
                      aria-label="Edytuj przerwę"
                      data-testid="break-tile"
                      [ngClass]="[timelineTileShellClasses, timelineTileSurfaceClasses]"
                      class="absolute left-1 right-1 z-1 pl-4 pr-3 py-2 flex flex-col justify-start gap-0.5 text-left cursor-pointer hover:-translate-y-px transition-transform focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
                      [style.top.px]="segmentTopPx(seg.startMin)"
                      [style.height.px]="segmentHeightPx(seg.startMin, seg.endMin)"
                    >
                      <div class="absolute left-0 top-0 bottom-0 w-1.5 rounded-l-2xl" [ngClass]="neutralAccentBarClasses"></div>
                      <div class="flex items-baseline gap-2 min-w-0">
                        <span class="text-sm font-bold tabular-nums text-surface-700 shrink-0">{{ formatHm(seg.startMin) }}–{{ formatHm(seg.endMin) }}</span>
                        <i class="pi pi-pencil text-surface-400 text-[10px] leading-none ml-auto shrink-0" aria-hidden="true"></i>
                      </div>
                      <p class="text-xs font-medium text-surface-500 dark:text-surface-400 truncate">Przerwa</p>
                    </button>
                  } @else {
                    <div
                      [ngClass]="[timelineTileShellClasses, timelineTileSurfaceClasses]"
                      class="absolute left-1 right-1 z-1 pl-4 pr-3 py-2 flex flex-col justify-start gap-0.5"
                      [style.top.px]="segmentTopPx(seg.startMin)"
                      [style.height.px]="segmentHeightPx(seg.startMin, seg.endMin)"
                    >
                      <div class="absolute left-0 top-0 bottom-0 w-1.5 rounded-l-2xl" [ngClass]="neutralAccentBarClasses"></div>
                      <span class="text-sm font-bold tabular-nums text-surface-700 shrink-0">{{ formatHm(seg.startMin) }}–{{ formatHm(seg.endMin) }}</span>
                      <p class="text-xs font-medium text-surface-500 dark:text-surface-400 truncate">Przerwa</p>
                    </div>
                  }
                }

                <!-- Wolne terminy grafiku statycznego (dashed) — pod wizytami (z-1), klik = dodaj wizytę. -->
                @for (slot of selectedDayStaticSlots(); track slot.startMin) {
                  <button
                    type="button"
                    (click)="openCreateVisitAt(slot.startMin)"
                    aria-label="Wolny termin — dodaj wizytę"
                    class="absolute left-1 right-1 z-1 rounded-2xl border-2 border-dashed border-primary/40 bg-primary/5 hover:bg-primary/10 flex items-center gap-2 pl-4 pr-3 overflow-hidden transition-colors"
                    [style.top.px]="segmentTopPx(slot.startMin)"
                    [style.height.px]="segmentHeightPx(slot.startMin, slot.endMin)"
                  >
                    <i class="pi pi-plus text-primary text-xs shrink-0" aria-hidden="true"></i>
                    <span class="text-xs font-bold text-primary truncate">Wolny termin</span>
                    <span class="text-[11px] font-mono tabular-nums text-surface-400 shrink-0">{{ formatHm(slot.startMin) }}</span>
                  </button>
                }

                <!-- Wizyty -->
                @for (a of positionedAppointments(); track a.raw.id) {
                <div
                    role="button"
                    tabindex="0"
                    [attr.aria-label]="appointmentServiceName(a.raw) + ' — ' + statusLabel(a.statusVariant)"
                    (click)="onAppointmentTap(a.raw, $event)"
                    (keydown.enter)="onAppointmentTap(a.raw, $event)"
                    (keydown.space)="$event.preventDefault(); onAppointmentTap(a.raw, $event)"
                    class="absolute z-2 pl-4 pr-3 flex flex-col justify-start cursor-pointer hover:-translate-y-px transition-transform focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
                    [class.py-2.5]="!a.compact"
                    [class.gap-1.5]="!a.compact"
                    [class.py-2]="a.compact"
                    [class.gap-0.5]="a.compact"
                    [class.ring-2]="isSwapSelected(a.raw)"
                              [class.ring-primary]="isSwapSelected(a.raw)"
                              [ngClass]="[timelineTileShellClasses, timelineTileSurfaceClasses]"
                    [style.left]="laneLeftStyle(a)"
                    [style.width]="laneWidthStyle(a)"
                    [style.top.px]="segmentTopPx(a.startMin)"
                    [style.height.px]="visitBlockHeightPx(a.startMin, a.endMin)"
                  >
                    <div class="absolute left-0 top-0 bottom-0 w-1.5 rounded-l-2xl" [ngClass]="accentBarClasses(a.statusVariant)"></div>
                    @if (a.isOutsideSchedule) {
                      <span
                        class="absolute top-1.5 right-1.5 inline-flex items-center justify-center w-2 h-2 rounded-full bg-red-500 ring-2 ring-white dark:ring-surface-900"
                        pTooltip="Wizyta poza godzinami pracy"
                        tooltipPosition="left"
                        aria-label="Wizyta poza godzinami pracy"
                      ></span>
                    }
                    @if (a.compact) {
                      <!-- Krótka wizyta (<45 min): gęsty układ — godzina + usługa w jednym wierszu,
                           klientka pod spodem. Nazwisko zawsze widoczne; akcje przez tap (sheet/panel). -->
                      <div class="flex items-baseline gap-2 min-w-0">
                        <span class="text-sm font-bold tabular-nums text-surface-700 dark:text-surface-300 shrink-0">{{ formatHm(a.startMin) }}</span>
                        <span class="text-sm font-bold leading-tight text-surface-900 truncate min-w-0">{{ appointmentServiceName(a.raw) }}</span>
                      </div>
                      <p class="text-xs font-medium text-surface-500 dark:text-surface-400 truncate">{{ appointmentCustomerLine(a.raw) }}</p>
                    } @else {
                      <!-- Status niesie WYŁĄCZNIE lewy pasek (accentBarClasses) — bez tła statusowego
                           i bez pigułki, spójnie z widokiem agendy. -->
                      <p class="text-base font-bold leading-tight text-surface-900 truncate min-w-0">{{ appointmentServiceName(a.raw) }}</p>
                      <p class="text-[13px] font-semibold font-mono tabular-nums text-surface-500 dark:text-surface-400 shrink-0">
                        {{ formatHm(a.startMin) }} – {{ formatHm(a.endMin) }}
                      </p>
                      <p class="text-sm text-surface-700 truncate">
                        {{ appointmentCustomerLine(a.raw) }}
                      </p>
                      @if ((canQuickConfirm(a.statusVariant) || canQuickCancel(a.statusVariant)) && canMutateAppointment(a.raw)) {
                        <div class="flex items-center gap-1.5 pt-0.5">
                          @if (canQuickConfirm(a.statusVariant)) {
                            <button
                              type="button"
                              class="text-[11px] font-bold uppercase tracking-wider rounded-full px-3 py-1.5 border border-emerald-500/45 text-emerald-700 dark:text-emerald-300 bg-white/85 dark:bg-surface-50/65 hover:bg-emerald-50 dark:hover:bg-emerald-900/25 transition-colors"
                              [disabled]="isAppointmentActionLocked(a.raw.id)"
                              (click)="quickConfirm(a.raw.id, $event)"
                            >
                              Zatwierdź
                            </button>
                          }
                          @if (canQuickCancel(a.statusVariant)) {
                            <button
                              type="button"
                              class="text-[11px] font-bold uppercase tracking-wider rounded-full px-3 py-1.5 border border-red-500/45 text-red-700 dark:text-red-300 bg-white/85 dark:bg-surface-50/65 hover:bg-red-50 dark:hover:bg-red-900/25 transition-colors"
                              [disabled]="isAppointmentActionLocked(a.raw.id)"
                              (click)="quickCancel(a.raw.id, $event)"
                            >
                              Anuluj
                            </button>
                          }
                        </div>
                      }
                    }
                  </div>
                }

                <!-- Aktualny czas -->
                @if (nowLineTopPx() !== null) {
                  <div
                    class="absolute left-0 right-0 z-10 pointer-events-none h-0.5 bg-emerald-600/90 shadow-[0_0_8px_rgba(5,150,105,0.5)]"
                    [style.top.px]="nowLineTopPx()!"
                  ></div>
                }

                <!-- Empty state: brak wizyt / dzień wolny (oś czasu wciąż widoczna jako tło).
                     Nie pokazujemy, gdy są kafelki wolnych terminów grafiku statycznego. -->
                @if (positionedAppointments().length === 0 && selectedDayStaticSlots().length === 0) {
                  <div class="absolute inset-x-4 top-12 sm:top-20 z-20 flex justify-center pointer-events-none">
                    <div class="pointer-events-auto max-w-sm w-full rounded-3xl border border-surface-200/80 dark:border-surface-200/70 bg-white/95 dark:bg-surface-50/95 backdrop-blur-sm shadow-lg p-5 sm:p-6 text-center">
                      <div class="w-12 h-12 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mx-auto mb-3">
                        <i
                          class="text-xl text-surface-400"
                          [class.pi]="true"
                          [class.pi-calendar-times]="isSelectedDayOff()"
                          [class.pi-calendar]="!isSelectedDayOff()"
                          aria-hidden="true"
                        ></i>
                      </div>
                      <h3 class="text-base font-bold text-surface-900 mb-1">
                        {{ isSelectedDayOff() ? 'Dzień wolny' : 'Brak wizyt w tym dniu' }}
                      </h3>
                      <p class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed mb-4">
                        @if (isSelectedDayOnLeave()) {
                          Urlop lub nieobecność w tym dniu.
                        } @else if (isSelectedDayOff()) {
                          Brak godzin pracy w tym dniu. Możesz ustawić godziny albo dodać wizytę.
                        } @else {
                          Wybierz inny dzień w pasku dat powyżej albo dodaj nową wizytę dla tego dnia.
                        }
                      </p>
                      <!-- Na urlopie żadne akcje nie mają sensu (godziny i wizyty i tak zablokowane). -->
                      @if (!isSelectedDayOnLeave()) {
                        <div class="flex flex-col sm:flex-row items-center justify-center gap-2">
                          @if (isSelectedDayOff() && canEditPreviewedDayAvailability()) {
                            <button
                              type="button"
                              data-tour="calendar-set-day-hours"
                              (click)="onSetSelectedDayHours()"
                              class="inline-flex items-center gap-2 rounded-full px-4 py-2 text-sm font-bold border border-surface-300 dark:border-surface-600 text-surface-700 hover:border-primary/45 transition-colors"
                            >
                              <i class="pi pi-clock"></i> Ustaw godziny na ten dzień
                            </button>
                          }
                          <button
                            type="button"
                            (click)="openCreateVisit()"
                            class="inline-flex items-center gap-2 rounded-full px-4 py-2 text-sm font-bold bg-primary text-primary-contrast hover:opacity-90 transition-opacity"
                          >
                            <i class="pi pi-plus"></i>
                            Dodaj wizytę
                          </button>
                        </div>
                      }
                    </div>
                  </div>
                }

              </div>
            </div>
            }
            }
          }
        </div>

        }
      </div>

          </div>

          <!-- Sticky panel detail (desktop split-view) — widoczny tylko gdy enableSplitDetailLayout() i lg+ -->
          @if (enableSplitDetailLayout()) {
            <aside class="hidden lg:block">
              <app-appointment-detail-sheet
                [appointment]="selectedAppointment()"
                [preloadedDetail]="deepLinkDetail()"
                [canRevealInCalendar]="canRevealSelectedInCalendar()"
                [isDesktop]="true"
                [isUpdating]="isSelectedAppointmentUpdating()"
                [canMutate]="canMutateAppointment(selectedAppointment())"
                [embedded]="true"
                [hideEmployeeLine]="isSoloSalon()"
                [canAddBreakAfter]="canAddBreakAfterSelected()"
                (closeRequested)="closeAppointmentSheet()"
                (revealRequested)="revealSelectedInCalendar()"
                (confirm)="onSheetConfirm($event)"
                (cancel)="onSheetCancel($event)"
                (rescheduleRequested)="onSheetReschedule($event)"
                (changeServiceRequested)="onSheetChangeService($event)"
                (durationChanged)="onSheetDurationChanged()"
                (rebookRequested)="onSheetRebook($event)"
                (addBreakAfterRequested)="onAddBreakAfterSelected($event)"
                (swapRequested)="startSwapWith($event)"
              />
            </aside>
          }
        </div>

      <!-- Drawer/Sheet — mobile fallback. Na desktop split-view (gdy widoczny embedded panel obok) drawer się nie pokazuje. -->
      @if (!enableSplitDetailLayout() || !isDesktop()) {
        <app-appointment-detail-sheet
          [appointment]="selectedAppointment()"
          [preloadedDetail]="deepLinkDetail()"
          [canRevealInCalendar]="canRevealSelectedInCalendar()"
          [isDesktop]="isDesktop()"
          [isUpdating]="isSelectedAppointmentUpdating()"
          [canMutate]="canMutateAppointment(selectedAppointment())"
          [hideEmployeeLine]="isSoloSalon()"
          [canAddBreakAfter]="canAddBreakAfterSelected()"
          (closeRequested)="closeAppointmentSheet()"
          (revealRequested)="revealSelectedInCalendar()"
          (confirm)="onSheetConfirm($event)"
          (cancel)="onSheetCancel($event)"
          (rescheduleRequested)="onSheetReschedule($event)"
          (changeServiceRequested)="onSheetChangeService($event)"
          (durationChanged)="onSheetDurationChanged()"
          (rebookRequested)="onSheetRebook($event)"
          (addBreakAfterRequested)="onAddBreakAfterSelected($event)"
          (swapRequested)="startSwapWith($event)"
        />
      }

      <!-- Ciężkie dialogi montowane DOPIERO przy otwarciu — tak jak drawery dostępności niżej.
           Każdy ma w konstruktorze bezparametrowe rxResource (pracownicy, klienci, kategorie
           usług), więc wiszące w DOM strzelały do API przy każdym montowaniu kalendarza, mimo
           że użytkownik ich nie otwierał: 3 z 4 wywołań /api/Employees, /api/Customers x2
           i /api/ServiceCategories x3 brały się właśnie stąd. -->
      @if (rescheduleTarget()) {
        <app-reschedule-appointment-dialog
          [appointment]="rescheduleTarget()"
          [isDesktop]="isDesktop()"
          [allowEmployeeChange]="canMutateOthers()"
          (closeRequested)="closeRescheduleDialog()"
          (success)="onRescheduleSuccess($event)"
        />
      }

      @if (changeServiceTarget()) {
        <app-change-service-dialog
          [appointment]="changeServiceTarget()"
          [isDesktop]="isDesktop()"
          (closeRequested)="closeChangeServiceDialog()"
          (success)="onChangeServiceSuccess()"
        />
      }

      @if (swapPair()) {
        <app-swap-appointments-dialog
          [first]="swapPair()?.first ?? null"
          [second]="swapPair()?.second ?? null"
          [isDesktop]="isDesktop()"
          (closeRequested)="closeSwapDialog()"
          (success)="onSwapSuccess()"
        />
      }

      @if (createContext()) {
        <app-create-appointment-drawer
          [context]="createContext()"
          [allowEmployeeChange]="canMutateOthers()"
          (closeRequested)="closeCreateDrawer()"
          (success)="onCreateSuccess($event)"
        />
      }

      @if (breakContext()) {
        <app-break-editor-drawer
          [context]="breakContext()"
          [schedules]="breakEditorSchedules()"
          [overrides]="breakEditorOverrides()"
          [leaves]="breakEditorLeaves()"
          [dayAppointments]="breakDayAppointments()"
          [slotStepMinutes]="appointmentSlotStepMinutes()"
          (closeRequested)="closeBreakEditor()"
          (success)="onBreakSuccess()"
        />
      }

      @if (quickAddOpen()) {
        <app-quick-add-sheet
          [isOpen]="true"
          [isDesktop]="isDesktop()"
          [date]="selectedDateYmd()"
          [employeeId]="effectiveEmployeeId()"
          [allowEmployeeChange]="canMutateOthers()"
          [schedules]="breakEditorSchedules()"
          [overrides]="breakEditorOverrides()"
          [leaves]="breakEditorLeaves()"
          [dayAppointments]="breakDayAppointments()"
          [slotStepMinutes]="appointmentSlotStepMinutes()"
          [allowBreak]="canAddBreakForSelectedDay()"
          (closeRequested)="closeQuickAdd()"
          (createSuccess)="onQuickCreateSuccess($event)"
          (breakSuccess)="onQuickBreakSuccess()"
        />
      }

      <app-month-day-sheet
        [date]="previewedDay()"
        [appointments]="previewedDayAppointments()"
        [isDesktop]="isDesktop()"
        [canCreate]="canCreateAppointmentForOwnScope()"
        [availability]="previewedDayAvailability()"
        [canEditAvailability]="canEditPreviewedDayAvailability()"
        (closeRequested)="closePreviewedDay()"
        (openDayView)="onPreviewedDayOpenDay($event)"
        (addAppointment)="onPreviewedDayAdd($event)"
        (appointmentSelected)="onPreviewedDayPick($event)"
        (addLeave)="onPreviewedDayAddLeave($event)"
        (setDayHours)="onPreviewedDaySetHours($event)"
      />

      <!-- Drawery edycji dostępności dnia (urlop / godziny dnia) — wchłonięte z podglądu miesiąca.
           Tworzone dopiero przy otwarciu (formularze odpytują API w konstruktorze). -->
      @if (availLeaveDrawerOpen()) {
        <app-employee-leave-form-drawer
          [isOpen]="true"
          [id]="availTargetEmployeeId()"
          [initialDate]="availActionDate()"
          (saved)="onAvailabilityDrawerSaved()"
          (closed)="availLeaveDrawerOpen.set(false)"
        />
      }
      @if (monthPublicationDrawerOpen()) {
        <app-month-publication-drawer
          [isOpen]="true"
          [employeeId]="availTargetEmployeeId()"
          [year]="browsedYear()"
          [month]="browsedMonth()"
          [publication]="browsedMonthPublication()"
          (saved)="onMonthPublicationSaved()"
          (closed)="monthPublicationDrawerOpen.set(false)"
        />
      }
      @if (availSpecialDayDrawerOpen()) {
        <app-employee-special-day-drawer
          [isOpen]="true"
          [id]="availTargetEmployeeId()"
          [initialDate]="availActionDate()"
          (saved)="onAvailabilityDrawerSaved()"
          (closed)="availSpecialDayDrawerOpen.set(false)"
        />
      }
    </div>
  `,
})
export class VisitScheduleComponent {


  /** Eksport do szablonu (porównanie dat kalendarzowych). */
  protected readonly sameCalendarDay = sameCalendarDay;

  // Delegate pure helpers wyciągniętych do `shared/` — zachowujemy istniejące `this.X(...)`
  // call sites bez zmian. Sygnatury identyczne, logika identyczna.
  private readonly startOfDay = startOfDay;
  private readonly startOfMonth = startOfMonth;
  private readonly resolveWorkingRangesForDate = resolveWorkingRangesForDate;
  private readonly resolveBreaksForDate = resolveBreaksForDate;
  private readonly findBlockingLeaveForDate = findBlockingLeaveForDate;
  private readonly pickOverrideForDate = pickOverrideForDate;
  private readonly pickScheduleForDate = pickScheduleForDate;

  private http = inject(HttpClient);
  private apiBaseUrl = inject(API_BASE_URL);
  private employeesClient = inject(EmployeesClient);
  private salonSettingsClient = inject(SalonSettingsClient);
  private appointmentsClient = inject(AppointmentsClient);
  private messages = inject(MessageService);
  private confirm = inject(ConfirmationService);
  private auth = inject(AuthSessionService);
  private router = inject(Router);
  private readonly appointmentFocus = inject(AppointmentFocusService);
  private route = inject(ActivatedRoute);
  private lastEmployeeStore = inject(LastScheduleEmployeeStore);
  protected calendarState = inject(CalendarStateService);
  private daySlider = viewChild<ElementRef<HTMLElement>>('daySlider');
  private viewportWidth = signal(typeof window !== 'undefined' ? window.innerWidth : 1280);
  private appointmentUpdates = signal<Record<string, boolean>>({});

  /** Polityka widoczności kalendarza ustawiona przez ownera (F2.1/F2.2). */
  protected readonly staffCalendarPolicy = computed(
    () =>
      this.salonSettings.value()?.staffCalendarVisibilityPolicy
      ?? StaffCalendarVisibilityPolicy.OwnCalendarOnly,
  );

  /** Czy zalogowany użytkownik widzi cały zespół (selektor pracowników, multi-kolumna desktop). */
  protected readonly canSeeTeam = computed(() => {
    const role = this.auth.currentRole();
    if (role === 'owner' || role === 'manager' || role === 'kiosk') return true;
    if (role === 'employee') {
      const p = this.staffCalendarPolicy();
      return p === StaffCalendarVisibilityPolicy.TeamReadOnly
          || p === StaffCalendarVisibilityPolicy.TeamFull;
    }
    return false;
  });

  /** Czy zalogowany użytkownik może mutować cudze wizyty (Zatwierdź / Anuluj / Reschedule). */
  protected readonly canMutateOthers = computed(() => {
    const role = this.auth.currentRole();
    // Kiosk („Recepcja") obsługuje wizyty całego zespołu — pełne mutacje jak owner/manager.
    if (role === 'owner' || role === 'manager' || role === 'kiosk') return true;
    if (role === 'employee') {
      return this.staffCalendarPolicy() === StaffCalendarVisibilityPolicy.TeamFull;
    }
    return false;
  });

  /**
   * „Employee scoped" = widok ograniczony do jednego pracownika (banery „Twoja zmiana",
   * brak selektora pracownika, single-col oś czasu). Employee bez team-policy jest scoped;
   * z TeamReadOnly/TeamFull wpada do widoku zespołu jak Owner/Manager.
   */
  protected readonly isEmployeeScoped = computed(() => {
    const role = this.auth.currentRole();
    if (role === 'employee') return !this.canSeeTeam();
    // Kiosk widzi cały zespół (multi-kolumna + selektor), więc NIE jest scoped do jednej osoby.
    return false;
  });

  /**
   * Właściciel lub menedżer — jedyni odbiorcy checklisty pierwszych kroków (kroki: grafik, usługi,
   * zasady rezerwacji, pierwsza wizyta dotyczą konfiguracji salonu, nie codziennej pracy pracownika).
   */
  protected readonly isOwnerOrManager = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager';
  });

  protected readonly isDesktop = computed(() => this.viewportWidth() >= 1024);

  /**
   * Mobilny widok tygodnia (agenda 7 dni). W tym trybie karta sama dopasowuje wysokość do
   * viewportu (`WeekAgendaComponent`), więc dolny padding pod navbar (`admin-page-pad-for-bottom-nav`)
   * jest zbędny — navbar jest w przepływie, a padding tylko dokładałby pusty scroll pod kartą.
   */
  protected readonly isMobileWeek = computed(() => this.viewMode() === 'week' && !this.isDesktop());

  /**
   * Multi-kolumna desktop dla każdego, kto widzi cały zespół (SOLO = jedna szeroka kolumna).
   * Pracownik scoped (OwnCalendarOnly) też dostaje ten układ, ale z jedną własną kolumną
   * (`columnEmployees`) — dzięki temu jego kalendarz wygląda identycznie jak u właściciela solo,
   * zamiast wąskiego single-col + bocznego panelu detalu.
   */
  protected readonly showDesktopColumns = computed(
    () => this.isDesktop() && (this.canSeeTeam() || this.isEmployeeScoped()),
  );

  protected readonly isSingleEmployee = computed(() => this.columnEmployees().length === 1);

  /**
   * Salon jednoosobowy — cała lista pracowników salonu to jedna osoba (a nie tylko bieżący widok).
   * Wtedy nazwa pracownika w drawerze wizyty jest zawsze ta sama, więc ją chowamy. Dla pracownika
   * scoped (widzi tylko siebie) też = 1, co jest w porządku: wszystkie jego wizyty są jego.
   */
  protected readonly isSoloSalon = computed(() => (this.employees.value()?.length ?? 0) === 1);

  /**
   * Czy podpowiadać karcie „Zacznij tutaj" przewodniki o zespole. Świadomie NIE `!isSoloSalon()`:
   * przed pobraniem listy `employees.value()` jest `undefined`, co dałoby „zespół jest" i mignięcie
   * pozycji, która zaraz znika. Fail-closed — dopóki nie wiemy, nie podpowiadamy.
   */
  protected readonly hasTeamForGuides = computed(() => (this.employees.value()?.length ?? 0) > 1);

  /**
   * Split-view desktop: lewy panel z kalendarzem + prawy sticky panel ze szczegółami wizyty.
   * Aktywny tylko gdy nie używamy multi-column team view — wtedy szerokość ekranu jest zużyta
   * na kolumny pracowników, dodanie 360px prawego panelu psułoby układ. Dla single-col (SOLO
   * lub filter na 1 pracownika) prawy panel mieści się komfortowo.
   */
  protected readonly enableSplitDetailLayout = computed(() => !this.showDesktopColumns());

  private readonly currentEmployeeId = computed(() => this.auth.currentEmployeeId());

  /** Id pracownika z URL (`/admin/schedule/:employeeId`). */
  private employeeIdFromRoute = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('employeeId'))),
    { initialValue: null }
  );

  private readonly openNewParam = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('new'))),
    { initialValue: null }
  );

  /** Deep-link do szczegółów wizyty: `?appointment=<id>` → otwórz drawer w kalendarzu na jej dniu. */
  private readonly appointmentParam = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('appointment'))),
    { initialValue: null }
  );

  /**
   * `reveal=0` — otwórz panel wizyty, ale NIE ruszaj kalendarza. Ustawiane przez powiadomienia
   * „wizyta do potwierdzenia": personel chce ją potwierdzić, a nie zmieniać oglądany dzień;
   * po zatwierdzeniu zostaje tam, gdzie był. Brak paramu = przeskok (deep-linki spoza
   * powiadomień, np. z profilu klienta, nie mają kontekstu kalendarza do zachowania).
   */
  private readonly deepLinkRevealParam = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('reveal') !== '0')),
    { initialValue: true }
  );

  /** Deep-link „Umów wizytę" z profilu klienta (`/admin/schedule?customerId=...`) — otwiera drawer
   *  tworzenia wizyty z tym klientem już wybranym (tryb listy). */
  private readonly openForCustomerParam = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('customerId'))),
    { initialValue: null }
  );

  /** Wyższa oś czasu — wizyty zajmą więcej pionowej przestrzeni i są czytelniejsze. */
  readonly hourHeightPx = 140;

  /** Powierzchnia kafelka przerwy — chrome jak kafel wizyty, ale neutralny + dashed (to nie rezerwacja). */
  protected readonly breakTileSurfaceClasses =
    'border-dashed border-surface-300 dark:border-surface-600 bg-surface-100/85 dark:bg-surface-100/55';

  // ── Wspólny wygląd kafelków OSI (mobile single-col) ──────────────────────────
  // Jedno źródło „skorupy" dla wizyt i przerw — kształt, ramka, cień. Status niesie WYŁĄCZNIE
  // lewy pasek (jak w agendzie): neutralne tło, bez tła statusowego i bez pigułki.
  /** Kształt + ramka + cień kafelka osi (identyczny dla wizyty i przerwy). */
  protected readonly timelineTileShellClasses =
    'rounded-2xl border overflow-hidden shadow-[0_10px_22px_-16px_rgba(15,23,42,0.55)]';
  /** Neutralne tło kafelka osi — kolor statusu żyje na lewym pasku, nie na tle. */
  protected readonly timelineTileSurfaceClasses =
    'bg-white/95 dark:bg-surface-100/70 border-surface-200/80 dark:border-surface-200/55';
  /** Neutralny lewy pasek (przerwa / brak statusu). Wizyta używa accentBarClasses(status). */
  protected readonly neutralAccentBarClasses = 'bg-surface-400/70 dark:bg-surface-500/55';
  /** Twarde granice doby na osi (auto-dopasowanie mieści się w tym oknie). */
  readonly fullRangeStartHour = 6;
  readonly fullRangeEndHour = 24;

  /**
   * Stan kalendarza (data, widok, filtry) pochodzi z `CalendarStateService` — pojedyncze źródło
   * prawdy, synchronizowane dwukierunkowo z query params URL-a. Aliasy zachowują dotychczasowy
   * API komponentu (`this.viewMode()`, `this.viewMode.set(...)` itd.) bez zmian w call-sites.
   */
  protected readonly viewMode = this.calendarState.view;
  protected readonly selectedDate = this.calendarState.date;

  /** Złączone filtry — czytane przez `<app-calendar-filters>` jako obiekt; setter rozbija na pola. */
  protected readonly filters = computed<CalendarFiltersValue>(() => ({
    employeeIds: this.calendarState.employees(),
    statuses: this.calendarState.statuses(),
    searchQuery: this.calendarState.searchQuery(),
  }));

  selectedMonthAnchor = signal<Date>(this.startOfMonth(new Date()));

  /** Wizyta wybrana tap-em na kafelek → otwiera bottom sheet (mobile) lub drawer (desktop). */
  protected readonly selectedAppointment = signal<AppointmentPreviewDto | null>(null);

  /**
   * Pełne dane wizyty otwartej deep-linkiem — już je pobraliśmy, żeby w ogóle wiedzieć, którą
   * wizytę pokazać. Podajemy je sheetowi, żeby nie fetchował po raz drugi tego samego zasobu
   * (drugi round-trip = drugi przeskok layoutu tuż po otwarciu panelu).
   */
  protected readonly deepLinkDetail = signal<AppointmentDto | null>(null);

  /**
   * Dzień wybrany tap-em w widoku miesiąca → otwiera sheet z listą wizyt dnia. Z poziomu
   * sheet'a użytkownik wybiera wizytę, klika „Otwórz widok dnia" albo „Dodaj wizytę".
   */
  protected readonly previewedDay = signal<Date | null>(null);

  /** Drawery edycji dostępności dnia (urlop / godziny dnia), wchłonięte z podglądu miesiąca. */
  protected readonly availLeaveDrawerOpen = signal(false);
  protected readonly availSpecialDayDrawerOpen = signal(false);
  /** Data (YYYY-MM-DD) przekazywana do otwartego drawera dostępności. */
  protected readonly availActionDate = signal<string | null>(null);
  /** Pracownik, którego dostępność edytujemy z drawera (miesiąc jest single-employee). */
  protected readonly availTargetEmployeeId = computed(() => this.effectiveEmployeeId() ?? '');

  /** Cel reschedule (F3.1) — wizyta otwarta w `reschedule-appointment-dialog`. */
  protected readonly rescheduleTarget = signal<AppointmentPreviewDto | null>(null);

  /** Cel „Zmień usługę" — wizyta otwarta w `change-service-dialog`. */
  protected readonly changeServiceTarget = signal<AppointmentPreviewDto | null>(null);
  protected readonly createContext = signal<CreateAppointmentContext | null>(null);
  /** Kontekst edytora szybkiej przerwy (chip desktop). */
  protected readonly breakContext = signal<BreakEditorContext | null>(null);
  /** Mobilny arkusz „szybkiego dodawania" (FAB) — zakładki Wizyta/Przerwa. */
  protected readonly quickAddOpen = signal(false);

  /**
   * Układ widoku dnia (single-column): 'agenda' (chronologiczna lista wizyt) lub 'timeline' (oś czasu).
   * Domyślnie agenda — czytelniej na telefonie (brak pustych godzin, duże tap-targety). Dotyczy tylko
   * widoku single-column; kolumny zespołu (desktop) zawsze używają osi.
   */
  protected readonly dayView = signal<'agenda' | 'timeline'>('agenda');

  /**
   * Karta „Pracownik" (mobile) ma sens tylko przy zespole (wybór pracownika) albo gdy salon nie ma
   * pracowników (podpowiedź „dodaj zespół"). W trybie solo (1 pracownik) oraz dla zalogowanego
   * pracownika (zna siebie) jest ukryta — zbędny pion nad kalendarzem.
   */
  /** Salon bez ani jednego pracownika — podpowiedź „dodaj zespół" zamiast pustego kalendarza. */
  protected readonly showEmptyTeamHint = computed(() => {
    if (this.showDesktopColumns() || this.isEmployeeScoped()) return false;
    return this.employees.value()?.length === 0;
  });

  /** Tryb zamiany terminów: tap w wizytę wybiera ją zamiast otwierać szczegóły. */
  protected readonly swapMode = signal(false);
  /** Pierwsza wybrana wizyta w trybie zamiany (druga otwiera dialog). */
  protected readonly swapFirst = signal<AppointmentPreviewDto | null>(null);
  /** Para wizyt do zamiany — niepuste otwiera `swap-appointments-dialog`. */
  protected readonly swapPair = signal<{ first: AppointmentPreviewDto; second: AppointmentPreviewDto } | null>(null);

  protected readonly isSelectedAppointmentUpdating = computed(() => {
    const id = this.selectedAppointment()?.id;
    return !!id && this.appointmentUpdates()[id] === true;
  });

  /**
   * Sygnał-tykawka odświeżany co 30 s przez efekt w konstruktorze. Czyni `nowLineTopPx`
   * zależnym od czasu — bez tego computed cachowałby pierwsze odczytanie `new Date()` i
   * zielona linia "teraz" nie poruszałaby się aż do zmiany innej zależności.
   */
  private nowTick = signal(Date.now());

  employees = rxResource({
    stream: () => this.employeesClient.getEmployees(),
  });

  /**
   * Ustawienia tenanta — czytane przez WSZYSTKIE role, w tym Employee (od F2.3). Endpoint
   * `GET /api/SalonSettings` jest open dla GeneralAccess; pole `staffCalendarVisibilityPolicy`
   * pozwala UI dostosować widoczność/akcje kalendarza dla pracownika.
   */
  salonSettings = rxResource({
    stream: () => this.salonSettingsClient.get(),
  });

  /** Minimalna wysokość bloku wizyty na osi — zgodna z interwałem slotów z ustawień salonu. */
  appointmentSlotStepMinutes = computed(() => {
    const v = this.salonSettings.value()?.appointmentSlotStepMinutes;
    return v === 5 || v === 10 || v === 15 || v === 30 ? v : 15;
  });

  effectiveEmployeeId = computed(() => {
    if (this.isEmployeeScoped()) {
      const own = this.currentEmployeeId();
      if (own) return own;
    }
    const list = this.employees.value() ?? [];
    const routeId = this.employeeIdFromRoute();
    if (routeId && list.some((e) => e.id === routeId)) return routeId;
    return list[0]?.id;
  });

  /**
   * Pracownicy renderowani jako kolumny w widoku desktop. Kto widzi zespół — wszyscy;
   * pracownik scoped (bez team-policy) — tylko własna kolumna, więc kolumnowy układ nie
   * ujawnia współpracowników ani nie odpala 403 na ich grafikach.
   */
  protected readonly columnEmployees = computed(() => {
    const all = this.employees.value() ?? [];
    if (this.canSeeTeam()) return all;
    const ownId = this.effectiveEmployeeId();
    return all.filter((e) => e.id === ownId);
  });

  /**
   * Czy możemy pobrać KONFIGURACJĘ grafiku danego pracownika (`/employee-schedules`
   * `/schedule-overrides` `/leaves`). Odzwierciedla backendowy `CanReadEmployeeScheduleConfigAsync`:
   * self, owner/manager, kiosk, albo pracownik w salonie z widocznością zespołu. Bez cudzej
   * konfiguracji kalendarz kolegi pokazywał „Dzień wolny" i pozwalał umówić wizytę na czyjś urlop.
   *
   * FAIL-CLOSED do czasu hydratacji sesji. `currentRole()` zwraca dziś `null`, gdy sesja jeszcze
   * nie wróciła z `/api/auth/me`, więc `canSeeTeam()` i tak jest false — ten warunek zostaje jako
   * jawna intencja (kiedyś rola domyślała na `'owner'` i pracownik strzelał po cudze grafiki).
   */
  private canFetchScheduleConfigFor(id: string | undefined): boolean {
    if (!id) return false;
    if (!this.auth.isHydrated()) return false;
    if (id === this.currentEmployeeId()) return true;
    return this.canSeeTeam();
  }

  /** Pracownicy, dla których pobieramy konfigurację grafiku w widoku kolumn (wg uprawnień). */
  protected readonly scheduleConfigEmployeeIds = computed<string[]>(() =>
    this.columnEmployees()
      .map((e) => e.id!)
      .filter((id) => this.canFetchScheduleConfigFor(id)),
  );

  constructor() {
    effect(() => {
      const d = this.selectedDate();
      this.selectedMonthAnchor.set(this.startOfMonth(d));
    });

    effect(() => {
      const emps = this.employees.value();
      const routeId = this.employeeIdFromRoute();
      if (emps == null || emps.length === 0) return;
      const valid = routeId != null && emps.some((e) => e.id === routeId);

      if (valid) {
        // Wybór z URL jest autorytatywny — utrwalamy go, żeby przeżył wyjście do ustawień
        // i powrót przez goły link „Kalendarz" (`/admin/schedule`, bez `:employeeId`).
        untracked(() => this.rememberEmployee(routeId!));
        return;
      }

      // Brak/nieaktualny `:employeeId` → wracamy do ostatnio oglądanego pracownika.
      // Zapamiętany id to tylko podpowiedź: mógł zostać zdeaktywowany albo pochodzić z innego
      // salonu (sesja wsparcia), więc musi wciąż być na liście — inaczej pierwszy z listy.
      const remembered = untracked(() => this.rememberedEmployeeId());
      const target = remembered != null && emps.some((e) => e.id === remembered)
        ? remembered
        : emps[0].id!;

      untracked(() => {
        // `preserve` żeby deep-link `?date=...&view=...` przeżył wymuszony redirect na
        // ścieżkę z konkretnym employeeId — bez tego URL sync zaczynałby od pustych params.
        void this.router.navigate(['/admin', 'schedule', target], {
          replaceUrl: true,
          queryParamsHandling: 'preserve',
        });
      });
    });

    effect(() => {
      // Reaktywne centrowanie wybranego dnia po zmianie miesiąca/daty/renderu listy.
      this.selectedDate();
      this.visibleDays();
      queueMicrotask(() => this.centerSelectedDayInSlider());
    });

    /**
     * Rezerwacje z panelu www nie mają pushu do dashboardu — okresowe `reload()` + powrót na kartę
     * odświeżają listę bez pełnego przeładowania strony (status `reloading` zostawia stary widok).
     */
    effect((onCleanup) => {
      const pollMs = 8000;
      const reloadIfVisible = (): void => {
        if (document.visibilityState !== 'visible') return;
        void this.appointments.reload();
      };
      const onVis = (): void => {
        if (document.visibilityState === 'visible') {
          reloadIfVisible();
        }
      };
      document.addEventListener('visibilitychange', onVis);
      const id = window.setInterval(reloadIfVisible, pollMs);
      onCleanup(() => {
        document.removeEventListener('visibilitychange', onVis);
        window.clearInterval(id);
      });
    });

    /**
     * Aktualizuje sygnał `nowTick` co 30 s, żeby zielona linia "teraz" przesuwała się
     * razem z czasem. Bez tego `nowLineTopPx` byłby zacachowany na momencie pierwszego
     * odczytu `new Date()` i mógł wyglądać jak "stoi" lub mieć przesunięcie o czas
     * spędzony z otwartą kartą.
     */
    effect((onCleanup) => {
      const tickMs = 30000;
      const tick = (): void => {
        if (document.visibilityState !== 'visible') return;
        this.nowTick.set(Date.now());
      };
      const onVis = (): void => {
        if (document.visibilityState === 'visible') tick();
      };
      document.addEventListener('visibilitychange', onVis);
      const id = window.setInterval(tick, tickMs);
      onCleanup(() => {
        document.removeEventListener('visibilitychange', onVis);
        window.clearInterval(id);
      });
    });

    effect(() => {
      if (this.openNewParam() !== '1') return;
      if (!this.employeeIdFromRoute()) return;
      if (!this.employees.hasValue()) return;
      untracked(() => {
        void this.router.navigate([], { replaceUrl: true, queryParams: { new: null }, queryParamsHandling: 'merge' });
        this.openCreateDrawer();
      });
    });

    effect(() => {
      const id = this.appointmentParam();
      // Param znika (po obsłużeniu albo zwykła nawigacja) → reset guarda, by ten sam id dało
      // się otworzyć ponownie (np. powtórne wejście z profilu klienta).
      if (!id) {
        this.deepLinkOpenedFor = null;
        return;
      }
      // Czekamy na STABILNĄ trasę `/admin/schedule/:employeeId`. Goła `/admin/schedule` (deep-link
      // z powiadomienia / profilu klienta) jest natychmiast przekierowywana na trasę z employeeId,
      // co niszczy i odtwarza komponent — bez tego guarda fetch leciałby na znikającej instancji,
      // a `selectedAppointment`/`selectedDate` ustawiałyby się na komponencie, którego już nie widać.
      if (!this.employeeIdFromRoute()) return;
      // Czekamy na listę pracowników — `openAppointmentFromDeepLink` decyduje o przełączeniu kolumny.
      if (!this.employees.hasValue()) return;
      // Guard: param `appointment` bywa re-emitowany (redirect pracownika / sync URL) zanim async
      // fetch zdąży go wyczyścić — bez tego byłby podwójny getAppointmentById dla tego samego id.
      if (this.deepLinkOpenedFor === id) return;
      this.deepLinkOpenedFor = id;
      untracked(() => this.openAppointmentFromDeepLink(id));
    });

    // Klik w dzwonku przy zamontowanym kalendarzu — z pominięciem routera. Bez tego kanału
    // powiadomienie linkowało na gołą `/admin/schedule`, co wymuszało redirect na trasę
    // z employeeId, a ten NISZCZY i odtwarza komponent: dwa montowania, ~46 requestów i
    // migające URL-e, żeby otworzyć panel wymagający jednego fetcha.
    effect(() => {
      const req = this.appointmentFocus.requested();
      if (!req) return;
      if (!this.employees.hasValue()) return;
      untracked(() => {
        this.appointmentFocus.clear();
        this.openAppointmentById(req.appointmentId, req.reveal, false);
      });
    });

    // Deep-link z profilu klienta: otwórz drawer z klientem wybranym z listy. Czekamy na STABILNĄ
    // trasę `/admin/schedule/:employeeId` — goła `/admin/schedule?customerId=...` jest najpierw
    // przekierowywana (z `preserve`) na trasę z employeeId, co niszczy i odtwarza komponent; bez
    // tego guarda efekt odpaliłby na znikającej instancji i wyczyściłby param przed redirectem.
    effect(() => {
      const customerId = this.openForCustomerParam();
      if (!customerId) return;
      if (!this.employeeIdFromRoute()) return;
      if (!this.employees.hasValue()) return;
      untracked(() => {
        void this.router.navigate([], { replaceUrl: true, queryParams: { customerId: null }, queryParamsHandling: 'merge' });
        this.openCreateDrawer({ customerId, customerMode: 'list' });
      });
    });
  }

  /** Ostatni id wizyty otwartej z deep-linka — zapobiega podwójnemu fetchowi przy re-emisji paramu. */
  private deepLinkOpenedFor: string | null = null;

  visibleDays = computed(() => {
    const monthStart = this.selectedMonthAnchor();
    const start = new Date(monthStart);
    start.setDate(1 - 3); // lekki bufor przed początkiem miesiąca
    const end = new Date(monthStart.getFullYear(), monthStart.getMonth() + 1, 0);
    end.setDate(end.getDate() + 3); // lekki bufor po końcu miesiąca
    const out: Date[] = [];
    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
      out.push(this.startOfDay(d));
    }
    return out;
  });

  weeklySchedule = rxResource({
    /**
     * Tylko gdy mamy prawo do konfiguracji grafiku tego pracownika — inaczej (pracownik
     * w widoku zespołu, gdzie `effectiveEmployeeId` to inny pracownik) pomijamy fetch, by nie
     * wywołać 403. Bez `params` zasób ładuje się raz — przy braku listy zostaje pusty na stałe.
     */
    params: () => {
      // Na desktopie ten sam pracownik jest już pobierany przez zasób kolumnowy
      // (`scheduleConfigEmployeeIds` zawsze zawiera `effectiveEmployeeId`), więc bez tej bramki
      // każdy z endpointów grafiku leciał dwa razy.
      if (this.showDesktopColumns()) return undefined;
      const id = this.effectiveEmployeeId();
      return this.canFetchScheduleConfigFor(id) ? id : undefined;
    },
    stream: ({ params: id }) => {
      if (!id) return of(undefined);
      return this.employeesClient
        .getEmployeeSchedules(id)
        .pipe(catchError(() => of(undefined)));
    },
  });

  desktopWeeklySchedules = rxResource({
    params: () => {
      if (!this.showDesktopColumns()) return undefined;
      const ids = this.scheduleConfigEmployeeIds();
      if (!ids.length) return undefined;
      return ids.join('\x1e');
    },
    defaultValue: {} as Record<string, EmployeeScheduleDto[] | undefined>,
    stream: ({ params }) => {
      if (!params) return of({} as Record<string, EmployeeScheduleDto[] | undefined>);
      const ids: string[] = String(params).split('\x1e').filter(Boolean);
      if (!ids.length) return of({} as Record<string, EmployeeScheduleDto[] | undefined>);
      return forkJoin(
        ids.map((id) =>
          this.employeesClient
            .getEmployeeSchedules(id)
            .pipe(catchError(() => of(undefined as EmployeeScheduleDto[] | undefined)))
        )
      ).pipe(
        map((items: Array<EmployeeScheduleDto[] | undefined>) => {
          const out: Record<string, EmployeeScheduleDto[] | undefined> = {};
          ids.forEach((id, i) => {
            out[id] = items[i];
          });
          return out;
        })
      );
    },
  });

  /**
   * Publikacje miesięcy wybranego pracownika — sterują tym, od kiedy KLIENCI widzą terminy
   * w danym miesiącu. Bez odpowiednika desktopowego: pasek stanu pokazujemy tylko tam, gdzie
   * na ekranie jest dokładnie jeden pracownik.
   */
  monthPublications = rxResource({
    params: () => {
      const id = this.effectiveEmployeeId();
      return this.canFetchScheduleConfigFor(id) ? id : undefined;
    },
    defaultValue: [] as MonthPublicationDto[],
    stream: ({ params: id }) => {
      if (!id) return of([] as MonthPublicationDto[]);
      return this.employeesClient
        .getMonthPublications(id)
        .pipe(catchError(() => of([] as MonthPublicationDto[])));
    },
  });

  protected readonly browsedYear = computed(() => this.selectedMonthAnchor().getFullYear());
  protected readonly browsedMonth = computed(() => this.selectedMonthAnchor().getMonth() + 1);

  protected readonly browsedMonthPublication = computed<MonthPublicationDto | null>(
    () =>
      this.monthPublications
        .value()
        .find((p) => p.year === this.browsedYear() && p.month === this.browsedMonth()) ?? null
  );

  protected readonly browsedMonthHasPublication = computed(
    () => this.browsedMonthPublication() !== null
  );

  /** `opensOn` jako `YYYY-MM-DD` w czasie LOKALNYM — `toISOString()` cofnąłby dzień przed północą. */
  protected readonly browsedMonthPublicationOpensOn = computed<string | null>(() => {
    const raw = this.browsedMonthPublication()?.opensOn;
    if (!raw) return null;
    const d = new Date(raw);
    if (Number.isNaN(d.getTime())) return null;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  });

  protected readonly monthPublicationDrawerOpen = signal(false);

  protected openMonthPublicationDrawer(): void {
    if (!this.canEditPreviewedDayAvailability()) return;
    this.monthPublicationDrawerOpen.set(true);
  }

  protected onMonthPublicationSaved(): void {
    this.monthPublicationDrawerOpen.set(false);
    this.monthPublications.reload();
  }

  /** Dni specjalne (nadpisania grafiku) wybranego pracownika — widok single-col. */
  scheduleOverrides = rxResource({
    params: () => {
      // Na desktopie ten sam pracownik jest już pobierany przez zasób kolumnowy
      // (`scheduleConfigEmployeeIds` zawsze zawiera `effectiveEmployeeId`), więc bez tej bramki
      // każdy z endpointów grafiku leciał dwa razy.
      if (this.showDesktopColumns()) return undefined;
      const id = this.effectiveEmployeeId();
      return this.canFetchScheduleConfigFor(id) ? id : undefined;
    },
    defaultValue: [] as ScheduleOverrideDto[],
    stream: ({ params: id }) => {
      if (!id) return of([] as ScheduleOverrideDto[]);
      return this.employeesClient
        .getScheduleOverrides(id)
        .pipe(catchError(() => of([] as ScheduleOverrideDto[])));
    },
  });

  /** Dni specjalne wszystkich pracowników — widok desktop (kolumny). */
  desktopScheduleOverrides = rxResource({
    params: () => {
      if (!this.showDesktopColumns()) return undefined;
      const ids = this.scheduleConfigEmployeeIds();
      if (!ids.length) return undefined;
      return ids.join('\x1e');
    },
    defaultValue: {} as Record<string, ScheduleOverrideDto[] | undefined>,
    stream: ({ params }) => {
      if (!params) return of({} as Record<string, ScheduleOverrideDto[] | undefined>);
      const ids: string[] = String(params).split('\x1e').filter(Boolean);
      if (!ids.length) return of({} as Record<string, ScheduleOverrideDto[] | undefined>);
      return forkJoin(
        ids.map((id) =>
          this.employeesClient
            .getScheduleOverrides(id)
            .pipe(catchError(() => of(undefined as ScheduleOverrideDto[] | undefined)))
        )
      ).pipe(
        map((items: Array<ScheduleOverrideDto[] | undefined>) => {
          const out: Record<string, ScheduleOverrideDto[] | undefined> = {};
          ids.forEach((id, i) => {
            out[id] = items[i];
          });
          return out;
        })
      );
    },
  });

  /** Urlopy / nieobecności wybranego pracownika — widok single-col. */
  employeeLeaves = rxResource({
    params: () => {
      // Na desktopie ten sam pracownik jest już pobierany przez zasób kolumnowy
      // (`scheduleConfigEmployeeIds` zawsze zawiera `effectiveEmployeeId`), więc bez tej bramki
      // każdy z endpointów grafiku leciał dwa razy.
      if (this.showDesktopColumns()) return undefined;
      const id = this.effectiveEmployeeId();
      return this.canFetchScheduleConfigFor(id) ? id : undefined;
    },
    defaultValue: [] as EmployeeLeaveDto[],
    stream: ({ params: id }) => {
      if (!id) return of([] as EmployeeLeaveDto[]);
      return this.employeesClient
        .getEmployeeLeaves(id)
        .pipe(catchError(() => of([] as EmployeeLeaveDto[])));
    },
  });

  /** Urlopy / nieobecności wszystkich pracowników — widok desktop (kolumny). */
  desktopEmployeeLeaves = rxResource({
    params: () => {
      if (!this.showDesktopColumns()) return undefined;
      const ids = this.scheduleConfigEmployeeIds();
      if (!ids.length) return undefined;
      return ids.join('\x1e');
    },
    defaultValue: {} as Record<string, EmployeeLeaveDto[] | undefined>,
    stream: ({ params }) => {
      if (!params) return of({} as Record<string, EmployeeLeaveDto[] | undefined>);
      const ids: string[] = String(params).split('\x1e').filter(Boolean);
      if (!ids.length) return of({} as Record<string, EmployeeLeaveDto[] | undefined>);
      return forkJoin(
        ids.map((id) =>
          this.employeesClient
            .getEmployeeLeaves(id)
            .pipe(catchError(() => of(undefined as EmployeeLeaveDto[] | undefined)))
        )
      ).pipe(
        map((items: Array<EmployeeLeaveDto[] | undefined>) => {
          const out: Record<string, EmployeeLeaveDto[] | undefined> = {};
          ids.forEach((id, i) => {
            out[id] = items[i];
          });
          return out;
        })
      );
    },
  });

  appointments = rxResource({
    /**
     * Klucz żądania: data + pracownik. Bez tego `rxResource` nie powtarza strumienia, gdy
     * po pierwszym renderze pojawia się `effectiveEmployeeId` albo zmienia się dzień — wtedy
     * kalendarz zostaje pusty mimo wizyt w API.
     */
    params: () => {
      const mode = this.showDesktopColumns() ? 'desktop-all' : 'single';
      const empId = this.effectiveEmployeeId();
      const d = this.selectedDate();
      if (mode === 'single' && !empId) return undefined;
      return [mode, empId ?? '-', d.getFullYear(), d.getMonth(), d.getDate()].join('\x1e');
    },
    defaultValue: [] as AppointmentPreviewDto[],
    stream: () => {
      const empId = this.effectiveEmployeeId();
      const day = this.selectedDate();
      if (!this.showDesktopColumns() && !empId) return of([] as AppointmentPreviewDto[]);
      const dayStr = formatYyyyMmDd(day);
      /** Jak przy slotach: jawne `yyyy-MM-dd` w query — wygenerowany klient wysyła `toISOString()`, co bywa 400 dla `DateOnly`. */
      return this.http.get<AppointmentPreviewDto[]>(`${this.apiBaseUrl}/api/Appointments`, {
        params: {
          startDate: dayStr,
          endDate: dayStr,
          ...(this.showDesktopColumns() ? {} : { employeeId: empId }),
        },
      });
    },
  });

  hourLabels = computed(() => {
    const a = this.rangeStartHour();
    const b = this.rangeEndHour();
    const hrs: number[] = [];
    for (let h = a; h < b; h++) hrs.push(h);
    return hrs;
  });

  timelineHeightPx = computed(() => {
    const hours = this.rangeEndHour() - this.rangeStartHour();
    return hours * this.hourHeightPx + 16;
  });

  dayAppointments = computed(() => {
    if (this.appointments.error()) {
      return [] as AppointmentPreviewDto[];
    }
    const day = this.selectedDate();
    // Filtry (statusy/pracownicy/fraza) wspólne dla wszystkich widoków; tu doklejamy filtr
    // po wybranym dniu — backend bywa hojny i odda też sąsiednie dni.
    return filterAppointments(this.appointments.value(), this.filters()).filter((a) => {
      const ad = appointmentDay(a.date as Date);
      return ad != null && sameCalendarDay(ad, day);
    });
  });

  // ── Szybka przerwa (chip desktop / zakładka FAB mobile) ───────────────────

  /** Pracownik, którego dotyczy edytor przerwy (z kontekstu edycji, inaczej oglądany). */
  private readonly breakEditorEmployeeId = computed(
    () => this.breakContext()?.employeeId ?? this.effectiveEmployeeId(),
  );

  /** Grafik/dni specjalne/urlopy dla pracownika edytora — single (mobile) lub z rekordów desktop. */
  protected readonly breakEditorSchedules = computed<EmployeeScheduleDto[] | undefined>(() => {
    const id = this.breakEditorEmployeeId();
    if (!id) return undefined;
    if (this.showDesktopColumns()) return this.desktopWeeklySchedules.value()?.[id];
    return this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined;
  });

  protected readonly breakEditorOverrides = computed<ScheduleOverrideDto[] | undefined>(() => {
    const id = this.breakEditorEmployeeId();
    if (!id) return undefined;
    return this.showDesktopColumns()
      ? this.desktopScheduleOverrides.value()?.[id]
      : this.scheduleOverrides.value();
  });

  protected readonly breakEditorLeaves = computed<EmployeeLeaveDto[] | undefined>(() => {
    const id = this.breakEditorEmployeeId();
    if (!id) return undefined;
    return this.showDesktopColumns()
      ? this.desktopEmployeeLeaves.value()?.[id]
      : this.employeeLeaves.value();
  });

  /**
   * Wizyty pracownika edytora w wybranym dniu — do kontroli kolizji przy dodawaniu/edycji przerwy.
   * BEZ filtrów UI (status/fraza): ukryta statusem wizyta wciąż blokuje termin.
   */
  protected readonly breakDayAppointments = computed(() => {
    const emp = this.breakEditorEmployeeId();
    const day = this.selectedDate();
    return (this.appointments.value() ?? []).filter((a) => {
      if (emp && a.employeeId && a.employeeId !== emp) return false;
      const ad = appointmentDay(a.date as Date);
      return ad != null && sameCalendarDay(ad, day);
    });
  });

  /** Kontekst pojedynczego pracownika — przerwa dotyczy jednego grafiku, nie kolumn zespołu. */
  private readonly isSingleEmployeeContext = computed(
    () => !this.showDesktopColumns() || this.isSingleEmployee(),
  );

  /** Wybrany dzień jako „yyyy-MM-dd" (dla kontekstów edytorów). */
  protected readonly selectedDateYmd = computed(() => formatYyyyMmDd(this.selectedDate()));

  /** Surowy dzień grafiku oglądanego pracownika dla wybranej daty (pasy „jak zapisane"). */
  private readonly selectedDayRawSchedule = computed(() =>
    resolveRawScheduleDayForDate(
      this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined,
      this.scheduleOverrides.value(),
      this.employeeLeaves.value(),
      this.selectedDate(),
    ),
  );

  /** Czy można dodać szybką przerwę: pojedynczy pracownik, grafik dynamiczny z pasem, dzień nie miniony. */
  protected readonly canAddBreakForSelectedDay = computed(() => {
    if (this.selectedDayIsPast()) return false;
    if (!this.effectiveEmployeeId()) return false;
    if (!this.isSingleEmployeeContext()) return false;
    return canAddGridBreak(this.selectedDayRawSchedule());
  });

  /** Tekst tooltipa, gdy chip przerwy jest nieaktywny (pusty → aktywny). */
  protected readonly breakDisabledReason = computed(() => {
    if (this.canAddBreakForSelectedDay()) return '';
    if (this.selectedDayIsPast()) return 'Nie można dodać przerwy w przeszłości.';
    const raw = this.selectedDayRawSchedule();
    if (!raw) return 'Brak grafiku dla tego dnia (lub urlop) — ustaw godziny w grafiku.';
    if (raw.mode === SlotGenerationMode.FixedStartTimes) {
      return 'Przerwy dostępne tylko dla grafiku dynamicznego. W grafiku ze stałymi godzinami zablokuj slot w ustawieniach grafiku.';
    }
    return 'Brak pasa pracy w tym dniu — ustaw godziny w grafiku.';
  });

  /**
   * Czy w drawerze wizyty pokazać „Dodaj przerwę po wizycie": można dodawać przerwy (grafik
   * dynamiczny, pojedynczy pracownik, dzień nie miniony), koniec wizyty leży w pasie pracy z
   * miejscem po nim, a zaraz za wizytą nie ma innej (nieanulowanej) wizyty (jest wolny czas).
   */
  protected readonly canAddBreakAfterSelected = computed(() => {
    if (!this.canAddBreakForSelectedDay()) return false;
    const a = this.selectedAppointment();
    if (!a?.endTime) return false;
    const emp = this.effectiveEmployeeId();
    if (a.employeeId && emp && a.employeeId !== emp) return false;
    const end = parseTimeToMinutes(a.endTime);
    const range = this.currentDaySingleRanges().find((r) => r.startMin <= end && end < r.endMin);
    if (!range) return false;
    const occupied = this.breakDayAppointments().some(
      (x) =>
        x.status?.id !== 5 &&
        parseTimeToMinutes(x.startTime) <= end &&
        end < parseTimeToMinutes(x.endTime),
    );
    return !occupied;
  });

  /**
   * Wizyty dla dnia wybranego w widoku miesiąca (F2.4) — źródło `monthAppointments` (cały
   * miesiąc fetchowany w view='month'), filtrowane przez aktywne filtry kalendarza.
   */
  protected readonly previewedDayAppointments = computed(() => {
    const day = this.previewedDay();
    if (!day) return [] as AppointmentPreviewDto[];
    return filterAppointments(this.monthAppointments.value(), this.filters()).filter((a) => {
      const ad = appointmentDay(a.date as Date);
      return ad != null && sameCalendarDay(ad, day);
    });
  });

  /** Dostępność dnia podglądanego w sheecie (grafik/urlop/dzień specjalny oglądanego pracownika). */
  protected readonly previewedDayAvailability = computed<DayAvailability | null>(() => {
    const day = this.previewedDay();
    if (!day) return null;
    const cfg = this.monthScheduleConfig();
    if (!cfg) return null;
    return resolveDayAvailability(cfg.sched, cfg.overrides, cfg.leaves, day);
  });

  /** Czy w sheecie pokazać akcje edycji dostępności — self lub zarządzanie personelem (jak backend). */
  protected readonly canEditPreviewedDayAvailability = computed(() => {
    const empId = this.effectiveEmployeeId();
    if (!empId) return false;
    return this.canMutateOthers() || empId === this.currentEmployeeId();
  });

  /**
   * Lista pracowników do MultiSelect filtrów. Pomija samego siebie w widoku employee/kiosk.
   */
  filterEmployees = computed(() => {
    const list = this.employees.value() ?? [];
    return list.map((e) => ({
      id: e.id ?? '',
      label: [e.firstName, e.lastName].filter((s): s is string => !!s).join(' ') || 'Pracownik',
    }));
  });

  /**
   * Wizyty z całego miesiąca dla widoku miesięcznego — fetch ograniczony do view='month',
   * żeby nie obciążać API gdy oglądamy dzień.
   */
  monthAppointments = rxResource({
    params: () => {
      if (this.viewMode() !== 'month') return undefined;
      const m = this.selectedMonthAnchor();
      // Miesiąc jest single-employee (z przełącznikiem) — empId w kluczu, by zmiana pracownika przeładowała.
      return `${m.getFullYear()}-${m.getMonth()}:${this.effectiveEmployeeId() ?? '-'}`;
    },
    defaultValue: [] as AppointmentPreviewDto[],
    stream: () => {
      if (this.viewMode() !== 'month') return of([] as AppointmentPreviewDto[]);
      const m = this.selectedMonthAnchor();
      const start = new Date(m.getFullYear(), m.getMonth(), 1);
      const end = new Date(m.getFullYear(), m.getMonth() + 1, 0);
      const startStr = formatYyyyMmDd(start);
      const endStr = formatYyyyMmDd(end);
      const empId = this.effectiveEmployeeId();
      return this.http
        .get<AppointmentPreviewDto[]>(`${this.apiBaseUrl}/api/Appointments`, {
          params: {
            startDate: startStr,
            endDate: endStr,
            ...(empId ? { employeeId: empId } : {}),
          },
        })
        .pipe(catchError(() => of([] as AppointmentPreviewDto[])));
    },
  });

  /**
   * Wizyty obejmujące zakres paska dni (F2.5) — używane wyłącznie do badge'a liczby wizyt
   * na kafelkach pasku w widoku 'day'. Pobierane raz na miesiąc (bufor ±3 dni) niezależnie
   * od `appointments` (pojedynczy dzień) — `appointments` zostaje cienkim zapytaniem,
   * a pasek dostaje pełen comiesięczny widok.
   */
  dayStripAppointments = rxResource({
    params: () => {
      if (this.viewMode() !== 'day') return undefined;
      const days = this.visibleDays();
      if (!days.length) return undefined;
      const start = days[0];
      const end = days[days.length - 1];
      const empId = this.effectiveEmployeeId();
      return [
        formatYyyyMmDd(start),
        formatYyyyMmDd(end),
        this.showDesktopColumns() ? '*' : (empId ?? '-'),
      ].join('\x1e');
    },
    defaultValue: [] as AppointmentPreviewDto[],
    stream: () => {
      if (this.viewMode() !== 'day') return of([] as AppointmentPreviewDto[]);
      const days = this.visibleDays();
      if (!days.length) return of([] as AppointmentPreviewDto[]);
      const startStr = formatYyyyMmDd(days[0]);
      const endStr = formatYyyyMmDd(days[days.length - 1]);
      const empId = this.effectiveEmployeeId();
      return this.http
        .get<AppointmentPreviewDto[]>(`${this.apiBaseUrl}/api/Appointments`, {
          params: {
            startDate: startStr,
            endDate: endStr,
            ...(this.showDesktopColumns() ? {} : empId ? { employeeId: empId } : {}),
          },
        })
        .pipe(catchError(() => of([] as AppointmentPreviewDto[])));
    },
  });

  /**
   * Mapa `yyyy-mm-dd` → { total, pending } z `dayStripAppointments` (już z filtrami kalendarza).
   * Używane przez kafelki paska dni do wyświetlenia badge'a liczby wizyt.
   */
  protected readonly dayStripCounts = computed(() => {
    const map = new Map<string, { total: number; pending: number }>();
    const filtered = filterAppointments(this.dayStripAppointments.value(), this.filters());
    for (const a of filtered) {
      const ad = appointmentDay(a.date as Date);
      if (!ad) continue;
      const key = formatYyyyMmDd(ad);
      const slot = map.get(key) ?? { total: 0, pending: 0 };
      slot.total += 1;
      if (statusVariantFromPreview(a) === 'pending') slot.pending += 1;
      map.set(key, slot);
    }
    return map;
  });

  protected dayStripCountFor(day: Date): { total: number; pending: number } {
    return this.dayStripCounts().get(formatYyyyMmDd(day)) ?? { total: 0, pending: 0 };
  }

  /**
   * Rezerwacje w oknie „dziś → +7 dni" — źródło baneru „do potwierdzenia" nad kalendarzem.
   * Niezależne od widoku (day/week/month) i aktywnych filtrów, bo pending to głównie rezerwacje
   * online czekające na akcję właściciela — nie mogą zniknąć przy zmianie widoku.
   * Baner widzi KAŻDY, kto ma własną wizytę do potwierdzenia. Zakres: wyłącznie własne wizyty
   * (`employeeId`), spójnie z osobistym dzwonkiem — wcześniej liczył pending całego salonu i
   * pokazywał „2", gdy własna była jedna. Recepcja (kiosk) nie ma kalendarza → cały salon.
   * Odświeżane po `quickConfirm`/`quickCancel` (jedyne miejsca, gdzie pending zmienia status).
   */
  pendingAppointments = rxResource({
    params: () => this.pendingScope(),
    defaultValue: [] as AppointmentPreviewDto[],
    stream: ({ params: scope }) => {
      if (!scope) return of([] as AppointmentPreviewDto[]);
      const start = this.startOfDay(new Date());
      const end = new Date(start);
      end.setDate(end.getDate() + 7);
      const params: Record<string, string> = {
        startDate: formatYyyyMmDd(start),
        endDate: formatYyyyMmDd(end),
      };
      if (scope !== DESK_PENDING_SCOPE) params['employeeId'] = scope;
      return this.http
        .get<AppointmentPreviewDto[]>(`${this.apiBaseUrl}/api/Appointments`, { params })
        .pipe(catchError(() => of([] as AppointmentPreviewDto[])));
    },
  });

  /**
   * Zakres banera „do potwierdzenia": własny `employeeId`, albo `DESK_PENDING_SCOPE` dla recepcji
   * (kiosk nie ma własnego kalendarza → liczy cały salon). `undefined` = sesja jeszcze nieznana
   * albo konto bez pracownika → nie pytamy. Fail-closed: przed hydratacją nie znamy ani roli
   * (`currentRole()` = null), ani `employeeId`.
   */
  private pendingScope(): string | undefined {
    if (!this.auth.isHydrated()) return undefined;
    if (this.auth.currentRole() === 'kiosk') return DESK_PENDING_SCOPE;
    return this.currentEmployeeId() ?? undefined;
  }

  protected readonly pendingCount = computed(
    () =>
      this.pendingAppointments
        .value()
        .filter((a) => statusVariantFromPreview(a) === 'pending').length,
  );

  /**
   * Klik w baner → otwórz drawer najbliższej wizyty oczekującej na potwierdzenie (zamiast
   * ustawiać filtr statusu 'pending'). Wybieramy najwcześniejszą po dacie + godzinie startu
   * i reużywamy `openAppointmentFromDeepLink` (skacze na dzień/pracownika i otwiera szczegóły).
   */
  protected goToPending(): void {
    const next = this.pendingAppointments
      .value()
      .filter((a) => a.id && statusVariantFromPreview(a) === 'pending')
      .sort((a, b) => {
        const da = a.date ? new Date(a.date).getTime() : 0;
        const db = b.date ? new Date(b.date).getTime() : 0;
        if (da !== db) return da - db;
        return (a.startTime ?? '').localeCompare(b.startTime ?? '');
      })[0];
    if (!next?.id) return;
    this.openAppointmentFromDeepLink(next.id);
  }

  /**
   * Wizyty tygodnia (Pn–Nd) — fetch ograniczony do view='week', niezależny od dziennego.
   */
  weekAppointments = rxResource({
    /**
     * Klucz żądania: początek tygodnia + tryb kolumn + pracownik. Bez `empId` w kluczu zmiana
     * pracownika nie przeładowywała tygodnia (widok dnia i miesiąca miały to od początku).
     */
    params: () => {
      if (this.viewMode() !== 'week') return undefined;
      const d = this.selectedDate();
      const monIdx = (d.getDay() + 6) % 7;
      const start = new Date(d);
      start.setDate(d.getDate() - monIdx);
      return appointmentsRequestKey(
        start.toISOString().slice(0, 10),
        this.showDesktopColumns(),
        this.effectiveEmployeeId(),
      );
    },
    defaultValue: [] as AppointmentPreviewDto[],
    stream: () => {
      if (this.viewMode() !== 'week') return of([] as AppointmentPreviewDto[]);
      const d = this.selectedDate();
      const monIdx = (d.getDay() + 6) % 7;
      const start = new Date(d);
      start.setDate(d.getDate() - monIdx);
      const end = new Date(start);
      end.setDate(start.getDate() + 6);
      const startStr = formatYyyyMmDd(start);
      const endStr = formatYyyyMmDd(end);
      const empId = this.effectiveEmployeeId();
      return this.http
        .get<AppointmentPreviewDto[]>(`${this.apiBaseUrl}/api/Appointments`, {
          params: {
            startDate: startStr,
            endDate: endStr,
            ...(this.showDesktopColumns() ? {} : empId ? { employeeId: empId } : {}),
          },
        })
        .pipe(catchError(() => of([] as AppointmentPreviewDto[])));
    },
  });

  onViewModeChange(mode: CalendarViewMode): void {
    this.viewMode.set(mode);
  }

  /** Rozbija obiekt filtrów na pojedyncze settery serwisu (każdy idempotentny). */
  onFiltersChange(value: CalendarFiltersValue): void {
    this.calendarState.setEmployees(value.employeeIds);
    this.calendarState.setStatuses(value.statuses as AppointmentStatusVariant[]);
    this.calendarState.setSearchQuery(value.searchQuery);
  }

  /**
   * Pasek wyboru pracownika nad kalendarzem. Pokazujemy tylko wtedy, gdy jest w czym wybierać:
   * użytkownik nie jest zawężony do własnego kalendarza (`isEmployeeScoped`) i zespół liczy
   * więcej niż jedną osobę.
   */
  protected readonly showEmployeeStrip = computed(
    () => !this.isEmployeeScoped() && this.filterEmployees().length > 1,
  );

  /**
   * Chip w pasku = „pokaż kalendarz tego pracownika". To NIE jest filtr — wybór pracownika i filtr
   * wizyt to dwie osobne rzeczy (kolumny na desktopie i tak zależą od `canSeeTeam`, nie od filtra).
   * Przy okazji czyścimy zapisany filtr pracownika: multiselect zniknął z paska filtrów, więc nie
   * ma już czym go zdjąć, a `CalendarStateService` trzyma go w localStorage między sesjami i
   * potrafiłby po cichu ukrywać wizyty.
   */
  protected onEmployeeStripSelect(id: string): void {
    if (this.filters().employeeIds.length > 0) {
      this.calendarState.setEmployees([]);
    }
    this.onEmployeeChange(id);
  }

  /** Który chip jest aktywny — zawsze aktualnie wyświetlany pracownik. */
  protected readonly stripSelectedId = computed<string | null>(
    () => this.effectiveEmployeeId() ?? null,
  );

  /**
   * Tap w kafelek miesiąca (lub kartę dnia w `week-agenda`) → otwiera sheet z listą wizyt
   * dnia (F2.4). Sheet zawiera akcje „Otwórz widok dnia" i „Dodaj wizytę"; tap konkretnej
   * wizyty z listy otwiera istniejący `appointment-detail-sheet`. Wybrana data idzie do
   * `selectedDate` od razu — sheet po zamknięciu nie reset-uje tego ustawienia, więc
   * jeden tap-zamknij-tap nie wymusza ponownego scrollowania.
   */
  onMonthCellClick(date: Date): void {
    const d = this.startOfDay(date);
    this.selectedDate.set(d);
    this.selectedMonthAnchor.set(this.startOfMonth(date));
    this.previewedDay.set(d);
  }

  /**
   * Tap w kartę dnia w widoku tygodnia/agendy — drill-down do widoku dziennego (timeline).
   * Sheet (F2.4) jest zarezerwowany dla miesiąca; w tygodniu użytkownik już ma zbity przegląd
   * i chce zobaczyć timeline dnia, więc nie wkładamy go w dodatkowy krok.
   */
  onWeekDayClick(date: Date): void {
    this.selectedDate.set(this.startOfDay(date));
    this.selectedMonthAnchor.set(this.startOfMonth(date));
    this.viewMode.set('day');
  }

  /** Sheet zamyka się i przechodzi w timeline dnia. */
  protected onPreviewedDayOpenDay(date: Date): void {
    this.previewedDay.set(null);
    this.selectedDate.set(this.startOfDay(date));
    this.selectedMonthAnchor.set(this.startOfMonth(date));
    this.viewMode.set('day');
  }

  /** Sheet zamyka się i przekierowuje na formularz nowej wizyty dla wybranego dnia. */
  protected onPreviewedDayAdd(date: Date): void {
    this.previewedDay.set(null);
    this.selectedDate.set(this.startOfDay(date));
    this.openCreateDrawer();
  }

  /** Sheet zamyka się; otwiera się `appointment-detail-sheet` dla wybranej wizyty. */
  protected onPreviewedDayPick(appointment: AppointmentPreviewDto): void {
    this.previewedDay.set(null);
    this.selectedAppointment.set(appointment);
  }

  /** Sheet zamyka się; otwiera drawer urlopu/chorobowego dla oglądanego pracownika i dnia. */
  protected onPreviewedDayAddLeave(date: Date): void {
    if (!this.canEditPreviewedDayAvailability()) return;
    this.availActionDate.set(formatYyyyMmDd(this.startOfDay(date)));
    this.previewedDay.set(null);
    this.availLeaveDrawerOpen.set(true);
  }

  /** Sheet zamyka się; otwiera drawer „godziny dnia" (dzień specjalny) dla oglądanego pracownika. */
  protected onPreviewedDaySetHours(date: Date): void {
    if (!this.canEditPreviewedDayAvailability()) return;
    this.availActionDate.set(formatYyyyMmDd(this.startOfDay(date)));
    this.previewedDay.set(null);
    this.availSpecialDayDrawerOpen.set(true);
  }

  /** „Ustaw godziny na ten dzień" z karty „Dzień wolny" (widok dnia) — dla aktualnie wybranej daty. */
  protected onSetSelectedDayHours(): void {
    this.onPreviewedDaySetHours(this.selectedDate());
  }

  /** Po zapisie w drawerze dostępności: zamknij i odśwież grafik/override'y/urlopy + wizyty miesiąca. */
  protected onAvailabilityDrawerSaved(): void {
    this.availLeaveDrawerOpen.set(false);
    this.availSpecialDayDrawerOpen.set(false);
    this.weeklySchedule.reload();
    this.scheduleOverrides.reload();
    this.employeeLeaves.reload();
    this.monthPublications.reload();
    this.monthAppointments.reload();
  }

  protected closePreviewedDay(): void {
    this.previewedDay.set(null);
  }

  shiftWeek(delta: number): void {
    const next = new Date(this.selectedDate());
    next.setDate(next.getDate() + delta * 7);
    this.selectedDate.set(this.startOfDay(next));
  }

  desktopColumns = computed(() => {
    if (!this.showDesktopColumns()) return [] as Array<{
      id: string;
      label: string;
      items: Array<{
        raw: AppointmentPreviewDto;
        startMin: number;
        endMin: number;
        statusVariant: AppointmentStatusVariant;
        compact: boolean;
        isOutsideSchedule: boolean;
        lane: number;
        laneCount: number;
      }>;
    }>;
    const emps = this.columnEmployees();
    const all = this.positionedAppointments();
    const segments = this.desktopWorkingSegments();
    const schedules = this.desktopWeeklySchedules.value() ?? {};
    const overrides = this.desktopScheduleOverrides.value() ?? {};
    const leaves = this.desktopEmployeeLeaves.value() ?? {};
    return emps.map((e) => {
      const empId = e.id!;
      const ranges = segments[empId] ?? [];
      const empSchedules = schedules[empId];
      const hasSchedule =
        !!this.findBlockingLeaveForDate(leaves[empId], this.selectedDate()) ||
        !!this.pickOverrideForDate(overrides[empId], this.selectedDate()) ||
        (!!empSchedules?.length && !!this.pickScheduleForDate(empSchedules, this.selectedDate()));
      // Tryb stały tego pracownika na ten dzień → brak pasów, kontrola out-of-range dawałaby fałszywy alarm.
      const dayIsFixed =
        resolveFixedStartTimesForDate(empSchedules, overrides[empId], leaves[empId], this.selectedDate()) !== null;
      // Lane'y liczone w obrębie kolumny pracownika — `positionedAppointments` miesza wszystkich,
      // więc globalny lane byłby błędny dla pojedynczej kolumny.
      const items = assignTimelineLanes(
        all.filter((x) => this.appointmentEmployeeId(x.raw) === empId),
      ).map((x) => ({
        ...x,
        isOutsideSchedule: dayIsFixed
          ? false
          : this.isAppointmentOutsideRanges(x.startMin, x.endMin, ranges, hasSchedule),
      }));
      return {
        id: empId,
        label: [e.firstName, e.lastName].filter(Boolean).join(' ') || 'Pracownik',
        items,
      };
    });
  });

  desktopWorkingSegments = computed(() => {
    if (!this.showDesktopColumns()) return {} as Record<string, { startMin: number; endMin: number }[]>;
    const schedules = this.desktopWeeklySchedules.value() ?? {};
    const overrides = this.desktopScheduleOverrides.value() ?? {};
    const leaves = this.desktopEmployeeLeaves.value() ?? {};
    const day = this.selectedDate();
    const out: Record<string, { startMin: number; endMin: number }[]> = {};
    for (const [employeeId, employeeSchedules] of Object.entries(schedules)) {
      const ranges = this.resolveWorkingRangesForDate(
        employeeSchedules,
        overrides[employeeId],
        leaves[employeeId],
        day
      );
      out[employeeId] = ranges
        .map((r) => ({
          startMin: parseTimeToMinutes(r.startTime),
          endMin: Math.max(parseTimeToMinutes(r.endTime), parseTimeToMinutes(r.startTime) + 1),
        }))
        .filter((r) => r.endMin > r.startMin);
    }
    return out;
  });

  /**
   * Tytuł baneru "Twój dzień" dla widoku pracownika — liczy wizyty (bez anulowanych).
   */
  employeeBannerTitle = computed(() => {
    const stats = this.dayStats();
    return `Twój dzień: ${stats.total} ${this.declensionWizyt(stats.total)}`;
  });

  /**
   * Etykieta zmiany dla pracownika: zakres godzinowy + najbliższa przerwa, jeśli grafik dostępny.
   */
  employeeShiftLabel = computed(() => {
    const sched = this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined;
    const overrides = this.scheduleOverrides.value();
    const leaves = this.employeeLeaves.value();
    const d = this.selectedDate();
    const ranges = this.resolveWorkingRangesForDate(sched, overrides, leaves, d);
    if (!ranges?.length) return '';
    const startMin = Math.min(
      ...ranges.map((r) => parseTimeToMinutes(r.startTime)).filter((x) => x > 0),
    );
    const endMin = Math.max(
      ...ranges.map((r) => parseTimeToMinutes(r.endTime)).filter((x) => x > 0),
    );
    if (!Number.isFinite(startMin) || !Number.isFinite(endMin) || endMin <= startMin) {
      return '';
    }
    const breakLabel = this.nextBreakLabel();
    const base = `Zmiana ${formatHm(startMin)} – ${formatHm(endMin)}`;
    return breakLabel ? `${base} • ${breakLabel}` : base;
  });

  private nextBreakLabel(): string {
    const sched = this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined;
    const overrides = this.scheduleOverrides.value();
    const leaves = this.employeeLeaves.value();
    const d = this.selectedDate();
    const breaks = this.resolveBreaksForDate(sched, overrides, leaves, d);
    if (!breaks.length) return '';
    const now = new Date();
    const nowMin = now.getHours() * 60 + now.getMinutes();
    const upcoming = breaks
      .map((b) => ({
        start: parseTimeToMinutes(b.startTime),
        end: parseTimeToMinutes(b.endTime),
      }))
      .filter((b) => b.end > b.start && b.end >= nowMin)
      .sort((a, b) => a.start - b.start);
    if (!upcoming.length) return '';
    const next = upcoming[0];
    return `przerwa ${formatHm(next.start)}–${formatHm(next.end)}`;
  }

  private declensionWizyt(n: number): string {
    if (n === 1) return 'wizyta';
    const mod10 = n % 10;
    const mod100 = n % 100;
    if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14)) return 'wizyty';
    return 'wizyt';
  }

  private workingRangeBounds = computed(() => {
    const sched = this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined;
    const overrides = this.scheduleOverrides.value();
    const leaves = this.employeeLeaves.value();
    const d = this.selectedDate();
    const ranges = this.resolveWorkingRangesForDate(sched, overrides, leaves, d);
    if (!ranges?.length) return null;
    // Przerwy też należą do dnia i muszą się zmieścić w oknie osi. Pasy pracy mają JUŻ wycięte
    // przerwy (np. praca 10–16 z przerwą 14–16 → pas 10–14), więc bez uwzględnienia przerwy
    // koniec dnia wypadałby na 14:00, a kafelek przerwy 14–16 zostałby obcięty do 14–15.
    const breaks = this.resolveBreaksForDate(sched, overrides, leaves, d);
    const mins = [...ranges, ...breaks]
      .flatMap((r) => [parseTimeToMinutes(r.startTime), parseTimeToMinutes(r.endTime)])
      .filter((x) => Number.isFinite(x) && x > 0);
    if (!mins.length) return null;
    return { min: Math.min(...mins), max: Math.max(...mins) };
  });

  rangeStartHour = computed(() => {
    const app = this.positionedAppointments();
    const sched = this.workingRangeBounds();
    const slots = this.selectedDayStaticSlots();
    const earliestApp = app.length ? Math.min(...app.map((a) => a.startMin)) : null;
    const earliestSlot = slots.length ? Math.min(...slots.map((s) => s.startMin)) : null;
    const minMin = Math.min(
      earliestApp ?? Number.POSITIVE_INFINITY,
      earliestSlot ?? Number.POSITIVE_INFINITY,
      sched?.min ?? Number.POSITIVE_INFINITY
    );
    if (!Number.isFinite(minMin)) return this.fullRangeStartHour;

    let startHour = Math.max(this.fullRangeStartHour, Math.floor((minMin - 60) / 60));

    // Dla bieżącego dnia nie pokazujemy "starej" części osi — max 2h wstecz od teraz.
    if (sameCalendarDay(this.selectedDate(), this.startOfDay(new Date()))) {
      const now = new Date();
      const nowMin = now.getHours() * 60 + now.getMinutes();
      const recentCutHour = Math.max(this.fullRangeStartHour, Math.floor((nowMin - 60) / 60));
      startHour = Math.max(startHour, recentCutHour);

      // Ale zachowujemy wgląd w już zakończone wizyty z dzisiaj.
      const earliestCompleted = app
        .filter((x) => x.statusVariant === 'completed')
        .map((x) => x.startMin);
      if (earliestCompleted.length > 0) {
        const completedStartHour = Math.max(
          this.fullRangeStartHour,
          Math.floor((Math.min(...earliestCompleted) - 60) / 60)
        );
        startHour = Math.min(startHour, completedStartHour);
      }
    }

    return startHour;
  });

  rangeEndHour = computed(() => {
    const app = this.positionedAppointments();
    const sched = this.workingRangeBounds();
    const slots = this.selectedDayStaticSlots();
    const latestApp = app.length ? Math.max(...app.map((a) => a.endMin)) : null;
    const latestSlot = slots.length ? Math.max(...slots.map((s) => s.endMin)) : null;
    const maxMin = Math.max(
      latestApp ?? Number.NEGATIVE_INFINITY,
      latestSlot ?? Number.NEGATIVE_INFINITY,
      sched?.max ?? Number.NEGATIVE_INFINITY
    );
    if (!Number.isFinite(maxMin)) return this.fullRangeEndHour;
    const start = this.rangeStartHour();
    const end = Math.min(this.fullRangeEndHour, Math.ceil((maxMin + 60) / 60));
    return Math.max(start + 6, end);
  });

  dayStats = computed(() => {
    const items = this.positionedAppointments();
    return {
      total: items.length,
      completed: items.filter((x) => x.statusVariant === 'completed').length,
      active: items.filter((x) => x.statusVariant !== 'completed' && x.statusVariant !== 'canceled').length,
    };
  });

  /**
   * Pracownik, dla którego liczymy „Plan dnia" jednoznacznie: single-col (mobile / panel solo)
   * pokazuje zawsze jednego (`effectiveEmployeeId`); widok kolumn desktop — tylko gdy w polu
   * widzenia jest dokładnie jedna kolumna (salon solo / filtr na 1 pracownika). Przy wielu
   * kolumnach `null` — nagłówkowy licznik wolnych slotów byłby niejednoznaczny.
   */
  private readonly singleViewedEmployeeId = computed<string | null>(() => {
    if (!this.showDesktopColumns()) return this.effectiveEmployeeId() ?? null;
    const cols = this.columnEmployees();
    return cols.length === 1 ? (cols[0]?.id ?? null) : null;
  });

  /**
   * Konfiguracja grafiku (grafik tygodniowy + dni specjalne + urlopy) jednoznacznie oglądanego
   * pracownika. Źródło zależy od układu: kolumny desktop czytają zasoby `desktop*` (per-pracownik),
   * single-col — zasoby pojedynczego pracownika. `null` = brak jednego pracownika w widoku lub
   * trwa ładowanie konfiguracji.
   */
  private readonly viewedEmployeeScheduleConfig = computed<{
    sched: EmployeeScheduleDto[] | undefined;
    overrides: ScheduleOverrideDto[] | undefined;
    leaves: EmployeeLeaveDto[] | undefined;
  } | null>(() => {
    const empId = this.singleViewedEmployeeId();
    if (!empId) return null;
    if (this.showDesktopColumns()) {
      if (
        this.desktopWeeklySchedules.isLoading() ||
        this.desktopScheduleOverrides.isLoading() ||
        this.desktopEmployeeLeaves.isLoading()
      ) {
        return null;
      }
      return {
        sched: this.desktopWeeklySchedules.value()?.[empId],
        overrides: this.desktopScheduleOverrides.value()?.[empId],
        leaves: this.desktopEmployeeLeaves.value()?.[empId],
      };
    }
    if (
      this.weeklySchedule.isLoading() ||
      this.scheduleOverrides.isLoading() ||
      this.employeeLeaves.isLoading()
    ) {
      return null;
    }
    return {
      sched: this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined,
      overrides: this.scheduleOverrides.value(),
      leaves: this.employeeLeaves.value(),
    };
  });

  /** Dni objęte licznikiem zależnie od widoku: dzień / tydzień (Pn–Nd) / cały miesiąc. */
  private freeSlotsRangeDays(): Date[] {
    const mode = this.viewMode();
    if (mode === 'week') {
      const d = this.selectedDate();
      const monIdx = (d.getDay() + 6) % 7;
      const start = this.startOfDay(d);
      start.setDate(start.getDate() - monIdx);
      return Array.from({ length: 7 }, (_, i) => {
        const x = new Date(start);
        x.setDate(start.getDate() + i);
        return x;
      });
    }
    if (mode === 'month') {
      const m = this.selectedMonthAnchor();
      const lastDay = new Date(m.getFullYear(), m.getMonth() + 1, 0).getDate();
      return Array.from({ length: lastDay }, (_, i) => new Date(m.getFullYear(), m.getMonth(), i + 1));
    }
    return [this.startOfDay(this.selectedDate())];
  }

  /** Źródło wizyt dopasowane do zakresu widoku (dzień / tydzień / miesiąc). */
  private freeSlotsAppointmentSource(): AppointmentPreviewDto[] {
    const mode = this.viewMode();
    if (mode === 'week') return this.weekAppointments.value();
    if (mode === 'month') return this.monthAppointments.value();
    return this.appointments.value();
  }

  /**
   * Mapa `yyyy-MM-dd` → zajęte przedziały (minuty) z niezanulowanych wizyt danego pracownika.
   * W widoku kolumn `restrict=true` filtruje po `employeeId` (lista miesza pracowników).
   * Filtry kalendarza (statusy/fraza) celowo POMIJAMY — zajętość slotu nie zależy od UI.
   */
  private buildBusyByDay(
    list: readonly AppointmentPreviewDto[],
    empId: string,
  ): Map<string, { s: number; e: number }[]> {
    const step = this.appointmentSlotStepMinutes();
    const restrict = this.showDesktopColumns();
    const map = new Map<string, { s: number; e: number }[]>();
    for (const a of list) {
      if (statusVariantFromPreview(a) === 'canceled') continue;
      if (restrict && this.appointmentEmployeeId(a) !== empId) continue;
      const ad = appointmentDay(a.date as Date);
      if (!ad) continue;
      const s = parseTimeToMinutes(a.startTime);
      const e = Math.max(parseTimeToMinutes(a.endTime), s + step);
      const key = formatYyyyMmDd(ad);
      const arr = map.get(key);
      if (arr) arr.push({ s, e });
      else map.set(key, [{ s, e }]);
    }
    return map;
  }

  /**
   * Wolne sloty grafiku STATYCZNEGO dla jednego dnia. `null` = dzień nie jest statyczny (siatka
   * dynamiczna / brak grafiku / urlop) — nie wlicza się do sumy. Dzień miniony → 0 (nic już nie
   * do zarezerwowania); dla DZIŚ pomijamy godziny minione (spójnie z backendowym `IsStartInPast`).
   */
  private freeStaticSlotsForDay(
    date: Date,
    cfg: { sched: EmployeeScheduleDto[] | undefined; overrides: ScheduleOverrideDto[] | undefined; leaves: EmployeeLeaveDto[] | undefined },
    busyByDay: Map<string, { s: number; e: number }[]>,
  ): number | null {
    const fixed = resolveFixedStartTimesForDate(cfg.sched, cfg.overrides, cfg.leaves, date);
    if (!fixed) return null;
    const now = new Date();
    const todayStart = this.startOfDay(now).getTime();
    const dayStart = this.startOfDay(date).getTime();
    if (dayStart < todayStart) return 0;
    const isToday = dayStart === todayStart;
    const nowMin = now.getHours() * 60 + now.getMinutes();
    const busy = busyByDay.get(formatYyyyMmDd(date)) ?? [];
    let free = 0;
    for (const t of fixed) {
      const m = parseTimeToMinutes(t);
      if (isToday && m < nowMin) continue;
      if (busy.some((r) => r.s <= m && m < r.e)) continue;
      free++;
    }
    return free;
  }

  /**
   * Liczba wolnych slotów dla grafiku STATYCZNEGO (stałe godziny startu) w zakresie aktywnego
   * widoku: dzień → wybrany dzień, tydzień → cały tydzień (Pn–Nd), miesiąc → cały miesiąc.
   * Sumuje wolne sloty po dniach statycznych jednoznacznie oglądanego pracownika. `null` (kafelek
   * ukryty), gdy: wielu pracowników w kolumnach, trwa ładowanie konfiguracji, albo żaden dzień
   * zakresu nie jest statyczny (siatka dynamiczna / brak grafiku).
   */
  freeStaticSlots = computed<number | null>(() => {
    const empId = this.singleViewedEmployeeId();
    if (!empId) return null;
    const cfg = this.viewedEmployeeScheduleConfig();
    if (!cfg) return null;

    const busyByDay = this.buildBusyByDay(this.freeSlotsAppointmentSource(), empId);
    let total = 0;
    let anyStatic = false;
    for (const day of this.freeSlotsRangeDays()) {
      const n = this.freeStaticSlotsForDay(day, cfg, busyByDay);
      if (n === null) continue;
      anyStatic = true;
      total += n;
    }
    return anyStatic ? total : null;
  });

  /**
   * Wolne terminy grafiku STATYCZNEGO dla wybranego dnia (single-col) — do kafelków „Wolny termin"
   * w agendzie i na osi. Pomija sloty zajęte przez wizyty oraz minione (dziś: przed „teraz",
   * dzień przeszły: puste). `endMin` = najmniejszy odstęp między stałymi startami (siatka slotów),
   * fallback = krok slotu. Pusto, gdy dzień nie jest statyczny.
   */
  readonly selectedDayStaticSlots = computed<{ startMin: number; endMin: number }[]>(() => {
    if (this.showDesktopColumns()) return [];
    const empId = this.singleViewedEmployeeId();
    if (!empId) return [];
    const cfg = this.viewedEmployeeScheduleConfig();
    if (!cfg) return [];
    const fixed = resolveFixedStartTimesForDate(cfg.sched, cfg.overrides, cfg.leaves, this.selectedDate());
    if (!fixed || fixed.length === 0) return [];
    const starts = fixed
      .map((t) => parseTimeToMinutes(t))
      .filter((m) => Number.isFinite(m) && m >= 0)
      .sort((a, b) => a - b);
    if (!starts.length) return [];
    const diffs: number[] = [];
    for (let i = 1; i < starts.length; i++) {
      const d = starts[i] - starts[i - 1];
      if (d > 0) diffs.push(d);
    }
    const slotLen = diffs.length ? Math.min(...diffs) : this.appointmentSlotStepMinutes();

    const now = new Date();
    const selStart = this.startOfDay(this.selectedDate()).getTime();
    const todayStart = this.startOfDay(now).getTime();
    if (selStart < todayStart) return [];
    const isToday = selStart === todayStart;
    const nowMin = now.getHours() * 60 + now.getMinutes();
    const busy =
      this.buildBusyByDay(this.freeSlotsAppointmentSource(), empId).get(
        formatYyyyMmDd(this.selectedDate()),
      ) ?? [];

    return starts
      .filter((m) => !(isToday && m < nowMin))
      .filter((m) => !busy.some((r) => r.s <= m && m < r.e))
      .map((startMin) => ({ startMin, endMin: startMin + slotLen }));
  });

  /**
   * Kontekst slotów grafiku STATYCZNEGO dla widoku miesiąca: konfiguracja grafiku jednoznacznie
   * oglądanego pracownika + mapa zajętości całego miesiąca. `null` (komórki pokazują chipy wizyt),
   * gdy: nie jesteśmy w widoku miesiąca, brak jednego pracownika (agregat), lub trwa ładowanie
   * konfiguracji. Bramka spójna z kafelkiem „wolne terminy" ({@link freeStaticSlots}).
   */
  /**
   * Konfiguracja grafiku pracownika oglądanego w MIESIĄCU. Miesiąc jest zawsze single-employee
   * (`effectiveEmployeeId` + przełącznik), inaczej niż {@link viewedEmployeeScheduleConfig}, które
   * w kolumnach desktop wymaga dokładnie jednej kolumny. Ale ŹRÓDŁO danych jest to samo: gdy
   * `showDesktopColumns()`, zasoby single-col (`weeklySchedule` itd.) są celowo niepobierane
   * (dedup requestów) i zostają puste na stałe — czytanie ich tutaj kasowało sloty wolne/zajęte
   * w miesiącu na desktopie. `null` gdy brak uprawnień lub trwa ładowanie → same wizyty.
   */
  private readonly monthScheduleConfig = computed<{
    sched: EmployeeScheduleDto[] | undefined;
    overrides: ScheduleOverrideDto[] | undefined;
    leaves: EmployeeLeaveDto[] | undefined;
  } | null>(() => {
    const empId = this.effectiveEmployeeId();
    if (!empId || !this.canFetchScheduleConfigFor(empId)) return null;
    if (this.showDesktopColumns()) {
      if (
        this.desktopWeeklySchedules.isLoading() ||
        this.desktopScheduleOverrides.isLoading() ||
        this.desktopEmployeeLeaves.isLoading()
      ) {
        return null;
      }
      return {
        sched: this.desktopWeeklySchedules.value()?.[empId],
        overrides: this.desktopScheduleOverrides.value()?.[empId],
        leaves: this.desktopEmployeeLeaves.value()?.[empId],
      };
    }
    if (
      this.weeklySchedule.isLoading() ||
      this.scheduleOverrides.isLoading() ||
      this.employeeLeaves.isLoading()
    ) {
      return null;
    }
    return {
      sched: this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined,
      overrides: this.scheduleOverrides.value(),
      leaves: this.employeeLeaves.value(),
    };
  });

  protected readonly monthStaticSlots = computed<MonthStaticSlots | null>(() => {
    if (this.viewMode() !== 'month') return null;
    const empId = this.effectiveEmployeeId();
    if (!empId) return null;
    const cfg = this.monthScheduleConfig();
    if (!cfg) return null;
    const busy = this.buildBusyByDay(this.monthAppointments.value(), empId);
    return { cfg, busy };
  });

  /**
   * Pomocnik: czy odcinek `[startMin, endMin]` mieści się w którymkolwiek z `ranges`.
   * Zwraca `true` gdy wizyta wystaje poza grafik. Gdy `hasSchedule=false` (nie ma w ogóle
   * grafiku obowiązującego), banner przy kalendarzu pokrywa już informację, więc nie
   * oznaczamy wizyt indywidualnie (`false`).
   */
  private isAppointmentOutsideRanges(
    startMin: number,
    endMin: number,
    ranges: { startMin: number; endMin: number }[],
    hasSchedule: boolean
  ): boolean {
    return isAppointmentOutsideWorkingHours({
      startMin,
      endMin,
      ranges,
      hasSchedule,
      isPastDay: this.selectedDayIsPast(),
    });
  }

  /**
   * Czy wybrany dzień jest w przeszłości (przed dzisiaj). Dla minionych dni grafik bywa już nieaktywny
   * (activeTo minęło) → workingRanges puste → wizyty fałszywie oznaczane „poza godzinami pracy".
   * Ostrzeżenie out-of-range jest akcyjne tylko dla dziś/przyszłości, więc w przeszłości je wyciszamy.
   */
  private readonly selectedDayIsPast = computed(
    () => this.startOfDay(this.selectedDate()).getTime() < this.startOfDay(new Date()).getTime(),
  );

  /**
   * Czy wybrany dzień jest w trybie stałych slotów (single-col). W tym trybie nie ma „pasów pracy",
   * więc kontrola „poza godzinami" (pusty zestaw pasów → wszystko poza) dawałaby fałszywy alarm na
   * KAŻDEJ wizycie. Dla dni stałych ostrzeżenie out-of-range wyłączamy.
   */
  private readonly selectedDaySingleIsFixed = computed(() => {
    const sched = this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined;
    const overrides = this.scheduleOverrides.value();
    const leaves = this.employeeLeaves.value();
    return resolveFixedStartTimesForDate(sched, overrides, leaves, this.selectedDate()) !== null;
  });

  /** Aktualne `workingRanges` dla widoku single-col (jeden wybrany pracownik). */
  protected readonly currentDaySingleRanges = computed(() => {
    const sched = this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined;
    const overrides = this.scheduleOverrides.value();
    const leaves = this.employeeLeaves.value();
    const d = this.selectedDate();
    const ranges = this.resolveWorkingRangesForDate(sched, overrides, leaves, d);
    return ranges
      .map((r) => ({
        startMin: parseTimeToMinutes(r.startTime),
        endMin: Math.max(parseTimeToMinutes(r.endTime), parseTimeToMinutes(r.startTime) + 1),
      }))
      .filter((r) => r.endMin > r.startMin);
  });

  /**
   * Pas grafiku pracy na osi — z SUROWYCH godzin pracy (bez wycinania przerw), żeby przerwa NIE
   * przecinała tła grafiku: pas obejmuje cały dzień pracy (np. 10–16), a kafelek przerwy leży na
   * nim jako blok. `currentDaySingleRanges` (z wyciętymi przerwami) nadal steruje out-of-range wizyt.
   * Urlop / dzień wolny → pusto (raw = null).
   */
  protected readonly currentDayWorkBandSegments = computed(() => {
    const raw = this.selectedDayRawSchedule();
    if (!raw?.workRanges?.length) return [] as { startMin: number; endMin: number }[];
    return raw.workRanges
      .map((r) => ({
        startMin: parseTimeToMinutes(r.startTime!),
        endMin: Math.max(parseTimeToMinutes(r.endTime!), parseTimeToMinutes(r.startTime!) + 1),
      }))
      .filter((r) => r.endMin > r.startMin)
      .sort((a, b) => a.startMin - b.startMin);
  });

  positionedAppointments = computed(() => {
    const list = this.dayAppointments();
    const ranges = this.currentDaySingleRanges();
    const hasSchedule = !this.showDesktopColumns() && this.hasActiveScheduleForSelected();
    // Tryb stały: brak pasów pracy → kontrola out-of-range flagowałaby wszystko. Wyłączamy.
    const dayIsFixed = this.selectedDaySingleIsFixed();
    const mapped = list.map((raw) => {
      const startMin = parseTimeToMinutes(raw.startTime);
      const step = this.appointmentSlotStepMinutes();
      const endMin = Math.max(parseTimeToMinutes(raw.endTime), startMin + step);
      const statusVariant = statusVariantFromPreview(raw);
      const durationMin = Math.max(endMin - startMin, 0);
      const isOutsideSchedule = dayIsFixed
        ? false
        : this.isAppointmentOutsideRanges(startMin, endMin, ranges, hasSchedule);
      // compact = gęsty, czytelny układ 2-liniowy (godzina + usługa, nazwisko pod spodem). Pełny
      // układ (status-pill + zakres godzin + nazwisko + przyciski akcji) przy większej typografii
      // mieści się dopiero ~90 min (84 px/h), więc poniżej tego progu używamy gęstego — nazwisko
      // zawsze widoczne, bez przycinania. Akcje krótkich wizyt: panel boczny / arkusz po tapnięciu.
      return { raw, startMin, endMin, statusVariant, compact: durationMin < 60, isOutsideSchedule };
    });
    // Kolumny dla nakładających się wizyt (single-col = jeden pracownik, więc lane'y są poprawne).
    return assignTimelineLanes(mapped);
  });

  /** Wizyty wybranego dnia w kolejności chronologicznej — dla widoku agendy (lista). */
  readonly agendaAppointments = computed(() =>
    [...this.positionedAppointments()].sort(
      (a, b) => a.startMin - b.startMin || a.endMin - b.endMin
    )
  );

  /**
   * Pozycje agendy = początek/koniec pracy (grafik przedziałowy) + wizyty + przerwy, przeplecione
   * chronologicznie. `sort` rozstrzyga remis na tej samej minucie — priorytet rosnący:
   * `work-start (0) < break (1) < work-end (2) < visit (3) < slot (4)`. Dzięki temu:
   *   • „Początek pracy 8:00" jest nad wizytą/przerwą 8:00,
   *   • przerwa kończąca zmianę (np. 14–16 przy pracy 10–16) ląduje NAD kafelkiem „Koniec pracy 14:00",
   *   • ale wizyta poza grafikiem o godzinie końca nadal ląduje POD „Koniec pracy" (work-end < visit).
   * Przerwa z `breakRange` klikalna.
   */
  readonly agendaItems = computed(() => {
    const visits = this.agendaAppointments().map((v) => ({
      kind: 'visit' as const,
      key: 'v-' + (v.raw.id ?? v.startMin),
      startMin: v.startMin,
      endMin: v.endMin,
      sort: 3,
      visit: v,
    }));
    const breaks = this.breakSegments().map((b) => ({
      kind: 'break' as const,
      key: 'b-' + b.startMin,
      startMin: b.startMin,
      endMin: b.endMin,
      sort: 1,
      breakRange: b.breakRange,
    }));
    const work: Array<{ kind: 'work-start' | 'work-end'; key: string; startMin: number; endMin: number; sort: number }> = [];
    // Znaczniki pracy tylko dla grafiku przedziałowego (dynamiczny Grid); dla stałych slotów brak.
    if (!this.selectedDaySingleIsFixed()) {
      // `currentDaySingleRanges` to pasy pracy z WYCIĘTYMI przerwami, więc przerwa dzieli dzień na
      // dwa pasy. Przerwy pokazujemy osobnym kafelkiem, więc scalamy pasy z powrotem przez luki,
      // które w całości pokrywa przerwa → zostaje realny początek/koniec dnia. Prawdziwe split-shifty
      // (luka NIE będąca przerwą) pozostają osobnymi oknami pracy.
      const ranges = [...this.currentDaySingleRanges()].sort((a, b) => a.startMin - b.startMin);
      const breaks = this.breakSegments();
      const merged: { startMin: number; endMin: number }[] = [];
      for (const r of ranges) {
        const last = merged[merged.length - 1];
        const gapIsBreak =
          !!last && breaks.some((b) => b.startMin <= last.endMin && b.endMin >= r.startMin);
        if (last && gapIsBreak) {
          last.endMin = Math.max(last.endMin, r.endMin);
        } else {
          merged.push({ startMin: r.startMin, endMin: r.endMin });
        }
      }
      for (const r of merged) {
        work.push({ kind: 'work-start', key: 'ws-' + r.startMin, startMin: r.startMin, endMin: r.startMin, sort: 0 });
        work.push({ kind: 'work-end', key: 'we-' + r.endMin, startMin: r.endMin, endMin: r.endMin, sort: 2 });
      }
    }
    // Wolne terminy grafiku statycznego jako kafelki „Wolny termin" (tylko dzień statyczny).
    const slots = this.selectedDayStaticSlots().map((s) => ({
      kind: 'slot' as const,
      key: 's-' + s.startMin,
      startMin: s.startMin,
      endMin: s.endMin,
      sort: 4,
    }));
    return [...visits, ...breaks, ...work, ...slots].sort(
      (a, b) => a.startMin - b.startMin || a.sort - b.sort
    );
  });

  /** Bieżąca godzina w minutach od północy (reaktywna przez `nowTick` — odświeża się co 30 s). */
  protected readonly nowMinutes = computed(() => {
    const d = new Date(this.nowTick());
    return d.getHours() * 60 + d.getMinutes();
  });

  /**
   * Pozycja agendy, której PRZEDZIAŁ czasowy obejmuje bieżącą godzinę (np. trwająca wizyta lub
   * przerwa) — wtedy linię „Teraz" rysujemy NA tym kafelku, a nie jako osobny wiersz. Znaczniki
   * pracy mają zerową długość (start==end), więc nigdy nie „zawierają" teraz. `null` = nie dziś
   * albo bieżąca godzina wypada w luce między pozycjami.
   */
  readonly agendaNowContainer = computed(() => {
    if (!sameCalendarDay(this.selectedDate(), new Date(this.nowTick()))) return null;
    const nm = this.nowMinutes();
    return (
      this.agendaItems().find(
        (it) => it.endMin > it.startMin && it.startMin <= nm && nm < it.endMin,
      ) ?? null
    );
  });

  /** Ułamek [0,1] pozycji bieżącej godziny wewnątrz kafelka z `agendaNowContainer`; `null` gdy brak. */
  readonly agendaNowFraction = computed<number | null>(() => {
    const c = this.agendaNowContainer();
    if (!c) return null;
    const span = c.endMin - c.startMin;
    if (span <= 0) return null;
    return Math.min(1, Math.max(0, (this.nowMinutes() - c.startMin) / span));
  });

  /**
   * Indeks pozycji agendy, PRZED którą wstawiamy OSOBNY wiersz znacznika „Teraz" — tylko gdy dziś
   * ORAZ bieżąca godzina wypada w luce (żaden kafelek jej nie obejmuje; wtedy nakładka na kafelku
   * przejmuje rolę). `agendaItems().length` = znacznik na końcu; `null` = nie dziś / jest nakładka.
   */
  readonly agendaNowIndex = computed<number | null>(() => {
    if (!sameCalendarDay(this.selectedDate(), new Date(this.nowTick()))) return null;
    // Bieżąca godzina mieści się w kafelku → linię rysuje nakładka, osobny wiersz zbędny.
    if (this.agendaNowContainer()) return null;
    const items = this.agendaItems();
    const nm = this.nowMinutes();
    const idx = items.findIndex((it) => it.startMin >= nm);
    return idx === -1 ? items.length : idx;
  });

  /**
   * Buduje bloki przerw na osi WPROST z przerw dnia (każda długość — także krótkie, np. 15 min).
   * `removable` → przerwa jest klikalnym kafelkiem (edycja/usuwanie); inaczej blok statyczny.
   * Pasy pracy (zielone) rysują strukturę zmian osobno — tu pokazujemy tylko realne przerwy.
   */
  private buildBreakSegments(breaks: TimeRangeDto[] | undefined, removable: boolean): BreakSegment[] {
    const r0 = this.rangeStartHour() * 60;
    const r1 = this.rangeEndHour() * 60;
    return (breaks ?? [])
      .map((b) => ({
        startMin: Math.max(parseTimeToMinutes(b.startTime), r0),
        endMin: Math.min(parseTimeToMinutes(b.endTime), r1),
        breakRange: removable ? b : null,
      }))
      .filter((s) => s.endMin > s.startMin)
      .sort((a, b) => a.startMin - b.startMin);
  }

  /** Przerwy oglądanego pracownika (single-col / mobile). */
  breakSegments = computed<BreakSegment[]>(() => {
    if (this.weeklySchedule.error()) return [] as BreakSegment[];
    const raw = this.selectedDayRawSchedule();
    if (!raw) return [] as BreakSegment[];
    return this.buildBreakSegments(raw.breaks, this.canAddBreakForSelectedDay());
  });

  /** Przerwy per kolumna w widoku zespołu (desktop). Edytowalne tylko dla pojedynczego kontekstu. */
  desktopBreakSegments = computed<Record<string, BreakSegment[]>>(() => {
    if (!this.showDesktopColumns()) return {};
    const schedules = this.desktopWeeklySchedules.value() ?? {};
    const overrides = this.desktopScheduleOverrides.value() ?? {};
    const leaves = this.desktopEmployeeLeaves.value() ?? {};
    const day = this.selectedDate();
    const editableId = this.canAddBreakForSelectedDay() ? this.effectiveEmployeeId() : null;
    const out: Record<string, BreakSegment[]> = {};
    for (const [employeeId, empSchedules] of Object.entries(schedules)) {
      const raw = resolveRawScheduleDayForDate(empSchedules, overrides[employeeId], leaves[employeeId], day);
      out[employeeId] = this.buildBreakSegments(raw?.breaks, employeeId === editableId);
    }
    return out;
  });

  /**
   * Liczba wizyt poza grafikiem w obecnym widoku — używana m.in. przez NotificationCenter
   * (badge na dzwonku w nagłówku).
   */
  outsideScheduleCount = computed(() => {
    if (this.showDesktopColumns()) {
      return this.desktopColumns().reduce((sum, c) => sum + c.items.filter((i) => i.isOutsideSchedule).length, 0);
    }
    return this.positionedAppointments().filter((a) => a.isOutsideSchedule).length;
  });

  /**
   * Czy istnieje grafik obowiązujący dla wybranego pracownika i daty (tylko single-col,
   * gdzie w polu widzenia jest jeden pracownik). W desktop column-view brak grafiku per
   * pracownik wynika z pustego `desktopWorkingSegments[col.id]`.
   */
  hasActiveScheduleForSelected = computed(() => {
    if (this.showDesktopColumns()) return true;
    if (
      this.weeklySchedule.isLoading() ||
      this.scheduleOverrides.isLoading() ||
      this.employeeLeaves.isLoading()
    )
      return true;
    if (this.weeklySchedule.error()) return true;
    // Urlop/L4 i dzień specjalny też są „aktywnym grafikiem" — nawet gdy oznaczają dzień wolny.
    if (this.findBlockingLeaveForDate(this.employeeLeaves.value(), this.selectedDate())) return true;
    if (this.pickOverrideForDate(this.scheduleOverrides.value(), this.selectedDate())) return true;
    const sched = this.weeklySchedule.value() as EmployeeScheduleDto[] | undefined;
    if (!sched?.length) return false;
    return !!this.pickScheduleForDate(sched, this.selectedDate());
  });

  /**
   * Czy wybrany dzień jest WOLNY (brak godzin pracy) — obejmuje urlop/L4, dzień specjalny „wolne"
   * i zwykły dzień bez grafiku. Inaczej niż `hasActiveScheduleForSelected` (które urlop traktuje jak
   * aktywny grafik), tu liczy się realny brak pasów pracy. Grafik stały (stałe sloty) = dzień
   * pracujący, więc go wykluczamy. Tylko single-col; podczas ładowania nie zgadujemy.
   */
  protected readonly isSelectedDayOff = computed(() => {
    const employeeId = this.effectiveEmployeeId();
    if (this.showDesktopColumns() || !employeeId) return false;
    // Brak DANYCH to nie to samo co dzień wolny. Gdy nie wolno nam pobrać konfiguracji grafiku
    // (albo sesja jeszcze nie wstała), milczymy zamiast twierdzić, że kolega nie pracuje.
    if (!this.canFetchScheduleConfigFor(employeeId)) return false;
    if (
      this.weeklySchedule.isLoading() ||
      this.scheduleOverrides.isLoading() ||
      this.employeeLeaves.isLoading() ||
      this.weeklySchedule.error()
    )
      return false;
    // Urlop/L4 = dzień wolny nawet gdy grafik tygodniowy przewiduje na ten dzień godziny.
    if (this.findBlockingLeaveForDate(this.employeeLeaves.value(), this.selectedDate())) return true;
    if (this.selectedDaySingleIsFixed()) return false;
    return this.currentDaySingleRanges().length === 0;
  });

  /**
   * Dzień wolny z powodu urlopu/L4. Wtedy ustawianie godzin na ten dzień NIC nie da (urlop dalej
   * blokuje), a wizyty nie da się dodać — więc nie pokazujemy tych akcji, tylko sam stan „Dzień wolny".
   */
  protected readonly isSelectedDayOnLeave = computed(() => {
    if (this.showDesktopColumns() || !this.effectiveEmployeeId() || this.employeeLeaves.isLoading()) {
      return false;
    }
    return !!this.findBlockingLeaveForDate(this.employeeLeaves.value(), this.selectedDate());
  });

  /**
   * Pozycja linii "teraz" w lokalnym układzie toru (single-col i każdej sekcji w desktop).
   * Używamy `segmentTopPx`, dokładnie jak dla wizyt — w tym samym układzie współrzędnych
   * wewnątrz toru (`<div class="relative pt-2 ...">`). Dzięki temu linia, wizyty i siatka
   * godzin są spójnie pozycjonowane w obu wariantach (mobile/desktop).
   */
  nowLineTopPx = computed(() => {
    const now = new Date(this.nowTick());
    if (!sameCalendarDay(now, this.selectedDate())) return null;
    const min = now.getHours() * 60 + now.getMinutes();
    const r0 = this.rangeStartHour() * 60;
    const r1 = this.rangeEndHour() * 60;
    if (min < r0 || min > r1) return null;
    return this.segmentTopPx(min);
  });

  /** Odpowiedź JSON bywa camelCase albo PascalCase — bez tego nazwa usługi znikała z kafelka. */
  appointmentServiceName(p: AppointmentPreviewDto): string {
    const x = p as unknown as Record<string, unknown>;
    const n = p.serviceName ?? x['ServiceName'];
    return typeof n === 'string' && n.trim() !== '' ? n.trim() : '—';
  }

  appointmentCustomerLine(p: AppointmentPreviewDto): string {
    const x = p as unknown as Record<string, unknown>;
    if (p.isGuest === true || x['IsGuest'] === true) {
      return 'Gość';
    }
    const fn = (p.customerFirstName ?? x['CustomerFirstName'] ?? '') as string;
    const ln = (p.customerLastName ?? x['CustomerLastName'] ?? '') as string;
    const line = [fn, ln].map((s) => String(s).trim()).filter(Boolean).join(' ');
    if (line !== '') return line;
    const phoneRaw = p.customerPhoneNumber ?? x['CustomerPhoneNumber'];
    const phone =
      typeof phoneRaw === 'string' && phoneRaw.trim() !== '' ? phoneRaw.trim() : '';
    return phone !== '' ? phone : '—';
  }

  appointmentContactLine(p: AppointmentPreviewDto): string | null {
    const x = p as unknown as Record<string, unknown>;
    if (p.isGuest === true || x['IsGuest'] === true) return 'Gość';
    const channel = this.salonSettings.value()?.customerVerificationChannel;
    if (channel === CustomerVerificationChannel.Email) {
      const email = (p.customerEmail ?? (p as unknown as Record<string, unknown>)['CustomerEmail']);
      return typeof email === 'string' && email.trim() !== '' ? email.trim() : null;
    }
    const phone = (p.customerPhoneNumber ?? (p as unknown as Record<string, unknown>)['CustomerPhoneNumber']);
    return typeof phone === 'string' && phone.trim() !== '' ? phone.trim() : null;
  }

  /** Tło i kolor obramowania kafelka na osi czasu wg statusu wizyty. */
  protected appointmentTimelineSurfaceClasses(variant: AppointmentStatusVariant): string {
    const map: Record<AppointmentStatusVariant, string> = {
      pending:
        'bg-amber-50 dark:bg-amber-950/40 border-amber-200/70 dark:border-amber-800/50',
      booked:
        'bg-sky-50 dark:bg-sky-950/35 border-sky-200/60 dark:border-sky-800/50',
      inProgress:
        'bg-violet-50 dark:bg-violet-950/40 border-violet-200/60 dark:border-violet-800/50',
      completed:
        'bg-emerald-50 dark:bg-emerald-950/35 border-emerald-200/60 dark:border-emerald-800/50',
      canceled:
        'bg-surface-200/80 dark:bg-surface-200/50 border-surface-400/60 dark:border-surface-600/50',
      awaitingOtp:
        'bg-surface-100/80 dark:bg-surface-100/50 border-surface-300/60 dark:border-surface-600/50',
      default:
        'bg-slate-50 dark:bg-slate-900/50 border-slate-200/60 dark:border-slate-700/50',
    };
    return map[variant] ?? map.default;
  }

  protected accentBarClasses(variant: AppointmentStatusVariant): string {
    const map: Record<AppointmentStatusVariant, string> = {
      pending: 'bg-amber-400/80 dark:bg-amber-500/65',
      booked: 'bg-sky-400/80 dark:bg-sky-500/65',
      inProgress: 'bg-violet-500/85 dark:bg-violet-500/70',
      completed: 'bg-emerald-500/75 dark:bg-emerald-500/65',
      canceled: 'bg-surface-400/55 dark:bg-surface-500/45',
      awaitingOtp: 'bg-surface-300/60 dark:bg-surface-500/40',
      default: 'bg-slate-400/60 dark:bg-slate-500/50',
    };
    return map[variant] ?? map.default;
  }

  /** Ikona statusu PrimeIcons — pokazywana zawsze, zwłaszcza w compact mode gdzie pełna pigułka się nie mieści. */
  protected statusIconClass(variant: AppointmentStatusVariant): string {
    const map: Record<AppointmentStatusVariant, string> = {
      pending: 'pi pi-clock',
      booked: 'pi pi-check-circle',
      inProgress: 'pi pi-spin pi-spinner',
      completed: 'pi pi-check',
      canceled: 'pi pi-times',
      awaitingOtp: 'pi pi-shield',
      default: 'pi pi-circle',
    };
    return map[variant] ?? map.default;
  }

  protected statusBadgeClasses(variant: AppointmentStatusVariant): string {
    const map: Record<AppointmentStatusVariant, string> = {
      pending: 'text-amber-700 dark:text-amber-300 bg-amber-100/70 dark:bg-amber-900/40 border-amber-300/50 dark:border-amber-700/50',
      booked: 'text-sky-700 dark:text-sky-300 bg-sky-100/70 dark:bg-sky-900/40 border-sky-300/50 dark:border-sky-700/50',
      inProgress: 'text-violet-700 dark:text-violet-300 bg-violet-100/70 dark:bg-violet-900/40 border-violet-300/50 dark:border-violet-700/50',
      completed: 'text-emerald-700 dark:text-emerald-300 bg-emerald-100/70 dark:bg-emerald-900/40 border-emerald-300/50 dark:border-emerald-700/50',
      canceled: 'text-surface-600 dark:text-surface-300 bg-surface-200/70 dark:bg-surface-200/50 border-surface-300/50 dark:border-surface-600/50',
      awaitingOtp: 'text-surface-500 dark:text-surface-400 bg-surface-100/70 dark:bg-surface-100/50 border-surface-200/50 dark:border-surface-600/50',
      default: 'text-slate-600 dark:text-slate-300 bg-slate-100/70 dark:bg-slate-800/50 border-slate-200/50 dark:border-slate-600/50',
    };
    return map[variant] ?? map.default;
  }

  protected statusLabel(variant: AppointmentStatusVariant): string {
    switch (variant) {
      case 'pending':
        return 'Oczekuje';
      case 'awaitingOtp':
        return 'Oczekuje na kod';
      case 'booked':
        return 'Zatwierdzona';
      case 'inProgress':
        return 'W trakcie';
      case 'completed':
        return 'Zakończona';
      case 'canceled':
        return 'Anulowana';
      default:
        return 'Wizyta';
    }
  }

  protected canQuickConfirm(variant: AppointmentStatusVariant): boolean {
    return variant === 'pending';
  }

  protected canQuickCancel(variant: AppointmentStatusVariant): boolean {
    return variant !== 'completed' && variant !== 'canceled';
  }

  /**
   * Czy w bieżącym kontekście (rola + zakres) zalogowany user może dodać wizytę. Kiosk („Recepcja")
   * tworzy dla całego zespołu; Employee bez `TeamFull` — tylko gdy widzi własny kalendarz;
   * Owner/Manager zawsze. Używane w `month-day-sheet` do warunkowego pokazania CTA „Dodaj wizytę".
   */
  protected canCreateAppointmentForOwnScope(): boolean {
    const role = this.auth.currentRole();
    if (role === 'kiosk') return true;
    if (role === 'owner' || role === 'manager') return true;
    if (role !== 'employee') return false;
    if (this.canMutateOthers()) return true;
    const own = this.currentEmployeeId();
    const effective = this.effectiveEmployeeId();
    return !!own && !!effective && own === effective;
  }

  /**
   * Czy zalogowany user może zatwierdzać/anulować KONKRETNĄ wizytę. Owner/Manager — zawsze;
   * Employee — tylko własne, chyba że salon ma `TeamFull`. Sprawdzane per-wizyta (przyciski
   * w timeline + akcje sheet'a).
   */
  protected canMutateAppointment(appointment: AppointmentPreviewDto | null): boolean {
    if (!appointment) return false;
    if (this.canMutateOthers()) return true;
    const ownEmpId = this.currentEmployeeId();
    return !!ownEmpId && this.appointmentEmployeeId(appointment) === ownEmpId;
  }

  protected isUpdatingAppointment(id: string | undefined): boolean {
    if (!id) return false;
    return this.appointmentUpdates()[id] === true;
  }

  /**
   * Blokada przycisków szybkich akcji (Zatwierdź/Anuluj) — nie tylko na czas żądania HTTP, ale też
   * na czas przeładowania listy PO nim. Bez tego po szybkim „Zatwierdź" przycisk odblokowywał się,
   * zanim kafel zdążył się przerenderować, a „Anuluj" wskakiwał na miejsce zniknionego „Zatwierdź" —
   * okno, w którym drugi tap tego samego gestu anulował świeżo potwierdzoną wizytę.
   */
  protected isAppointmentActionLocked(id: string | undefined): boolean {
    return (
      this.isUpdatingAppointment(id) ||
      this.appointments.isLoading() ||
      this.pendingAppointments.isLoading()
    );
  }

  weekdayShort(d: Date): string {
    return PL_WEEKDAYS[d.getDay()] ?? '';
  }

  selectDay(d: Date): void {
    this.selectedDate.set(this.startOfDay(d));
  }

  goToday(): void {
    const today = this.startOfDay(new Date());
    this.selectedDate.set(today);
    this.selectedMonthAnchor.set(this.startOfMonth(today));
  }

  isDayInSelectedMonth(day: Date): boolean {
    const anchor = this.selectedMonthAnchor();
    return day.getFullYear() === anchor.getFullYear() && day.getMonth() === anchor.getMonth();
  }

  /** Czy podany dzień to dzisiaj — pasek dat wyróżnia „dziś" kolorowym znacznikiem. */
  isToday(day: Date): boolean {
    return sameCalendarDay(day, new Date());
  }

  /** Czy kalendarz pokazuje już dzisiejszy dzień — wtedy przycisk „Dziś" jest nieaktywny. */
  isViewingToday = computed(() => sameCalendarDay(this.selectedDate(), new Date(this.nowTick())));

  /** Wygaszony, gdy jesteśmy już na dzisiaj; poza tym neutralny, jak w widoku tygodnia. */
  protected readonly todayButtonClasses = computed(() =>
    this.isViewingToday()
      ? 'border-surface-200 dark:border-surface-700 text-surface-400 cursor-default'
      : 'border-surface-300 dark:border-surface-600 text-surface-700 hover:border-primary/45',
  );

  /**
   * Krótsza etykieta miesiąca do przełącznika mobilnego — sam miesiąc (bez roku), a rok tylko gdy
   * różny od bieżącego. Dzięki temu „Lipiec" nie jest ucinany na wąskich ekranach.
   */
  selectedMonthLabelShort = computed(() => {
    const anchor = this.selectedMonthAnchor();
    const month = anchor.toLocaleDateString('pl-PL', { month: 'long' });
    const currentYear = new Date(this.nowTick()).getFullYear();
    return anchor.getFullYear() === currentYear ? month : `${month} ${anchor.getFullYear()}`;
  });

  shiftMonth(delta: number): void {
    const current = this.selectedMonthAnchor();
    const next = new Date(current.getFullYear(), current.getMonth() + delta, 1);
    this.selectedMonthAnchor.set(this.startOfMonth(next));
    const selected = this.selectedDate();
    if (selected.getFullYear() !== next.getFullYear() || selected.getMonth() !== next.getMonth()) {
      this.selectedDate.set(this.startOfMonth(next));
    }
  }

  /**
   * Po otwarciu panelu miesiąca (appendTo body) wyrównujemy jego PRAWĄ krawędź do prawej krawędzi
   * triggera — panel rozwija się w lewo, nie w prawo (desktop). PrimeNG domyślnie wyrównuje do lewej
   * i odbija dopiero przy krawędzi viewportu, więc na szerokim ekranie panel uciekał w prawo.
   */
  protected alignMonthPanel(): void {
    if (!this.isDesktop()) return;
    // PrimeNG pozycjonuje wrapper p-motion (position:absolute, inset-inline-start) i wyrównuje do lewej,
    // odbijając dopiero przy krawędzi viewportu — na szerokim ekranie panel uciekał w prawo. Przesuwamy
    // go tak, by jego prawa krawędź pokryła się z prawą krawędzią triggera (otwiera się w lewo). Mapowanie
    // inset→pozycja w PrimeNG bywa nieliniowe (+ re-align po onShow), więc korygujemy iteracyjnie aż luka ≤1px.
    const correct = (tries: number): void => {
      const trigger = document.querySelector<HTMLElement>('app-visit-schedule p-datepicker, app-visit-schedule p-date-picker');
      const panel = document.querySelector<HTMLElement>('.cal-month-panel');
      if (!trigger || !panel) return;
      let positioned: HTMLElement = panel;
      while (positioned !== document.body && getComputedStyle(positioned).position === 'static' && positioned.parentElement) {
        positioned = positioned.parentElement;
      }
      if (positioned === document.body) return;
      const gap = trigger.getBoundingClientRect().right - panel.getBoundingClientRect().right;
      if (Math.abs(gap) <= 1) return;
      const current = parseFloat(getComputedStyle(positioned).insetInlineStart)
        || positioned.getBoundingClientRect().left + window.scrollX;
      positioned.style.left = '';
      positioned.style.insetInlineStart = `${Math.max(8, current + gap)}px`;
      if (tries > 0) requestAnimationFrame(() => correct(tries - 1));
    };
    requestAnimationFrame(() => correct(5));
  }

  /** Wybór miesiąca z `p-date-picker` (view="month") — `ngModel` to Date (pierwszy dzień miesiąca). */
  onMonthPicked(value: Date | null | undefined): void {
    if (!value) return;
    const next = this.startOfMonth(value);
    this.selectedMonthAnchor.set(next);
    const selected = this.selectedDate();
    if (selected.getFullYear() !== value.getFullYear() || selected.getMonth() !== value.getMonth()) {
      this.selectedDate.set(next);
    }
  }

  segmentTopPx(startMin: number): number {
    const base = this.rangeStartHour() * 60;
    return ((startMin - base) / 60) * this.hourHeightPx + 8;
  }

  /**
   * Wysokość wg trwania na osi (pasy pracy / przerwy). Podłoga 44px zapewnia wygodny tap-target dla
   * krótkich przerw (15-min przy 140px/h = 35px → podniesione do 44px). Pasy pracy są długie, więc
   * podłoga ich nie dotyczy.
   */
  segmentHeightPx(startMin: number, endMin: number): number {
    return Math.max(((endMin - startMin) / 60) * this.hourHeightPx, 44);
  }

  /**
   * Wysokość kafelka wizyty = wyłącznie czas trwania na kalendarzu (bez sztucznego min.),
   * żeby sąsiednie wizyty nie nachodziły na siebie.
   */
  visitBlockHeightPx(startMin: number, endMin: number): number {
    const durationMin = Math.max(endMin - startMin, 0);
    const h = (durationMin / 60) * this.hourHeightPx;
    return Math.max(h, 2);
  }

  /** Lewy offset kafelka wg przydzielonej kolumny kolizji (6 px margines lewej krawędzi toru). */
  protected laneLeftStyle(a: { lane: number; laneCount: number }): string {
    return `calc(${(a.lane * 100) / a.laneCount}% + 6px)`;
  }

  /** Szerokość kafelka = 1/laneCount toru minus margines (większy, gdy jest tylko jedna kolumna). */
  protected laneWidthStyle(a: { lane: number; laneCount: number }): string {
    return `calc(${100 / a.laneCount}% - ${a.laneCount > 1 ? 8 : 12}px)`;
  }

  /** FAB (mobile): otwiera arkusz z zakładkami Wizyta/Przerwa. */
  onFabClick(): void {
    this.quickAddOpen.set(true);
  }

  /** „Dodaj wizytę" (desktop chip / empty-state): kreator wizyty bezpośrednio. */
  openCreateVisit(): void {
    this.openCreateDrawer();
  }

  closeQuickAdd(): void {
    this.quickAddOpen.set(false);
  }

  onQuickCreateSuccess(id: string): void {
    this.quickAddOpen.set(false);
    this.onCreateSuccess(id);
  }

  onQuickBreakSuccess(): void {
    this.quickAddOpen.set(false);
    this.reloadBreakSources();
  }

  /**
   * Tap w kafelek wizyty (timeline lub karta agendy) → bottom sheet / drawer z pełnymi
   * szczegółami wizyty (drawer doładowuje cenę/notatkę/Instagram po id).
   */
  onAppointmentTap(appointment: AppointmentPreviewDto, ev?: Event): void {
    ev?.stopPropagation();
    if (this.swapMode()) {
      this.onSwapPick(appointment);
      return;
    }
    this.selectedAppointment.set(appointment);
  }

  /** Toggle trybu zamiany; wyjście czyści wybór. Używane przez „Anuluj" na banerze zamiany. */
  toggleSwapMode(): void {
    const next = !this.swapMode();
    this.swapMode.set(next);
    this.swapFirst.set(null);
    if (next) {
      this.closeAppointmentSheet();
    }
  }

  /**
   * Start zamiany z drawera wizyty: zamyka drawer, włącza tryb zamiany z tą wizytą jako pierwszą,
   * po czym użytkownik wskazuje drugą wizytę (tap → `onSwapPick` → dialog zamiany).
   */
  startSwapWith(appointment: AppointmentPreviewDto): void {
    if (!appointment.id) return;
    this.closeAppointmentSheet();
    this.swapMode.set(true);
    this.swapFirst.set(appointment);
  }

  /** Wybór wizyty w trybie zamiany: pierwsza → zapamiętaj; druga → otwórz dialog. */
  private onSwapPick(appointment: AppointmentPreviewDto): void {
    if (!appointment.id) return;
    const first = this.swapFirst();
    if (!first) {
      this.swapFirst.set(appointment);
      return;
    }
    if (first.id === appointment.id) {
      this.swapFirst.set(null); // ponowny tap = odznacz
      return;
    }
    this.swapPair.set({ first, second: appointment });
    this.swapFirst.set(null);
    this.swapMode.set(false);
  }

  closeSwapDialog(): void {
    this.swapPair.set(null);
  }

  onSwapSuccess(): void {
    this.swapPair.set(null);
    void this.appointments.reload();
    void this.dayStripAppointments.reload();
    if (this.viewMode() === 'month') void this.monthAppointments.reload();
    if (this.viewMode() === 'week') void this.weekAppointments.reload();
  }

  /** Czy wizyta jest pierwszą wybraną w trybie zamiany (do podświetlenia kafelka). */
  protected isSwapSelected(appointment: AppointmentPreviewDto | undefined): boolean {
    return this.swapMode() && !!appointment?.id && this.swapFirst()?.id === appointment.id;
  }

  closeAppointmentSheet(): void {
    this.selectedAppointment.set(null);
    this.deepLinkDetail.set(null);
  }

  /**
   * Czy warto proponować „Pokaż w kalendarzu" — tylko gdy kalendarz NIE pokazuje już tej wizyty
   * (inny dzień albo inny pracownik). Po kliknięciu kafelka w kalendarzu przycisk się nie pojawia,
   * bo prowadziłby tam, gdzie użytkownik już jest.
   */
  protected readonly canRevealSelectedInCalendar = computed(() => {
    const a = this.selectedAppointment();
    if (!a?.date) return false;

    const day = this.startOfDay(new Date(a.date as unknown as string));
    if (Number.isNaN(day.getTime())) return false;

    const shown = this.selectedDate();
    if (this.viewMode() !== 'day' || day.getTime() !== this.startOfDay(shown).getTime()) return true;

    const empId = a.employeeId ?? null;
    return !!empId && empId !== this.employeeIdFromRoute();
  });

  /**
   * Przeskok zainicjowany przez użytkownika (przycisk w panelu), nie przez samo otwarcie
   * powiadomienia. Panel zostaje otwarty — zmienia się tylko tło, którego użytkownik zażądał.
   */
  protected revealSelectedInCalendar(): void {
    const a = this.selectedAppointment();
    if (!a?.date) return;

    const day = this.startOfDay(new Date(a.date as unknown as string));
    if (Number.isNaN(day.getTime())) return;

    this.selectedDate.set(day);
    this.viewMode.set('day');

    const emps = this.employees.value() ?? [];
    const empId = a.employeeId ?? null;
    const switchEmp =
      empId && empId !== this.employeeIdFromRoute() && emps.some((e) => e.id === empId)
        ? empId
        : null;

    // Dzień MUSI iść w tej samej nawigacji co ewentualna zmiana pracownika — inaczej
    // `CalendarStateService.applyFromUrl` odczyta stary `date` z URL i cofnie sygnał daty.
    void this.router.navigate(switchEmp ? ['/admin', 'schedule', switchEmp] : [], {
      replaceUrl: true,
      queryParams: { date: formatYyyyMmDd(day), view: 'day' },
      queryParamsHandling: 'merge',
    });
  }

  onSheetConfirm(id: string): void {
    this.closeAppointmentSheet();
    this.quickConfirm(id);
  }

  onSheetCancel(id: string): void {
    // Arkusz szczegółów potwierdził już anulowanie własnym dialogiem (onCancel) — wchodzimy wprost
    // w wykonanie, żeby nie pokazywać drugiego dialogu.
    this.closeAppointmentSheet();
    this.performCancel(id);
  }

  /**
   * Zmiana czasu wizyty w arkuszu szczegółów (endTime się zmienił) — przeładuj kalendarz, żeby blok
   * odświeżył długość. Arkusz zostaje otwarty (personel może dalej regulować / zatwierdzać).
   */
  onSheetDurationChanged(): void {
    void this.appointments.reload();
    void this.pendingAppointments.reload();
  }

  /**
   * Deep-link do pojedynczej wizyty (`/admin/schedule?appointment=<id>`, np. z profilu klienta).
   * Doładowuje wizytę po id, przestawia kalendarz na jej dzień (i pracownika, jeśli inny niż w URL),
   * po czym otwiera drawer ze szczegółami. Param czyścimy tą samą nawigacją — refresh nie otworzy
   * drawera ponownie. Wywoływane z efektu w konstruktorze gdy `appointmentParam` jest ustawiony.
   */
  private openAppointmentFromDeepLink(id: string): void {
    // `reveal=0` → panel bez ruszania kalendarza (powiadomienia „do potwierdzenia").
    this.openAppointmentById(id, this.deepLinkRevealParam() !== false, true);
  }

  /**
   * Otwiera panel wizyty po id. `clearUrlParams` odróżnia dwa wejścia:
   *  - deep-link URL-owy (`?appointment=`) — paramy trzeba wyczyścić, żeby refresh nie otwierał
   *    panelu ponownie, więc nawigacja jest nieunikniona;
   *  - `AppointmentFocusService` (klik w dzwonku przy zamontowanym kalendarzu) — w URL nic nie
   *    ma do posprzątania, więc przy `reveal=false` NIE nawigujemy ani razu.
   */
  private openAppointmentById(id: string, reveal: boolean, clearUrlParams: boolean): void {
    this.appointmentsClient.getAppointmentById(id).subscribe({
      next: (full) => {
        const emps = this.employees.value() ?? [];
        const empId = full.employeeId ?? null;
        const switchEmp =
          reveal && empId && empId !== this.employeeIdFromRoute() && emps.some((e) => e.id === empId)
            ? empId
            : null;

        const d =
          reveal && full.date ? this.startOfDay(new Date(full.date as unknown as string)) : null;
        const day = d && !Number.isNaN(d.getTime()) ? d : null;
        if (day) {
          this.selectedDate.set(day);
          this.viewMode.set('day');
        }
        this.deepLinkDetail.set(full);
        this.selectedAppointment.set(full as unknown as AppointmentPreviewDto);

        // Panel jest już otwarty (sygnały wyżej). Nawigujemy TYLKO jeśli jest co zapisać:
        // dzień/pracownika przy `reveal`, albo paramy do wyczyszczenia po deep-linku URL-owym.
        // Klik w dzwonku bez `reveal` nie ma ani jednego, ani drugiego → zero nawigacji.
        if (!clearUrlParams && !day && !switchEmp) return;

        // Jedna nawigacja: ewentualna zmiana pracownika, ustawienie dnia/widoku wizyty ORAZ
        // wyczyszczenie param `appointment`. Dzień MUSI iść w tym samym navigate — inaczej
        // `CalendarStateService.applyFromUrl` odczyta stary `date` z URL i cofnie sygnał daty
        // (URL wygrywa wyścig z `selectedDate.set`).
        const queryParams: Record<string, string | null> = clearUrlParams
          ? { appointment: null, reveal: null }
          : {};
        if (day) {
          queryParams['date'] = formatYyyyMmDd(day);
          queryParams['view'] = 'day';
        }
        const commands = switchEmp ? ['/admin', 'schedule', switchEmp] : [];
        void this.router.navigate(commands, {
          replaceUrl: true,
          queryParams,
          queryParamsHandling: 'merge',
        });
      },
      error: () => {
        if (!clearUrlParams) return;
        // Nie udało się pobrać (brak dostępu / usunięta) — czyścimy param, zostajemy w kalendarzu.
        void this.router.navigate([], {
          replaceUrl: true,
          queryParams: { appointment: null, reveal: null },
          queryParamsHandling: 'merge',
        });
      },
    });
  }

  /**
   * (F3.1) Sheet → dialog reschedule. Zamykamy sheet, ustawiamy target → dialog otworzy się
   * dzięki `[appointment]` (= signal). Po success parent reload-uje wizyty.
   */
  onSheetReschedule(appointment: AppointmentPreviewDto): void {
    this.closeAppointmentSheet();
    this.rescheduleTarget.set(appointment);
  }

  /**
   * „Umów ponownie": klient kończy wizytę, od razu umawiamy mu kolejną. PreviewDto ma tylko
   * nazwy (brak `serviceId` / `customerId`), więc fetchujemy pełny `AppointmentDto` po `id`,
   * z którego budujemy prefill kontekstu CreateAppointmentDrawer.
   */
  onSheetRebook(appointment: AppointmentPreviewDto): void {
    if (!appointment.id) return;
    this.closeAppointmentSheet();
    this.appointmentsClient.getAppointmentById(appointment.id).subscribe({
      next: (full) => {
        const d = this.selectedDate();
        const y = d.getFullYear();
        const mo = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        const emp = full.employeeId ?? this.effectiveEmployeeId();
        this.createContext.set({
          date: `${y}-${mo}-${day}`,
          ...(emp ? { employeeId: emp } : {}),
          prefill: {
            serviceId: full.serviceId,
            customerId: full.customerId,
            customerMode: full.customerId ? 'list' : 'guest',
          },
        });
      },
      error: () => {
        // Fallback: otwieramy drawer bez prefilla — user uzupełni ręcznie.
        const d = this.selectedDate();
        const y = d.getFullYear();
        const mo = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        const emp = this.effectiveEmployeeId();
        this.createContext.set({ date: `${y}-${mo}-${day}`, ...(emp ? { employeeId: emp } : {}) });
      },
    });
  }

  closeRescheduleDialog(): void {
    this.rescheduleTarget.set(null);
  }

  onRescheduleSuccess(_id: string): void {
    this.rescheduleTarget.set(null);
    // Reload zarówno dnia jak i strip-a — wizyta mogła zmienić dzień, więc obie listy
    // mogą być stale (stary slot zniknie, nowy slot dojedzie).
    void this.appointments.reload();
    void this.dayStripAppointments.reload();
    if (this.viewMode() === 'month') void this.monthAppointments.reload();
    if (this.viewMode() === 'week') void this.weekAppointments.reload();
  }

  /** Sheet → dialog „Zmień usługę". Zamykamy sheet, ustawiamy target → dialog otworzy się. */
  onSheetChangeService(appointment: AppointmentPreviewDto): void {
    this.closeAppointmentSheet();
    this.changeServiceTarget.set(appointment);
  }

  closeChangeServiceDialog(): void {
    this.changeServiceTarget.set(null);
  }

  onChangeServiceSuccess(): void {
    this.changeServiceTarget.set(null);
    // Termin bez zmian, ale długość/cena wizyty mogły się zmienić — odśwież widoczne listy,
    // by kafelek przeliczył czas trwania.
    void this.appointments.reload();
    void this.dayStripAppointments.reload();
    if (this.viewMode() === 'month') void this.monthAppointments.reload();
    if (this.viewMode() === 'week') void this.weekAppointments.reload();
  }

  closeCreateDrawer(): void {
    this.createContext.set(null);
  }

  onCreateSuccess(_id: string): void {
    this.createContext.set(null);
    void this.appointments.reload();
    void this.dayStripAppointments.reload();
    if (this.viewMode() === 'month') void this.monthAppointments.reload();
    if (this.viewMode() === 'week') void this.weekAppointments.reload();
  }

  /** Otwarcie edytora przerwy dla oglądanego pracownika i wybranego dnia. */
  openBreakEditor(): void {
    if (!this.canAddBreakForSelectedDay()) return;
    const emp = this.effectiveEmployeeId();
    if (!emp) return;
    this.breakContext.set({ employeeId: emp, date: formatYyyyMmDd(this.selectedDate()) });
  }

  /** „Dodaj przerwę po wizycie" z drawera wizyty → edytor z prefillem startu = koniec wizyty. */
  onAddBreakAfterSelected(a: AppointmentPreviewDto): void {
    if (!this.canAddBreakAfterSelected() || !a.endTime) return;
    const emp = a.employeeId ?? this.effectiveEmployeeId();
    if (!emp) return;
    const end = parseTimeToMinutes(a.endTime);
    this.closeAppointmentSheet();
    this.breakContext.set({
      employeeId: emp,
      date: formatYyyyMmDd(this.selectedDate()),
      startTime: `${String(Math.floor(end / 60)).padStart(2, '0')}:${String(end % 60).padStart(2, '0')}`,
    });
  }

  closeBreakEditor(): void {
    this.breakContext.set(null);
  }

  /** Po dodaniu przerwy: zamknij edytor i odśwież dni specjalne (oś przeliczy przerwy). */
  onBreakSuccess(): void {
    this.breakContext.set(null);
    this.reloadBreakSources();
  }

  /**
   * Odświeża dni specjalne po dodaniu/edycji/usunięciu przerwy. Kafelki czytają z różnych zasobów
   * zależnie od widoku: single-col → `scheduleOverrides`, kolumny (desktop) → `desktopScheduleOverrides`.
   * Przeładowujemy oba, by oś zaktualizowała się bez odświeżania strony.
   */
  private reloadBreakSources(): void {
    void this.scheduleOverrides.reload();
    void this.desktopScheduleOverrides.reload();
  }

  /** Klik w kafelek przerwy (single-col) → edytor w trybie edycji (zmiana godzin / usunięcie). */
  openBreakEditFor(brk: TimeRangeDto, ev?: Event): void {
    this.openBreakEditForEmployee(this.effectiveEmployeeId(), brk, ev);
  }

  /** Klik w kafelek przerwy w kolumnie (desktop) → edycja dla pracownika tej kolumny. */
  openBreakEditForEmployee(employeeId: string | undefined, brk: TimeRangeDto, ev?: Event): void {
    ev?.stopPropagation();
    if (!employeeId || !this.canAddBreakForSelectedDay() || employeeId !== this.effectiveEmployeeId()) {
      return;
    }
    this.breakContext.set({
      employeeId,
      date: formatYyyyMmDd(this.selectedDate()),
      editBreak: brk,
    });
  }

  quickConfirm(id: string | undefined, ev?: Event): void {
    ev?.stopPropagation();
    if (!id || this.isUpdatingAppointment(id)) return;
    this.setAppointmentUpdating(id, true);
    this.appointmentsClient.updateAppointmentStatus(id, 2).subscribe({
      next: () => {
        this.messages.add({
          severity: 'success',
          summary: 'Zatwierdzono',
          detail: 'Wizyta została zatwierdzona.',
          life: 2500,
        });
        void this.appointments.reload();
        void this.pendingAppointments.reload();
        this.setAppointmentUpdating(id, false);
      },
      error: () => {
        this.messages.add({
          severity: 'error',
          summary: 'Błąd',
          detail: 'Nie udało się zatwierdzić wizyty.',
          life: 4000,
        });
        this.setAppointmentUpdating(id, false);
      },
    });
  }

  quickCancel(id: string | undefined, ev?: Event): void {
    ev?.stopPropagation();
    if (!id || this.isUpdatingAppointment(id)) return;
    // Anulowanie jest destrukcyjne (zwalnia termin + SMS do klientki) i na kaflu sąsiaduje z
    // „Zatwierdź". Po szybkim potwierdzeniu „Zatwierdź" znika, a „Anuluj" wskakuje na jego miejsce —
    // bez tego dialogu podwójny tap na potwierdzeniu anulował świeżo potwierdzoną wizytę
    // (incydent 2026-06: potwierdzenie i anulowanie w tej samej sekundzie, zwolniony termin).
    this.confirm.confirm({
      header: 'Anulować wizytę?',
      message:
        'Termin zostanie zwolniony, a klientka dostanie SMS o odwołaniu. Tej operacji nie można cofnąć.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Tak, anuluj',
      rejectLabel: 'Wstecz',
      acceptButtonStyleClass: 'p-button-danger',
      rejectButtonStyleClass: 'p-button-outlined p-button-secondary',
      accept: () => this.performCancel(id),
    });
  }

  /**
   * Faktyczne anulowanie — wołane PO potwierdzeniu. Kafel potwierdza dialogiem powyżej; arkusz
   * szczegółów potwierdza własnym dialogiem (onCancel) i wchodzi tu przez onSheetCancel bez drugiego.
   */
  private performCancel(id: string): void {
    if (this.isUpdatingAppointment(id)) return;
    this.setAppointmentUpdating(id, true);
    this.appointmentsClient.updateAppointmentStatus(id, 5).subscribe({
      next: () => {
        this.messages.add({
          severity: 'success',
          summary: 'Anulowano',
          detail: 'Wizyta została anulowana.',
          life: 2500,
        });
        void this.appointments.reload();
        void this.pendingAppointments.reload();
        this.setAppointmentUpdating(id, false);
      },
      error: () => {
        this.messages.add({
          severity: 'error',
          summary: 'Błąd',
          detail: 'Nie udało się anulować wizyty.',
          life: 4000,
        });
        this.setAppointmentUpdating(id, false);
      },
    });
  }

  /**
   * Ostatnio oglądany pracownik. Dla pracownika „scoped" nie ma czego pamiętać — widzi wyłącznie
   * własny kalendarz, a podpowiedź z cudzym id i tak odpadłaby na sprawdzeniu listy.
   */
  private rememberedEmployeeId(): string | null {
    if (this.isEmployeeScoped()) return null;
    return this.lastEmployeeStore.read(this.auth.currentUserId());
  }

  private rememberEmployee(employeeId: string): void {
    if (this.isEmployeeScoped()) return;
    this.lastEmployeeStore.save(this.auth.currentUserId(), employeeId);
  }

  onEmployeeChange(employeeId: string): void {
    if (this.isEmployeeScoped()) {
      return;
    }
    if (!employeeId) return;
    // Zachowujemy stan kalendarza w URL (data/widok/filtry) przy zmianie pracownika.
    void this.router.navigate(['/admin', 'schedule', employeeId], {
      queryParamsHandling: 'preserve',
    });
  }

  private openCreateDrawer(prefill?: CreateAppointmentContext['prefill'], startTime?: string): void {
    const d = this.selectedDate();
    const y = d.getFullYear();
    const mo = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    const emp = this.effectiveEmployeeId();
    this.createContext.set({
      date: `${y}-${mo}-${day}`,
      ...(emp ? { employeeId: emp } : {}),
      ...(startTime ? { startTime } : {}),
      ...(prefill ? { prefill } : {}),
    });
  }

  /** Klik w kafelek „Wolny termin" grafiku statycznego → drawer z zaznaczoną godziną (HH:mm). */
  protected openCreateVisitAt(startMin: number): void {
    this.openCreateDrawer(undefined, formatHm(startMin));
  }

  private centerSelectedDayInSlider(): void {
    const slider = this.daySlider()?.nativeElement;
    if (!slider) return;
    const selected = slider.querySelector<HTMLElement>('[data-selected-day="true"]');
    if (!selected) return;

    const targetLeft = selected.offsetLeft - (slider.clientWidth / 2 - selected.clientWidth / 2);
    const maxLeft = Math.max(0, slider.scrollWidth - slider.clientWidth);
    const nextLeft = Math.min(maxLeft, Math.max(0, targetLeft));
    if (typeof slider.scrollTo === 'function') {
      slider.scrollTo({ left: nextLeft, behavior: 'smooth' });
      return;
    }
    slider.scrollLeft = nextLeft;
  }

  private appointmentEmployeeId(a: AppointmentPreviewDto): string | null {
    const x = a as unknown as Record<string, unknown>;
    const v = x['employeeId'] ?? x['EmployeeId'];
    return typeof v === 'string' ? v : null;
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    if (typeof window !== 'undefined') {
      this.viewportWidth.set(window.innerWidth);
    }
  }

  private setAppointmentUpdating(id: string, updating: boolean): void {
    this.appointmentUpdates.update((state) => {
      if (!updating) {
        const copy = { ...state };
        delete copy[id];
        return copy;
      }
      return { ...state, [id]: true };
    });
  }

  protected readonly formatHm = formatHm;

  /** Łączny czas trwania (np. „15 min", „1 godz", „1 godz 30 min") — do kafelka przerwy w agendzie. */
  protected breakDurationLabel(startMin: number, endMin: number): string {
    const total = Math.max(0, endMin - startMin);
    const h = Math.floor(total / 60);
    const m = total % 60;
    if (h && m) return `${h} godz ${m} min`;
    if (h) return `${h} godz`;
    return `${m} min`;
  }
}
