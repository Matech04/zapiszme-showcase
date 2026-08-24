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
import { RouterLink } from '@angular/router';
import { DatePicker } from 'primeng/datepicker';
import { ConfirmationService, MessageService } from 'primeng/api';
import {
  AppointmentPreviewDto,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  EmployeesClient,
  ScheduleOverrideDto,
  TimeRangeDto,
} from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { ScheduleOverridesApiService } from '@core/api/schedule-overrides-api.service';
import { FormDrawerShellComponent } from '@shared/ui/drawer/form-drawer-shell.component';
import { startOfDay } from './date-utils';
import {
  buildGridOverrideDto,
  canAddGridBreak,
  gridDayMatchesWeeklySchedule,
  isBreakWithinWorkRanges,
  normalizeTimeHms,
  resolveRawScheduleDayForDate,
  timeRangesOverlap,
} from './schedule-resolution';

export interface BreakEditorContext {
  employeeId: string;
  /** yyyy-MM-dd */
  date: string;
  /** Opcjonalny prefill startu (HH:mm / HH:mm:ss) — np. z tapnięcia w pusty slot na osi. */
  startTime?: string;
  /** Gdy ustawione → tryb EDYCJI istniejącej przerwy (prefill + zapis zastępuje, dostępne usuwanie). */
  editBreak?: TimeRangeDto;
}

/** Statusowe id wizyty „Anulowana" — nie blokuje przerwy (kolizja ignorowana). */
const CANCELED_STATUS_ID = 5;

const PRESET_MINUTES = [15, 30, 60] as const;

