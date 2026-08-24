import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { CommonModule, DOCUMENT, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { ToggleButtonModule } from 'primeng/togglebutton';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { SelectButtonModule } from 'primeng/selectbutton';
import {
  EmployeeScheduleDto,
  EmployeeScheduleDayDto,
  ShiftTemplateDto,
  TimeRangeDto,
  EmployeesClient,
  ShiftTemplatesClient,
  SlotGenerationMode,
} from '@core/api/api-client';
import { rxResource } from '@angular/core/rxjs-interop';
import { WeekDayCardComponent } from './ui/week-day-card.component';
import { Router, RouterLink } from '@angular/router';
import { MessageService, MenuItem } from 'primeng/api';
import { Menu } from 'primeng/menu';
import { FormFieldCalendarComponent } from '@shared/ui/forms/form-field-calendar.component';
import { safeBackWith } from '@core/navigation/safe-back';
import { HasUnsavedChanges } from '@core/guards/has-unsaved-changes';
import { DirtyFormBeforeUnloadDirective } from '@core/guards/dirty-form-beforeunload.directive';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { of } from 'rxjs';

type DayKey = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';

const DAY_OF_WEEK_INDEX: Record<DayKey, number> = {
  Sunday: 0,
  Monday: 1,
  Tuesday: 2,
  Wednesday: 3,
  Thursday: 4,
  Friday: 5,
  Saturday: 6,
};

const DAY_MAPPING: { key: DayKey; label: string }[] = [
  { key: 'Monday', label: 'Poniedziałek' },
  { key: 'Tuesday', label: 'Wtorek' },
  { key: 'Wednesday', label: 'Środa' },
  { key: 'Thursday', label: 'Czwartek' },
  { key: 'Friday', label: 'Piątek' },
  { key: 'Saturday', label: 'Sobota' },
  { key: 'Sunday', label: 'Niedziela' },
];

/** Sentinel używany do oznaczenia grafiku „bezterminowo / do odwołania". */
export const INDEFINITE_ACTIVE_TO = '9999-12-31';

function todayYyyyMmDd(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function todayPlusYearsYyyyMmDd(years: number): string {
  const d = new Date();
  d.setFullYear(d.getFullYear() + years);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Koniec bieżącego roku (31 grudnia) w formacie yyyy-MM-dd. */
function endOfThisYearYyyyMmDd(): string {
  return `${new Date().getFullYear()}-12-31`;
}

export interface DayScheduleUi {
  /** Trwały klucz dnia tygodnia w obrębie cyklu (np. "Monday"). */
  dayKey: string;
  /** Nazwa do wyświetlenia (np. "Poniedziałek"). */
  dayName: string;
  /** Indeks tygodnia w cyklu (0..numberOfCycles-1). */
  weekIndex: number;
  isWorking: boolean;
  workRanges: { startTime: string; endTime: string }[];
  breaks: { startTime: string; endTime: string }[];
  /** Lista godzin startu slotów (HH:mm) — używana tylko w trybie FixedStartTimes. */
  fixedStartTimes: string[];
}

@Component({
  selector: 'app-weekly-schedule',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    WeekDayCardComponent,
    Button,
    FormsModule,
    ToggleButtonModule,
    ToggleSwitch,
    SelectButtonModule,
    FormFieldCalendarComponent,
    Menu,
    DirtyFormBeforeUnloadDirective,
  ],
  template: `
  <div class="min-h-full bg-surface-50 dark:bg-surface-950" [appDirtyFormBeforeUnload]="hasUnsavedChanges()">
    <div class="max-w-3xl mx-auto px-4 sm:px-6 py-8 sm:py-10">
      <nav class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1">
        <a [routerLink]="hubLink()" class="hover:text-primary transition-colors">{{ hubLabel() }}</a>
        <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
        <a [routerLink]="schedulesLink()" class="hover:text-primary transition-colors">Grafiki powtarzalne</a>
        <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
        <span class="text-surface-700 dark:text-surface-300">{{ headingLabel() }}</span>
      </nav>

      <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 mb-8">
        <div>
          <h1 class="text-2xl sm:text-3xl font-bold text-surface-900 tracking-tight mb-2">
            {{ headingLabel() }}
          </h1>
          <p class="text-surface-600 dark:text-surface-400 text-sm sm:text-base max-w-xl leading-relaxed">
            Ustaw godziny pracy w każdym dniu tygodnia — powtórzą się automatycznie co tydzień.
            To właśnie te godziny klienci widzą jako wolne terminy do rezerwacji.
          </p>
        </div>

        <div class="flex flex-col sm:flex-row gap-2 sm:gap-3 shrink-0 w-full sm:w-auto">
          <p-button
            label="Wróć"
            severity="secondary"
            [outlined]="true"
            styleClass="w-full sm:w-auto"
            [disabled]="schedulesData.isLoading() || saving()"
            (onClick)="goBack()"
          />
          <p-button
            data-testid="schedule-save"
            data-tour="schedule-save"
            label="Zapisz"
            icon="pi pi-check"
            styleClass="w-full sm:w-auto font-semibold"
            [loading]="saving()"
            [disabled]="schedulesData.isLoading()"
            (onClick)="updateWeeklySchedule()"
          />
        </div>
      </div>

      <div class="rounded-2xl border border-surface-200/80 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-5 shadow-sm mb-6 sm:mb-8">
        <div class="flex items-center gap-4">
          <div
            class="shrink-0 w-14 h-14 rounded-xl border-2 border-primary/30 bg-primary/5 flex items-center justify-center text-lg font-bold text-primary"
            aria-hidden="true"
          >
            {{ employeeInitials() }}
          </div>
          <div class="min-w-0 flex-1">
            <p class="font-bold text-surface-900 text-base sm:text-lg truncate">
              {{ employeeDisplayName() }}
            </p>
            <p class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 truncate">
              {{ employeeSubtitle() }}
            </p>
          </div>
        </div>
      </div>

      <div class="rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-6 shadow-sm mb-6 sm:mb-8">
        <h2 class="text-base font-bold text-surface-900 mb-3">Sposób ustalania godzin</h2>
        <p-selectbutton
          data-testid="slot-mode-toggle"
          data-tour="slot-mode"
          [options]="slotModeOptions"
          [ngModel]="slotGenerationMode()"
          (ngModelChange)="onChangeSlotMode($event)"
          optionLabel="label"
          optionValue="value"
          [allowEmpty]="false"
          ariaLabel="Sposób ustalania godzin"
        />
        <p class="text-xs text-surface-500 dark:text-surface-400 mt-2 leading-relaxed">
          @if (slotGenerationMode() === SlotGenerationMode.FixedStartTimes) {
            <strong>Ustalone godziny</strong> — klient wybiera tylko z godzin, które sam(a) wpisujesz (np. 9:00, 12:00, 15:00).
            To są dokładnie te godziny, na które klient może się zapisać.
          } @else {
            <strong>Elastyczne godziny</strong> — klient wybiera dowolną wolną godzinę w Twoich blokach pracy; sloty tworzą się automatycznie (np. co 30 min między 9:00 a 17:00).
          }
        </p>
      </div>

      <div class="rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-6 shadow-sm mb-6 sm:mb-8">
        <h2 class="text-base font-bold text-surface-900 mb-4">Obowiązywanie grafiku</h2>
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
          <app-form-field-calendar
            id="active-from"
            label="Obowiązuje od"
            testId="schedule-active-from"
            [value]="activeFrom()"
            (valueChange)="onActiveFromChange($event)"
          />
          @if (!indefinite()) {
            <app-form-field-calendar
              id="active-to"
              label="Obowiązuje do"
              testId="schedule-active-to"
              [value]="activeTo()"
              (valueChange)="onActiveToChange($event)"
            />
          }
        </div>
        <div class="mt-3 flex items-center gap-2">
          <p-toggleswitch
            inputId="schedule-indefinite"
            [ngModel]="indefinite()"
            (ngModelChange)="onIndefiniteToggle($event)"
          />
          <label for="schedule-indefinite" class="text-sm text-surface-700 dark:text-surface-300 select-none cursor-pointer">
            Bezterminowo (do odwołania)
          </label>
        </div>
        @if (indefinite()) {
          <p class="text-xs text-surface-500 dark:text-surface-400 mt-1.5">
            Grafik działa od podanej daty i powtarza się bezterminowo. Wyłącz, aby wskazać dzień, w którym ma się skończyć.
          </p>
        }

        <div class="mt-4 pt-4 border-t border-surface-200/70 dark:border-surface-100">
          <div class="flex items-center gap-2">
            <p-toggleswitch
              inputId="schedule-active"
              [ngModel]="isActive()"
              (ngModelChange)="onActiveToggle($event)"
            />
            <label for="schedule-active" class="text-sm text-surface-700 dark:text-surface-300 select-none cursor-pointer">
              Grafik aktywny
            </label>
          </div>
          @if (!isActive()) {
            <p class="text-xs text-amber-700 dark:text-amber-300 mt-1.5">
              Grafik zapisany jako nieaktywny — nie generuje wolnych terminów. Włączysz go później na liście grafików.
            </p>
          }
        </div>

        <details class="mt-5 group" [attr.open]="numberOfCycles() > 1 ? '' : null">
          <summary class="cursor-pointer list-none flex items-center gap-2 text-sm font-bold text-surface-700 dark:text-surface-300 select-none py-1">
            <i class="pi pi-cog text-xs"></i>
            <span class="flex-1">Zaawansowane — cykl wielotygodniowy</span>
            <i class="pi pi-chevron-down text-[10px] transition-transform group-open:rotate-180"></i>
          </summary>
          <div class="mt-3 pt-3 border-t border-surface-200/70 dark:border-surface-100">
            <label class="text-sm font-bold text-surface-800 block mb-2">
              Liczba tygodni w cyklu
            </label>
            <p-selectbutton
              [options]="cycleOptions"
              [ngModel]="numberOfCycles()"
              (ngModelChange)="onChangeCycles($event)"
              optionLabel="label"
              optionValue="value"
              [allowEmpty]="false"
              ariaLabelledBy="cycles-label"
            />
            <p class="text-xs text-surface-500 dark:text-surface-400 mt-2 leading-relaxed">
              Użyj 2+ tylko gdy pracujesz w cyklu rotacyjnym (np. „tydzień A: poranki, tydzień B: popołudnia").
              Dla stałych godzin zostaw <strong>1</strong>.
            </p>
          </div>
        </details>
      </div>

      @if (templatesForMode().length > 0) {
        <div class="rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-5 shadow-sm mb-6 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
          <div class="min-w-0">
            <h2 class="text-base font-bold text-surface-900">Szablony</h2>
            <p class="text-xs text-surface-500 dark:text-surface-400 mt-0.5">
              Wypełnij jednym kliknięciem wszystkie dni robocze tego grafiku.
            </p>
          </div>
          <p-button
            label="Zastosuj do wszystkich dni roboczych"
            icon="pi pi-bolt"
            severity="secondary"
            [outlined]="true"
            data-testid="apply-template-all"
            styleClass="w-full sm:w-auto shrink-0"
            (onClick)="applyAllMenu.toggle($event)"
          />
          <p-menu #applyAllMenu [popup]="true" [model]="applyAllMenuItems()" styleClass="shift-template-menu" [appendTo]="'body'" />
        </div>
      }

      @if (erroredDays().length > 0) {
        <div role="alert" class="rounded-2xl border border-red-300 dark:border-red-700/60 bg-red-50 dark:bg-red-950/30 p-4 mb-6">
          <div class="flex items-start gap-2 mb-2.5 text-sm font-bold text-red-800 dark:text-red-200">
            <i class="pi pi-exclamation-triangle text-xs mt-0.5"></i>
            <span>Popraw dni z błędami przed zapisem:</span>
          </div>
          <div class="flex flex-wrap gap-2">
            @for (e of erroredDays(); track e.key) {
              <button
                type="button"
                class="inline-flex items-center gap-1.5 rounded-full border border-red-300 dark:border-red-700 bg-white/70 dark:bg-red-950/40 px-3 py-1 text-xs font-semibold text-red-700 dark:text-red-200 hover:bg-red-100 dark:hover:bg-red-900/40 transition-colors"
                (click)="scrollToDay(e.dayKey, e.weekIndex)"
              >
                <i class="pi pi-arrow-down text-[10px]" aria-hidden="true"></i>
                {{ e.label }}@if (numberOfCycles() > 1) {<span class="opacity-70">&nbsp;· T{{ e.weekIndex + 1 }}</span>}
              </button>
            }
          </div>
        </div>
      }

      <div data-tour="schedule-days">
      @for (week of weekGroups(); track week.weekIndex) {
        <div class="mb-3 sm:mb-4">
          <h3 class="text-xs sm:text-sm font-bold uppercase tracking-wider text-surface-500 dark:text-surface-400 mb-2 px-1">
            Tydzień {{ week.weekIndex + 1 }} z {{ numberOfCycles() }}
          </h3>
          <div class="flex flex-col gap-4 sm:gap-5">
            @for (day of week.days; track day.dayKey + '-' + day.weekIndex) {
              <week-day-card
                [day]="day"
                [mode]="slotGenerationMode()"
                [templates]="templatesForMode()"
                [validationError]="dayErrors()[day.dayKey + '-' + day.weekIndex]"
                (changedDay)="updateDay($event)"
                (copyToOthers)="copyDayToOthers($event)"
              />
            }
          </div>
        </div>
      }

      @if (weekGroups().length === 0) {
        <div class="mt-8 rounded-2xl border-2 border-dashed border-surface-200 dark:border-surface-100 p-10 text-center bg-surface-0 dark:bg-surface-50/60">
          <p class="text-surface-600 dark:text-surface-400">Brak danych grafiku do wyświetlenia.</p>
        </div>
      }
      </div>

      <div class="sticky bottom-0 z-20 -mx-4 sm:-mx-6 mt-6 border-t border-surface-200/80 dark:border-surface-100 bg-surface-50/85 dark:bg-surface-950/85 backdrop-blur px-4 sm:px-6 py-3">
        <div class="flex items-center gap-3">
          <p-button
            label="Wróć"
            severity="secondary"
            [outlined]="true"
            styleClass="shrink-0"
            [disabled]="saving()"
            (onClick)="goBack()"
          />
          <p-button
            data-testid="schedule-save-sticky"
            data-tour="schedule-save"
            label="Zapisz grafik"
            icon="pi pi-check"
            styleClass="flex-1 sm:flex-none sm:ml-auto font-semibold"
            [loading]="saving()"
            [disabled]="schedulesData.isLoading()"
            (onClick)="updateWeeklySchedule()"
          />
        </div>
      </div>
    </div>
  </div>
`,
})
export class WeeklyScheduleComponent implements HasUnsavedChanges {
  private employeesService = inject(EmployeesClient);
  private shiftTemplatesClient = inject(ShiftTemplatesClient);
  private auth = inject(AuthSessionService);
  private messageService = inject(MessageService);
  private location = inject(Location);
  private router = inject(Router);
  private readonly document = inject(DOCUMENT);

  id = input.required<string>();
  /** Opcjonalnie — przy edycji konkretnego grafiku po stronie listy. */
  scheduleId = input<string | undefined>(undefined);

  saving = signal(false);

  /** Czy formularz ma niezapisane zmiany (dla dirtyFormGuard + beforeunload). */
  private dirty = signal(false);

  /** Błędy walidacji per dzień (klucz = `${dayKey}-${weekIndex}`). Czyszczone przy edycji dnia. */
  dayErrors = signal<Record<string, string>>({});

  /** Dni z błędem walidacji (do banera podsumowania + skoków do karty). */
  protected erroredDays = computed(() =>
    Object.keys(this.dayErrors()).map((key) => {
      const idx = key.lastIndexOf('-');
      const dayKey = key.slice(0, idx);
      const weekIndex = Number(key.slice(idx + 1));
      const label = DAY_MAPPING.find((d) => d.key === dayKey)?.label ?? dayKey;
      return { key, dayKey, weekIndex, label };
    }),
  );

  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

  protected readonly cycleOptions = [
    { label: '1', value: 1 },
    { label: '2', value: 2 },
    { label: '3', value: 3 },
    { label: '4', value: 4 },
  ];

  /** Eksponujemy enum do szablonu. */
  protected readonly SlotGenerationMode = SlotGenerationMode;

  protected readonly slotModeOptions = [
    { label: 'Elastyczne godziny', value: SlotGenerationMode.Grid },
    { label: 'Ustalone godziny', value: SlotGenerationMode.FixedStartTimes },
  ];

  private isSelfMode = computed(() => this.router.url.startsWith('/admin/my-availability/'));
  protected hubLabel = computed(() => (this.isSelfMode() ? 'Moja dostępność' : 'Zarządzanie'));
  protected hubLink = computed(() =>
    this.isSelfMode() ? ['/admin/my-availability', this.id()] : '/admin/resources',
  );
  protected schedulesLink = computed(() => [
    this.isSelfMode() ? '/admin/my-availability' : '/admin/resources/employees',
    this.id(),
    'schedules',
  ]);

  employeeData = rxResource({
    stream: () => this.employeesService.getEmployee(this.id()),
  });

  schedulesData = rxResource({
    stream: () => this.employeesService.getEmployeeSchedules(this.id()),
  });

  /**
   * Szablony zmian to zasób salonu — API zwraca je tylko dla `StaffManagement`. Pracownik ich nie
   * pobiera (dostałby 403); pusta lista zwija UI „zastosuj szablon" w kartach dni.
   */
  private canUseTemplates = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager';
  });

  templatesData = rxResource({
    params: () => this.canUseTemplates(),
    stream: ({ params: allowed }) =>
      allowed ? this.shiftTemplatesClient.getShiftTemplates() : of([] as ShiftTemplateDto[]),
  });

  shiftTemplates = computed(() => this.templatesData.value() ?? []);

  /** Szablony pasujące do trybu aktualnego grafiku (stałe godziny vs przedziały). */
  templatesForMode = computed(() =>
    this.shiftTemplates().filter(
      (t) => (t.slotGenerationMode ?? SlotGenerationMode.Grid) === this.slotGenerationMode(),
    ),
  );

  /** Pozycje menu „Zastosuj do wszystkich dni roboczych". */
  applyAllMenuItems = computed<MenuItem[]>(() =>
    this.templatesForMode().map((t) => ({
      label: t.name ?? 'Szablon',
      icon: 'pi pi-clock',
      command: () => this.applyTemplateToAll(t),
    })),
  );

  /** Wybrany do edycji grafik (po `scheduleId`). Gdy brak — tworzymy nowy. */
  editingSchedule = computed<EmployeeScheduleDto | undefined>(() => {
    const list = this.schedulesData.value() ?? [];
    const targetId = this.scheduleId();
    if (!targetId) return undefined;
    return list.find((s) => s.id === targetId);
  });

  /** Tryb: 'create' albo 'edit'. */
  protected mode = computed<'create' | 'edit'>(() =>
    this.scheduleId() ? 'edit' : 'create',
  );

  protected headingLabel = computed(() =>
    this.mode() === 'edit' ? 'Edytuj grafik powtarzalny' : 'Nowy grafik powtarzalny',
  );

  activeFrom = linkedSignal<EmployeeScheduleDto | undefined, string | null>({
    source: () => this.editingSchedule(),
    computation: (s) => this.coerceYyyyMmDd(s?.activeFrom) ?? todayYyyyMmDd(),
  });

  /** Stan UI „Bezterminowo" — wykrywa sentinel 9999-12-31. W trybie tworzenia domyślnie ON (typowe dla SOLO). */
  indefinite = linkedSignal<EmployeeScheduleDto | undefined, boolean>({
    source: () => this.editingSchedule(),
    computation: (s) => {
      if (!s) return true;
      const val = this.coerceYyyyMmDd(s?.activeTo);
      return !!val && val >= '9999-12-30';
    },
  });

  activeTo = linkedSignal<EmployeeScheduleDto | undefined, string | null>({
    source: () => this.editingSchedule(),
    computation: (s) => {
      if (!s) return null;
      const val = this.coerceYyyyMmDd(s?.activeTo);
      if (val && val >= '9999-12-30') return null;
      return val ?? todayPlusYearsYyyyMmDd(5);
    },
  });

  numberOfCycles = linkedSignal({
    source: () => this.editingSchedule(),
    computation: (s) => Math.max(1, Math.min(4, s?.numberOfCycles ?? 1)),
  });

  /** Czy grafik jest aktywny. Nowy grafik domyślnie aktywny; można zapisać jako nieaktywny „szkic". */
  isActive = linkedSignal<EmployeeScheduleDto | undefined, boolean>({
    source: () => this.editingSchedule(),
    computation: (s) => s?.isActive ?? true,
  });

  /** Tryb generowania slotów. Ładowany z grafiku, domyślnie Grid. */
  slotGenerationMode = linkedSignal<EmployeeScheduleDto | undefined, SlotGenerationMode>({
    source: () => this.editingSchedule(),
    computation: (s) => s?.slotGenerationMode ?? SlotGenerationMode.Grid,
  });

  /** Płaska lista dni z aktualnym numberOfCycles, prefilled z editingSchedule. */
  daysFlat = linkedSignal({
    source: () => ({ schedule: this.editingSchedule(), cycles: this.numberOfCycles() }),
    computation: (src) => this.buildDaysGrid(src.schedule, src.cycles),
  });

  weekGroups = computed(() => {
    const days = this.daysFlat();
    const cycles = this.numberOfCycles();
    const groups: { weekIndex: number; days: DayScheduleUi[] }[] = [];
    for (let w = 0; w < cycles; w++) {
      groups.push({ weekIndex: w, days: days.filter((d) => d.weekIndex === w) });
    }
    return groups;
  });

  employeeDisplayName = computed(() => {
    const e = this.employeeData.value();
    if (!e) return 'Pracownik';
    const parts = [e.firstName, e.lastName].filter(Boolean);
    return parts.length ? parts.join(' ') : 'Pracownik';
  });

  employeeSubtitle = computed(() => {
    const e = this.employeeData.value();
    if (!e?.email) return 'Grafik pracy';
    return e.email;
  });

  employeeInitials = computed(() => {
    const e = this.employeeData.value();
    if (!e) return '?';
    const a = (e.firstName?.trim()?.[0] ?? '').toUpperCase();
    const b = (e.lastName?.trim()?.[0] ?? '').toUpperCase();
    const initials = `${a}${b}`.trim();
    return initials || '?';
  });

  onChangeCycles(value: number) {
    const safe = Math.max(1, Math.min(4, value));
    this.numberOfCycles.set(safe);
    this.dirty.set(true);
  }

  onChangeSlotMode(mode: SlotGenerationMode) {
    this.slotGenerationMode.set(mode);
    // Zmiana trybu unieważnia błędy walidacji liczone wg poprzedniej logiki.
    this.dayErrors.set({});
    this.dirty.set(true);
  }

  onIndefiniteToggle(enabled: boolean) {
    this.indefinite.set(enabled);
    if (enabled) {
      this.activeTo.set(null);
    } else if (!this.activeTo()) {
      this.activeTo.set(endOfThisYearYyyyMmDd());
    }
    this.dirty.set(true);
  }

  onActiveToggle(enabled: boolean) {
    this.isActive.set(enabled);
    this.dirty.set(true);
  }

  onActiveFromChange(value: string | null) {
    // p-date-picker emituje wartość także przy inicjalizacji (normalizacja) — ignoruj no-op,
    // inaczej flaga niezapisanych zmian zapala się bez faktycznej edycji.
    if (value === this.activeFrom()) return;
    this.activeFrom.set(value);
    this.dirty.set(true);
  }

  onActiveToChange(value: string | null) {
    if (value === this.activeTo()) return;
    this.activeTo.set(value);
    this.dirty.set(true);
  }

  updateDay(updatedDay: DayScheduleUi) {
    this.dirty.set(true);
    this.daysFlat.update((current) =>
      current.map((day) =>
        day.dayKey === updatedDay.dayKey && day.weekIndex === updatedDay.weekIndex
          ? updatedDay
          : day,
      ),
    );
    // Czyść błąd dla edytowanego dnia — user wie, że poprawia, ponowna walidacja przy zapisie.
    const key = `${updatedDay.dayKey}-${updatedDay.weekIndex}`;
    if (this.dayErrors()[key]) {
      this.dayErrors.update((prev) => {
        const next = { ...prev };
        delete next[key];
        return next;
      });
    }
  }

  /** Zastosuj szablon do wszystkich dni roboczych (zgodnie z trybem grafiku). Nie tworzy nowych dni roboczych. */
  applyTemplateToAll(tpl: ShiftTemplateDto) {
    const toHm = (t: string | undefined) => (t ? t.substring(0, 5) : '');
    const isFixed = this.slotGenerationMode() === SlotGenerationMode.FixedStartTimes;

    const fixedStartTimes = (tpl.fixedStartTimes ?? []).filter((t) => !!t).map((t) => toHm(t));
    const workRanges = (tpl.workRanges ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => ({ startTime: toHm(r.startTime), endTime: toHm(r.endTime) }));
    const breaks = (tpl.breaks ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => ({ startTime: toHm(r.startTime), endTime: toHm(r.endTime) }));

    if (isFixed ? fixedStartTimes.length === 0 : workRanges.length === 0) {
      return;
    }

    this.daysFlat.update((current) =>
      current.map((day) =>
        day.isWorking
          ? isFixed
            ? { ...day, fixedStartTimes: [...fixedStartTimes] }
            : { ...day, workRanges: workRanges.map((r) => ({ ...r })), breaks: breaks.map((b) => ({ ...b })) }
          : day,
      ),
    );
    this.dayErrors.set({});
    this.dirty.set(true);
    this.messageService.add({
      severity: 'success',
      summary: 'Zastosowano szablon',
      detail: `„${tpl.name}" w dniach roboczych. Pamiętaj, aby zapisać grafik.`,
      life: 4000,
    });
  }

  /** Kopiuje godziny z danego dnia do pozostałych dni ROBOCZYCH w tym samym tygodniu cyklu. */
  copyDayToOthers(source: DayScheduleUi) {
    const isFixed = this.slotGenerationMode() === SlotGenerationMode.FixedStartTimes;
    const hasContent = isFixed ? source.fixedStartTimes.length > 0 : source.workRanges.length > 0;
    if (!hasContent) return;

    let copied = 0;
    this.daysFlat.update((current) =>
      current.map((day) => {
        const isOtherWorkingDayInWeek =
          day.weekIndex === source.weekIndex && day.dayKey !== source.dayKey && day.isWorking;
        if (!isOtherWorkingDayInWeek) return day;
        copied++;
        return isFixed
          ? { ...day, fixedStartTimes: [...source.fixedStartTimes] }
          : {
              ...day,
              workRanges: source.workRanges.map((r) => ({ ...r })),
              breaks: source.breaks.map((b) => ({ ...b })),
            };
      }),
    );

    if (copied === 0) {
      this.messageService.add({
        severity: 'info',
        summary: 'Brak dni do skopiowania',
        detail: 'Włącz inne dni jako robocze, aby skopiować do nich te godziny.',
        life: 4000,
      });
      return;
    }

    this.dayErrors.set({});
    this.dirty.set(true);
    this.messageService.add({
      severity: 'success',
      summary: 'Skopiowano godziny',
      detail: `Z dnia „${source.dayName}" do pozostałych dni roboczych (${copied}). Pamiętaj, aby zapisać.`,
      life: 4000,
    });
  }

  /** Przewija widok do karty konkretnego dnia (użycie: baner błędów). */
  scrollToDay(dayKey: string, weekIndex: number) {
    const el = this.document.querySelector(`[data-testid="day-card-${dayKey}-${weekIndex}"]`);
    el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  private scrollToFirstError() {
    const first = this.erroredDays()[0];
    if (!first) return;
    // Poczekaj na render karty z błędem przed skokiem.
    setTimeout(() => this.scrollToDay(first.dayKey, first.weekIndex), 50);
  }

  updateWeeklySchedule() {
    const ui = this.daysFlat();
    const fromVal = (this.activeFrom() ?? '').trim();
    const isIndefinite = this.indefinite();
    const toVal = isIndefinite ? INDEFINITE_ACTIVE_TO : (this.activeTo() ?? '').trim();

    if (!fromVal || !toVal) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Brak dat',
        detail: 'Uzupełnij daty obowiązywania grafiku.',
        life: 4000,
      });
      return;
    }
    const { globalError, dayErrors } = this.validateSchedule(ui, fromVal, toVal);
    this.dayErrors.set(dayErrors);
    if (globalError) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Nieprawidłowy grafik',
        detail: globalError,
        life: 5000,
      });
      return;
    }
    if (Object.keys(dayErrors).length > 0) {
      const names = this.erroredDays()
        .map((e) => (this.numberOfCycles() > 1 ? `${e.label} (T${e.weekIndex + 1})` : e.label))
        .join(', ');
      this.messageService.add({
        severity: 'warn',
        summary: 'Popraw dni z błędami',
        detail: `Sprawdź: ${names}. Szczegóły pod każdą kartą.`,
        life: 5000,
      });
      this.scrollToFirstError();
      return;
    }

    const dto = this.mapUiToDto(ui, fromVal, toVal);
    if (!dto) {
      return;
    }

    this.saving.set(true);
    this.employeesService.setEmployeeSchedule(this.id(), dto).subscribe({
      next: () => {
        this.saving.set(false);
        this.dirty.set(false);
        this.messageService.add({
          severity: 'success',
          summary: 'Zapisano',
          detail: 'Grafik został zapisany.',
          life: 4000,
        });
        this.schedulesData.reload();
        if (this.mode() === 'create') {
          void this.router.navigate(this.schedulesLink());
        }
      },
      error: () => {
        this.saving.set(false);
      },
    });
  }

  goBack() {
    safeBackWith(this.location, this.router, this.hubLink() as string | string[]);
  }

  private buildDaysGrid(
    schedule: EmployeeScheduleDto | undefined,
    cycles: number,
  ): DayScheduleUi[] {
    const map = new Map<number, EmployeeScheduleDayDto>();
    for (const d of schedule?.days ?? []) {
      if (d.cycleIndex == null) continue;
      map.set(d.cycleIndex, d);
    }

    // Smart defaults dla nowego grafiku (create mode): Pn-Pt 9-17, Sb-Nd wolne.
    // Większość SOLO ownerów pracuje standardowo i tylko korekta minimalnie.
    const isCreateMode = !schedule;
    const WEEKDAY_DEFAULT_KEYS = new Set<string>(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']);
    const DEFAULT_WORK_RANGE = { startTime: '09:00', endTime: '17:00' };

    const result: DayScheduleUi[] = [];
    for (let w = 0; w < cycles; w++) {
      for (const cfg of DAY_MAPPING) {
        const cycleIndex = w * 7 + DAY_OF_WEEK_INDEX[cfg.key];
        const day = map.get(cycleIndex);
        const workRanges = (day?.workRanges ?? [])
          .filter((r) => !!r?.startTime && !!r?.endTime)
          .map((r) => ({
            startTime: this.toHm(r.startTime),
            endTime: this.toHm(r.endTime),
          }));
        const breaks = (day?.breaks ?? [])
          .filter((b) => !!b?.startTime && !!b?.endTime)
          .map((b) => ({
            startTime: this.toHm(b.startTime),
            endTime: this.toHm(b.endTime),
          }));

        const fixedStartTimes = (day?.fixedStartTimes ?? [])
          .filter((t) => !!t)
          .map((t) => this.toHm(t));

        // Preset tylko w pierwszym tygodniu cyklu — w 2-4 tygodniowych zostawiamy puste.
        const applyDefault = isCreateMode && w === 0 && WEEKDAY_DEFAULT_KEYS.has(cfg.key) && workRanges.length === 0;
        const finalWorkRanges = applyDefault ? [{ ...DEFAULT_WORK_RANGE }] : workRanges;

        // Tryb FixedStartTimes: dzień pracujący gdy ma jakiekolwiek godziny startu.
        const isFixedMode = (schedule?.slotGenerationMode ?? SlotGenerationMode.Grid) === SlotGenerationMode.FixedStartTimes;
        const isWorking = isFixedMode ? fixedStartTimes.length > 0 : finalWorkRanges.length > 0;

        result.push({
          dayKey: cfg.key,
          dayName: cfg.label,
          weekIndex: w,
          isWorking,
          workRanges: finalWorkRanges,
          breaks,
          fixedStartTimes,
        });
      }
    }
    return result;
  }

  private mapUiToDto(ui: DayScheduleUi[], fromVal: string, toVal: string): EmployeeScheduleDto | null {
    const cycles = this.numberOfCycles();
    if (!fromVal || !toVal) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Brak dat',
        detail: 'Uzupełnij daty obowiązywania grafiku.',
        life: 4000,
      });
      return null;
    }

    const mode = this.slotGenerationMode();
    const days: EmployeeScheduleDayDto[] = [];

    ui.forEach((day) => {
      if (!day.isWorking) {
        return;
      }
      const cycleIndex = day.weekIndex * 7 + DAY_OF_WEEK_INDEX[day.dayKey as DayKey];

      if (mode === SlotGenerationMode.FixedStartTimes) {
        // Sloty są grafikiem: tylko godziny startu, bez bloków/przerw.
        const fixedStartTimes = Array.from(
          new Set(day.fixedStartTimes.map((t) => t?.trim()).filter((t): t is string => !!t)),
        )
          .sort((a, b) => a.localeCompare(b))
          .map((t) => `${t}:00`);

        if (!fixedStartTimes.length) {
          return;
        }

        days.push({
          cycleIndex,
          workRanges: [],
          breaks: [],
          fixedStartTimes,
        });
        return;
      }

      const workRanges = day.workRanges
        .filter((r) => r.startTime?.trim() && r.endTime?.trim())
        .map((r) => ({
          startTime: `${r.startTime}:00`,
          endTime: `${r.endTime}:00`,
        } satisfies TimeRangeDto));

      if (!workRanges.length) {
        return;
      }

      const breaks = day.breaks
        .filter((b) => b.startTime?.trim() && b.endTime?.trim())
        .map((b) => ({
          startTime: `${b.startTime}:00`,
          endTime: `${b.endTime}:00`,
        } satisfies TimeRangeDto));

      days.push({
        cycleIndex,
        workRanges,
        breaks,
      });
    });

    return {
      activeFrom: fromVal as unknown as Date,
      activeTo: toVal as unknown as Date,
      numberOfCycles: cycles,
      days,
      slotGenerationMode: mode,
      id: this.scheduleId(),
      isActive: this.isActive(),
    };
  }

  /**
   * Waliduje grafik. Zwraca `globalError` (daty, ogólne) lub `dayErrors` (per-day, klucz = `${dayKey}-${weekIndex}`).
   * Pierwszy napotkany błąd w danym dniu jest raportowany — kolejne ignorowane do następnej walidacji.
   */
  private validateSchedule(ui: DayScheduleUi[], fromVal: string, toVal: string): { globalError: string | null; dayErrors: Record<string, string> } {
    const dayErrors: Record<string, string> = {};

    if (!fromVal || !toVal) {
      return { globalError: 'Uzupełnij daty obowiązywania grafiku.', dayErrors };
    }
    if (fromVal > toVal) {
      return { globalError: 'Data końca musi być późniejsza niż data początku.', dayErrors };
    }

    const isFixedMode = this.slotGenerationMode() === SlotGenerationMode.FixedStartTimes;

    for (const day of ui) {
      if (!day.isWorking) continue;
      const key = `${day.dayKey}-${day.weekIndex}`;

      if (isFixedMode) {
        const times = day.fixedStartTimes.map((t) => t?.trim()).filter((t): t is string => !!t);
        if (!times.length) {
          dayErrors[key] = 'Dzień oznaczony jako roboczy — dodaj przynajmniej jedną godzinę startu.';
          continue;
        }
        if (times.some((t) => this.timeToMinutes(t) === null)) {
          dayErrors[key] = 'Nieprawidłowy format godziny.';
          continue;
        }
        if (new Set(times).size !== times.length) {
          dayErrors[key] = 'Godziny startu się powtarzają.';
          continue;
        }
        continue;
      }

      if (!day.workRanges.length) {
        dayErrors[key] = 'Dzień oznaczony jako roboczy — dodaj przynajmniej jeden blok pracy.';
        continue;
      }
      const workParsed = day.workRanges.map((b) => ({
        start: this.timeToMinutes(b.startTime),
        end: this.timeToMinutes(b.endTime),
      }));
      if (workParsed.some((r) => r.start === null || r.end === null)) {
        dayErrors[key] = 'Nieprawidłowy format godziny.';
        continue;
      }
      if (workParsed.some((r) => (r.start as number) >= (r.end as number))) {
        dayErrors[key] = 'Godzina zakończenia musi być późniejsza niż rozpoczęcia.';
        continue;
      }
      const sortedWork = [...workParsed].sort((a, b) => (a.start as number) - (b.start as number));
      let overlapFound = false;
      for (let i = 0; i < sortedWork.length - 1; i++) {
        if ((sortedWork[i].end as number) > (sortedWork[i + 1].start as number)) {
          dayErrors[key] = 'Bloki pracy nakładają się.';
          overlapFound = true;
          break;
        }
      }
      if (overlapFound) continue;

      // breaks: each must fit in some work range
      let breakError = false;
      for (const br of day.breaks) {
        const bs = this.timeToMinutes(br.startTime);
        const be = this.timeToMinutes(br.endTime);
        if (bs === null || be === null) {
          dayErrors[key] = 'Nieprawidłowy format godziny przerwy.';
          breakError = true;
          break;
        }
        if (bs >= be) {
          dayErrors[key] = 'Godzina zakończenia przerwy musi być późniejsza niż rozpoczęcia.';
          breakError = true;
          break;
        }
        const fits = workParsed.some(
          (r) => (r.start as number) <= bs && be <= (r.end as number),
        );
        if (!fits) {
          dayErrors[key] = `Przerwa ${br.startTime}-${br.endTime} nie mieści się w żadnym bloku pracy.`;
          breakError = true;
          break;
        }
      }
      if (breakError) continue;

      // breaks: no overlap
      const sortedBreaks = [...day.breaks].sort((a, b) =>
        (a.startTime ?? '').localeCompare(b.startTime ?? ''),
      );
      for (let i = 0; i < sortedBreaks.length - 1; i++) {
        const aEnd = this.timeToMinutes(sortedBreaks[i].endTime) as number;
        const bStart = this.timeToMinutes(sortedBreaks[i + 1].startTime) as number;
        if (aEnd > bStart) {
          dayErrors[key] = 'Przerwy nakładają się.';
          break;
        }
      }
    }
    return { globalError: null, dayErrors };
  }

  private timeToMinutes(hhmm: string): number | null {
    const m = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(hhmm ?? '');
    if (!m) return null;
    return Number(m[1]) * 60 + Number(m[2]);
  }

  private toHm(value: string | undefined): string {
    return value ? value.substring(0, 5) : '';
  }

  private coerceYyyyMmDd(value: Date | string | undefined): string | null {
    if (value == null) return null;
    if (value instanceof Date) {
      if (Number.isNaN(value.getTime())) return null;
      return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`;
    }
    const s = String(value).trim();
    if (!s) return null;
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) return s;
    const d = new Date(s);
    if (Number.isNaN(d.getTime())) return null;
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }
}