function parseYmd(s: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  if (!m) return null;
  return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

function dateToMinutes(d: Date): number {
  return d.getHours() * 60 + d.getMinutes();
}

function minutesToTimeDate(base: Date, minutes: number): Date {
  const d = new Date(base);
  d.setHours(Math.floor(minutes / 60), minutes % 60, 0, 0);
  return d;
}

function hmsToMinutes(t: string | undefined): number {
  if (!t) return 0;
  const p = t.split(':').map(Number);
  return (p[0] ?? 0) * 60 + (p[1] ?? 0);
}

function timeDateToHms(d: Date): string {
  const hh = d.getHours().toString().padStart(2, '0');
  const mm = d.getMinutes().toString().padStart(2, '0');
  return `${hh}:${mm}:00`;
}

function formatHm(totalMinutes: number): string {
  const h = Math.floor(totalMinutes / 60);
  const m = totalMinutes % 60;
  return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
}

/**
 * Edytor szybkiej przerwy w panelu kalendarza. Przerwa zapisywana jest jako dzień specjalny
 * (`ScheduleOverride` w trybie `Grid`): bierzemy SUROWE pasy pracy dnia i dokładamy nową przerwę
 * do istniejących — dzięki temu nie kasujemy godzin pracy ani innych przerw. Działa tylko dla
 * grafiku dynamicznego (`Grid`) z pasem pracy obejmującym wybrany czas.
 *
 * Dane grafiku (schedules/overrides/leaves) i wizyty dnia podaje rodzic (kalendarz już je ma) —
 * komponent ich nie pobiera. Tryb `embedded` (mobilny arkusz z zakładkami) renderuje samo ciało;
 * rodzic steruje stopką (submit/anuluj) przez {@link submit} / {@link canSubmit} / {@link submitting}.
 */
@Component({
  selector: 'app-break-editor-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, RouterLink, FormDrawerShellComponent, DatePicker],
  template: `
    @if (embedded()) {
      <ng-container [ngTemplateOutlet]="body" />
    } @else {
      <app-form-drawer-shell
        [isOpen]="isVisible()"
        [title]="isEditMode() ? 'Edytuj przerwę' : 'Nowa przerwa'"
        label="Kalendarz"
        [submitLabel]="isEditMode() ? 'Zapisz' : 'Dodaj przerwę'"
        [submitDisabled]="!canSubmit()"
        [submitting]="submitting()"
        (submitClicked)="submit()"
        (closeRequested)="onCancel()"
      >
        <div drawer-body class="flex flex-col gap-5">
          <ng-container [ngTemplateOutlet]="body" />
        </div>
      </app-form-drawer-shell>
    }

    <ng-template #body>
      @if (!canAddBreak()) {
        <!-- Grafik statyczny / brak grafiku / urlop — przerwy niedostępne w tym trybie. -->
        <div
          class="flex flex-col gap-3 rounded-xl border border-sky-300/80 dark:border-sky-700/60 bg-sky-50 dark:bg-sky-950/30 px-3 py-3"
          data-testid="break-unavailable"
        >
          <div class="flex items-start gap-3">
            <i class="pi pi-info-circle mt-0.5 text-sky-700 dark:text-sky-300" aria-hidden="true"></i>
            <div class="flex flex-col gap-0.5 min-w-0">
              <span class="text-sm font-semibold text-sky-900 dark:text-sky-100">
                Przerwy dostępne tylko dla grafiku dynamicznego
              </span>
              <span class="text-xs text-sky-800/85 dark:text-sky-200/85 leading-relaxed">
                {{ unavailableReason() }}
              </span>
            </div>
          </div>
          <a
            [routerLink]="scheduleEditRouterLink()"
            class="self-start rounded-full border border-sky-500/45 px-3 py-1.5 text-[11px] font-bold uppercase tracking-wider text-sky-800 dark:text-sky-200 hover:bg-sky-100/60 dark:hover:bg-sky-900/40 transition-colors"
          >
            Edytuj grafik
          </a>
        </div>
      } @else {
        <!-- Godziny pracy dnia (kontekst) -->
        <p class="text-xs text-surface-500 dark:text-surface-400">
          Godziny pracy: <span class="font-semibold tabular-nums">{{ workRangesLabel() }}</span>
        </p>

        <!-- Start przerwy -->
        <div class="flex flex-col gap-1.5">
          <label for="break-start" class="admin-section-label">Początek przerwy</label>
          <p-date-picker
            inputId="break-start"
            [timeOnly]="true"
            hourFormat="24"
            [showIcon]="true"
            icon="pi pi-clock"
            [readonlyInput]="false"
            [fluid]="true"
            appendTo="body"
            [ngModel]="startTime()"
            (ngModelChange)="onStartChange($event)"
            [disabled]="submitting()"
          />
        </div>

        <!-- Długość: presety + ręczny koniec -->
        <div class="flex flex-col gap-1.5">
          <label class="admin-section-label">Długość</label>
          <div class="flex flex-wrap gap-2">
            @for (m of presetMinutes; track m) {
              <button
                type="button"
                [disabled]="submitting()"
                (click)="applyPreset(m)"
                [class.ring-2]="durationMinutes() === m"
                [class.ring-primary]="durationMinutes() === m"
                [class.bg-primary-50]="durationMinutes() === m"
                [class.dark:bg-primary-900/20]="durationMinutes() === m"
                [class.text-primary]="durationMinutes() === m"
                [class.border-primary]="durationMinutes() === m"
                class="px-3 py-1.5 rounded-full border border-surface-200 dark:border-surface-200 text-sm font-bold hover:border-primary transition-colors bg-surface-0 dark:bg-surface-50 text-surface-700 dark:text-surface-300 disabled:cursor-not-allowed"
              >
                {{ m }} min
              </button>
            }
          </div>
        </div>

        <!-- Koniec przerwy (ręczna korekta) -->
        <div class="flex flex-col gap-1.5">
          <label for="break-end" class="admin-section-label">Koniec przerwy</label>
          <p-date-picker
            inputId="break-end"
            [timeOnly]="true"
            hourFormat="24"
            [showIcon]="true"
            icon="pi pi-clock"
            [readonlyInput]="false"
            [fluid]="true"
            appendTo="body"
            [ngModel]="endTime()"
            (ngModelChange)="onEndChange($event)"
            [disabled]="submitting()"
          />
        </div>

        @if (validationError(); as err) {
          <div
            class="rounded-xl border border-amber-300/60 dark:border-amber-700/40 bg-amber-50/70 dark:bg-amber-950/30 px-3 py-2 text-xs text-amber-900 dark:text-amber-100 flex items-start gap-2"
            data-testid="break-validation-error"
          >
            <i class="pi pi-exclamation-triangle mt-0.5" aria-hidden="true"></i>
            <span>{{ err }}</span>
          </div>
        }

        <p class="text-[11px] text-surface-400 dark:text-surface-500 leading-snug">
          Przerwa zablokuje ten czas dla rezerwacji — w panelu i w rezerwacji online.
        </p>

        @if (isEditMode()) {
          <button
            type="button"
            (click)="deleteBreak()"
            [disabled]="submitting()"
            data-testid="break-delete"
            class="self-start inline-flex items-center gap-2 rounded-full border border-red-300/70 dark:border-red-700/50 px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-red-700 dark:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/25 transition-colors disabled:opacity-50"
          >
            <i class="pi pi-trash text-[11px]" aria-hidden="true"></i>
            Usuń przerwę
          </button>
        }
      }
    </ng-template>
  `,
})
export class BreakEditorDrawerComponent {
  readonly context = input<BreakEditorContext | null>(null);
  /** Tryb osadzony (mobilny arkusz z zakładkami) — bez własnego drawer-shell; rodzic steruje stopką. */
  readonly embedded = input<boolean>(false);
  /** Dane grafiku — przekazywane przez kalendarz (już je trzyma), by nie duplikować pobrań. */
  readonly schedules = input<EmployeeScheduleDto[] | undefined>(undefined);
  readonly overrides = input<ScheduleOverrideDto[] | undefined>(undefined);
  readonly leaves = input<EmployeeLeaveDto[] | undefined>(undefined);
  /** Wizyty wybranego dnia (do kontroli kolizji z przerwą). */
  readonly dayAppointments = input<AppointmentPreviewDto[]>([]);
  /** Krok slotów salonu (domyślna długość przerwy). */
  readonly slotStepMinutes = input<number>(15);

  readonly closeRequested = output<void>();
  readonly success = output<void>();

  private readonly employeesClient = inject(EmployeesClient);
  private readonly overridesApi = inject(ScheduleOverridesApiService);
  private readonly messages = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly auth = inject(AuthSessionService);

  protected readonly presetMinutes = PRESET_MINUTES;
  protected readonly isVisible = computed(() => this.context() != null);

  protected readonly startTime = signal<Date | null>(null);
  protected readonly endTime = signal<Date | null>(null);
  protected readonly isSubmitting = signal(false);
  /** Publiczny stan zapisu (rodzic w trybie embedded czyta go na stopkę). */
  readonly submitting = this.isSubmitting.asReadonly();

  /** Data kontekstu jako `Date` (północ lokalnie) — baza pickerów czasu. */
  private readonly contextDate = computed(() => {
    const ctx = this.context();
    return ctx ? parseYmd(ctx.date) : null;
  });

  /** Surowy dzień grafiku (z pasami „jak zapisane"). */
  private readonly rawDay = computed(() => {
    const d = this.contextDate();
    if (!d) return null;
    return resolveRawScheduleDayForDate(this.schedules(), this.overrides(), this.leaves(), d);
  });

  readonly canAddBreak = computed(() => canAddGridBreak(this.rawDay()));

  /** Tryb edycji istniejącej przerwy (vs dodawanie nowej). */
  readonly isEditMode = computed(() => !!this.context()?.editBreak);

  /** Edytowana przerwa, znormalizowana do HH:mm:ss (do porównań). */
  private readonly editBreakNorm = computed<TimeRangeDto | null>(() => {
    const e = this.context()?.editBreak;
    if (!e) return null;
    return { startTime: normalizeTimeHms(e.startTime ?? ''), endTime: normalizeTimeHms(e.endTime ?? '') };
  });

  /** Przerwy dnia z pominięciem edytowanej — baza do walidacji nakładania i budowy zapisu. */
  private readonly otherBreaks = computed<TimeRangeDto[]>(() => {
    const all = this.rawDay()?.breaks ?? [];
    const ed = this.editBreakNorm();
    if (!ed) return all;
    return all.filter(
      (b) =>
        !(
          normalizeTimeHms(b.startTime ?? '') === ed.startTime &&
          normalizeTimeHms(b.endTime ?? '') === ed.endTime
        ),
    );
  });

  private readonly workRanges = computed<TimeRangeDto[]>(() => this.rawDay()?.workRanges ?? []);

  protected readonly workRangesLabel = computed(() =>
    this.workRanges()
      .map((r) => `${formatHm(hmsToMinutes(r.startTime))}–${formatHm(hmsToMinutes(r.endTime))}`)
      .join(', '),
  );

  /** Powód niedostępności (komunikat) — rozróżnia urlop / brak grafiku / grafik statyczny. */
  protected readonly unavailableReason = computed(() => {
    const raw = this.rawDay();
    if (!raw) return 'W tym dniu pracownik nie ma aktywnego grafiku (lub ma urlop). Ustaw godziny w edytorze grafiku.';
    return 'Ten pracownik ma w tym dniu grafik ze stałymi godzinami. Aby zablokować termin, edytuj grafik.';
  });

  protected readonly durationMinutes = computed(() => {
    const s = this.startTime();
    const e = this.endTime();
    if (!s || !e) return 0;
    return dateToMinutes(e) - dateToMinutes(s);
  });

  /** Zakres przerwy w formacie TimeRange (HH:mm:ss) — gdy oba czasy ustawione. */
  private readonly breakRange = computed<TimeRangeDto | null>(() => {
    const s = this.startTime();
    const e = this.endTime();
    if (!s || !e) return null;
    return { startTime: timeDateToHms(s), endTime: timeDateToHms(e) };
  });

  /** Wizyty dnia, które realnie blokują (pomijamy anulowane). */
  private readonly blockingAppointments = computed(() =>
    this.dayAppointments().filter((a) => a.status?.id !== CANCELED_STATUS_ID),
  );

  protected readonly validationError = computed<string | null>(() => {
    const brk = this.breakRange();
    if (!brk) return null;
    const startMin = hmsToMinutes(brk.startTime);
    const endMin = hmsToMinutes(brk.endTime);
    if (endMin <= startMin) return 'Koniec przerwy musi być po jej początku.';
    if (!isBreakWithinWorkRanges(brk, this.workRanges())) {
      return 'Przerwa musi mieścić się w godzinach pracy.';
    }
    if (this.isPastBreak(startMin)) return 'Nie można dodać przerwy w przeszłości.';
    if (this.otherBreaks().some((b) => timeRangesOverlap(brk, b))) {
      return 'Ta przerwa nachodzi na inną przerwę.';
    }
    if (this.blockingAppointments().some((a) => timeRangesOverlap(brk, a))) {
      return 'W tym czasie jest już wizyta — wybierz inny termin.';
    }
    return null;
  });

  readonly canSubmit = computed(
    () =>
      !!this.context() &&
      this.canAddBreak() &&
      !!this.breakRange() &&
      this.validationError() === null &&
      !this.isSubmitting(),
  );

  /** Link do edytora grafiku — własny vs cudzy pracownik (jak w kreatorze wizyty). */
  readonly scheduleEditRouterLink = computed<string[]>(() => {
    const emp = this.context()?.employeeId;
    if (!emp) return ['/admin/my-availability'];
    return this.auth.currentEmployeeId() === emp
      ? ['/admin/my-availability', emp, 'schedules']
      : ['/admin/resources/employees', emp, 'schedules'];
  });

  constructor() {
    // Inicjalizacja / reset formularza przy otwarciu/zamknięciu drawera.
    effect(() => {
      const ctx = this.context();
      // Zależności do przeliczenia domyślnych godzin po doczytaniu danych grafiku.
      const work = this.workRanges();
      untracked(() => {
        if (!ctx) {
          this.reset();
          return;
        }
        this.isSubmitting.set(false);
        this.initDefaultTimes(work, ctx.startTime);
      });
    });
  }

  private initDefaultTimes(work: TimeRangeDto[], prefillStart?: string): void {
    const base = this.contextDate();
    if (!base || work.length === 0) {
      this.startTime.set(null);
      this.endTime.set(null);
      return;
    }
    // Tryb edycji — prefill z edytowanej przerwy (użytkownik koryguje konkretny zakres).
    const edit = this.context()?.editBreak;
    if (edit) {
      this.startTime.set(minutesToTimeDate(base, hmsToMinutes(edit.startTime)));
      this.endTime.set(minutesToTimeDate(base, hmsToMinutes(edit.endTime)));
      return;
    }
    const step = Math.max(5, this.slotStepMinutes() || 15);
    const sorted = [...work].sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''));
    const firstStart = hmsToMinutes(sorted[0].startTime);
    const breaks = this.rawDay()?.breaks ?? [];

    // Najwcześniejszy dozwolony start: prefill (tap w slot) → dziś: zaokrąglone „teraz" → początek dnia.
    let earliest = firstStart;
    if (prefillStart) {
      earliest = hmsToMinutes(prefillStart);
    } else if (startOfDay(base).getTime() === startOfDay(new Date()).getTime()) {
      const now = new Date();
      earliest = Math.max(firstStart, Math.ceil((now.getHours() * 60 + now.getMinutes()) / step) * step);
    }

    // Najwcześniejsze wykonalne [start, start+step] w pasie pracy, omijające istniejące przerwy.
    const placed = this.firstFeasibleStart(sorted, breaks, earliest, step);
    const startMin = placed ?? firstStart;
    const range =
      sorted.find((r) => hmsToMinutes(r.startTime) <= startMin && startMin < hmsToMinutes(r.endTime)) ??
      sorted[0];
    const endMin = Math.min(startMin + step, hmsToMinutes(range.endTime));

    this.startTime.set(minutesToTimeDate(base, startMin));
    this.endTime.set(minutesToTimeDate(base, endMin));
  }

  /**
   * Najwcześniejszy start ≥ `minStart`, dla którego okno `[start, start+step]` mieści się w pasie
   * pracy i nie nachodzi na istniejącą przerwę. `null`, gdy nigdzie się nie mieści (np. dziś po pracy).
   */
  private firstFeasibleStart(
    ranges: TimeRangeDto[],
    breaks: TimeRangeDto[],
    minStart: number,
    step: number,
  ): number | null {
    const bs = breaks
      .map((b) => ({ s: hmsToMinutes(b.startTime), e: hmsToMinutes(b.endTime) }))
      .sort((a, b) => a.s - b.s);
    for (const r of ranges) {
      const rs = hmsToMinutes(r.startTime);
      const re = hmsToMinutes(r.endTime);
      let c = Math.max(rs, minStart);
      // Przesuwaj kandydata za każdą przerwę, którą nachodzi okno [c, c+step].
      let moved = true;
      while (moved) {
        moved = false;
        for (const b of bs) {
          if (c < b.e && b.s < c + step) {
            c = Math.max(c, b.e);
            moved = true;
          }
        }
      }
      if (c + step <= re) return c;
    }
    return null;
  }

  private isPastBreak(startMin: number): boolean {
    const base = this.contextDate();
    if (!base) return false;
    const today = startOfDay(new Date());
    const day = startOfDay(base).getTime();
    if (day < today.getTime()) return true;
    if (day > today.getTime()) return false;
    const now = new Date();
    return startMin < now.getHours() * 60 + now.getMinutes();
  }

  protected onStartChange(value: Date | null | undefined): void {
    if (!value) {
      this.startTime.set(null);
      return;
    }
    const base = this.contextDate() ?? value;
    const startMin = dateToMinutes(value);
    const prevDuration = this.durationMinutes();
    this.startTime.set(minutesToTimeDate(base, startMin));
    // Utrzymaj dotychczasową długość (jeśli sensowna), inaczej krok salonu.
    const dur = prevDuration > 0 ? prevDuration : Math.max(5, this.slotStepMinutes() || 15);
    this.endTime.set(minutesToTimeDate(base, startMin + dur));
  }

  protected onEndChange(value: Date | null | undefined): void {
    if (!value) {
      this.endTime.set(null);
      return;
    }
    const base = this.contextDate() ?? value;
    this.endTime.set(minutesToTimeDate(base, dateToMinutes(value)));
  }

  protected applyPreset(minutes: number): void {
    const s = this.startTime();
    const base = this.contextDate();
    if (!s || !base) return;
    this.endTime.set(minutesToTimeDate(base, dateToMinutes(s) + minutes));
  }

  protected onCancel(): void {
    this.closeRequested.emit();
  }

  /**
   * Zapis przerwy jako dnia specjalnego. Dodawanie: dokłada przerwę do istniejących. Edycja:
   * zastępuje edytowaną (`otherBreaks` + nowa). Publiczne — wołane też z embedded (mobilny arkusz).
   */
  submit(): void {
    if (!this.canSubmit()) return;
    const ctx = this.context();
    const raw = this.rawDay();
    const brk = this.breakRange();
    if (!ctx || !raw || !brk) return;

    const normalized: TimeRangeDto = {
      startTime: normalizeTimeHms(brk.startTime ?? ''),
      endTime: normalizeTimeHms(brk.endTime ?? ''),
    };
    const base = this.isEditMode() ? this.otherBreaks() : raw.breaks;
    this.commitBreaks(
      ctx,
      raw,
      [...base, normalized],
      this.isEditMode() ? 'Przerwa zapisana' : 'Przerwa dodana',
      'Termin został zablokowany.',
    );
  }

  /** Usuwa edytowaną przerwę (z potwierdzeniem). */
  deleteBreak(): void {
    const ctx = this.context();
    const raw = this.rawDay();
    if (!ctx || !raw || !this.isEditMode() || this.isSubmitting()) return;
    this.confirmationService.confirm({
      message: 'Usunąć tę przerwę? Termin znów będzie dostępny do rezerwacji.',
      header: 'Usuwanie przerwy',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Usuń',
      rejectLabel: 'Anuluj',
      accept: () =>
        this.commitBreaks(ctx, raw, this.otherBreaks(), 'Przerwa usunięta', 'Termin znów jest dostępny.'),
    });
  }

  /**
   * Zapisuje docelową listę przerw dnia: gdy dzień wraca do grafiku tygodniowego — kasuje override
   * (`removeOverride`), inaczej zapisuje override Grid z `breaks`. Po sukcesie emituje `success`.
   */
  private commitBreaks(
    ctx: BreakEditorContext,
    raw: NonNullable<ReturnType<typeof resolveRawScheduleDayForDate>>,
    breaks: TimeRangeDto[],
    summary: string,
    detail: string,
  ): void {
    const day = this.contextDate();
    if (!day) return;
    this.isSubmitting.set(true);
    const onError = (): void => {
      this.isSubmitting.set(false);
      // Komunikaty domenowe (np. nakładanie/poza godzinami) obsługuje errorInterceptor (toast PL).
    };
    const onSuccess = (outside: number): void => {
      this.isSubmitting.set(false);
      this.messages.add(
        outside > 0
          ? {
              severity: 'warn',
              summary,
              detail: `Uwaga: ${outside} wizyt(y) wypada teraz poza grafikiem.`,
              life: 5000,
            }
          : { severity: 'success', summary, detail, life: 3000 },
      );
      this.success.emit();
    };

    if (gridDayMatchesWeeklySchedule(this.schedules(), this.leaves(), day, raw.workRanges, breaks)) {
      this.overridesApi.removeOverride(ctx.employeeId, ctx.date).subscribe({
        next: () => onSuccess(0),
        error: onError,
      });
    } else {
      const dto = buildGridOverrideDto(day, raw, breaks);
      // `date` w body to „yyyy-MM-dd" (JSON.stringify zwróci string; backend parsuje DateOnly).
      dto.date = ctx.date as unknown as Date;
      this.employeesClient.setScheduleOverride(ctx.employeeId, dto).subscribe({
        next: (result) => onSuccess(result?.appointmentsOutsideSchedule?.length ?? 0),
        error: onError,
      });
    }
  }

  private reset(): void {
    this.startTime.set(null);
    this.endTime.set(null);
    this.isSubmitting.set(false);
  }
}
