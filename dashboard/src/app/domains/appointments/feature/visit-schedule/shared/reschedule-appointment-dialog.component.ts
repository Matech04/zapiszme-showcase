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
import { rxResource } from '@angular/core/rxjs-interop';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { InputNumber } from 'primeng/inputnumber';
import { MessageService } from 'primeng/api';
import {
  AppointmentPreviewDto,
  AppointmentSlotDto,
  AppointmentsClient,
  EmployeesClient,
  RescheduleAppointmentRequest,
  ServiceCategoriesClient,
  ServiceCategoryDto,
  ServicesClient,
} from '@core/api/api-client';
import {
  groupServicesByCategory,
  shouldShowCategoryHeaders,
} from '../../../data-access/service-category-groups.util';
import { formatYyyyMmDd, startOfDay } from './date-utils';
import { SlotPickerComponent } from './slot-picker.component';
import { AdminDrawerComponent } from './admin-drawer.component';
import { AppointmentDatePickerComponent } from './appointment-date-picker.component';
import { comboGroupKey, toggleServiceSelection } from './combo-select.util';

/** Opcja usługi w dialogu. `group` = grupa wariantów, `categoryId` = podział katalogowy. */
interface ServiceOption {
  label: string;
  value: string;
  group: string;
  categoryId: string | null;
  duration: number;
}

/**
 * Generowany klient z NSwag robi `date.toISOString()` przy serializacji query stringu
 * (np. `getAvailableSlots`), co dla lokalnego `new Date(yyyy, mm, dd)` daje pełen ISO
 * timestamp z UTC-offsetem (`2026-05-17T22:00:00.000Z` dla CEST przy lokalnej 17.05).
 * Backend `DateOnly` model binder odrzuca taki format jako niewalidny.
 *
 * Trik: przekazujemy obiekt który spełnia `Date`-like wymóg w generowanym kodzie
 * (`"" + date.toISOString()`), ale `toISOString` zwraca już gotowy `yyyy-MM-dd`.
 * Generowany kod NIE używa innych metod Date przy serializacji query — wystarczy
 * podstawienie tej jednej.
 */
function toDateOnlyApi(yyyyMmDd: string): Date {
  return { toISOString: () => yyyyMmDd } as unknown as Date;
}

/**
 * Wizard „Zaproponuj nowy termin" (F3.1) — modalny drawer uruchamiany z `appointment-detail-sheet`.
 *
 * Workflow:
 *  1. Otwierany z `appointment: AppointmentPreviewDto` (ma `id` + `employeeId`).
 *  2. Pobiera pełen `AppointmentDto` przez `getAppointmentById` aby uzyskać oryginalne
 *     `serviceId` (preview-dto nie zawiera go — backend zwraca uproszczoną wersję dla siatki).
 *  3. Formularz pozwala zmienić: pracownika, usługę, datę, slot. Defaulty = oryginalne wartości.
 *  4. Submit przez PATCH `/api/Appointments/{id}/reschedule` → emit `success`.
 *
 * Backend handler powiadamia klienta (publishuje `AppointmentRescheduledBySalonEvent`)
 * — nie ma trybu „self-service" tutaj, więc IsSelfService default=false.
 */
@Component({
  selector: 'app-reschedule-appointment-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    Select,
    DatePicker,
    InputNumber,
    SlotPickerComponent,
    AdminDrawerComponent,
    AppointmentDatePickerComponent,
  ],
  template: `
    <app-admin-drawer
      [isOpen]="isVisible()"
      [isDesktop]="isDesktop()"
      title="Zmień termin wizyty"
      label="Kalendarz"
      [closeable]="!isSubmitting()"
      (closeRequested)="onCancel()"
    >
      <div drawer-body>
        @if (appointment(); as a) {
          <div class="flex flex-col gap-5">
            <div
              class="rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-surface-50/70 dark:bg-surface-50/40 px-4 py-3"
            >
              <p class="admin-section-label text-primary">Obecny termin</p>
              <p class="text-sm font-bold text-surface-900 mt-0.5">
                {{ serviceLine(a) }}
              </p>
              <p class="text-xs text-surface-600 dark:text-surface-300 mt-0.5 font-mono">
                {{ currentDateLabel() }} • {{ shortTime(a.startTime) }} – {{ shortTime(a.endTime) }}
              </p>
            </div>

            <!-- Pracownik — selektor tylko gdy użytkownik może przepisać wizytę na innego
                 pracownika (Owner/Manager/TeamFull). Pracownik scoped (OwnCalendarOnly /
                 TeamReadOnly) widzi swoją wizytę jako pole tylko-do-odczytu. -->
            <div class="flex flex-col gap-1.5">
              <label class="admin-section-label">Pracownik</label>
              @if (allowEmployeeChange()) {
                <p-select
                  [options]="employeeOptions()"
                  [ngModel]="employeeId()"
                  (ngModelChange)="onEmployeeChange($event)"
                  placeholder="Wybierz pracownika"
                  [disabled]="isSubmitting()"
                  [fluid]="true"
                  appendTo="body"
                />
              } @else {
                <div
                  class="rounded-xl border border-surface-200/70 dark:border-surface-700/70 bg-surface-50/70 dark:bg-surface-900/40 px-3 py-2.5 text-sm font-bold text-surface-900"
                >
                  {{ selectedEmployeeLabel() || 'Twoja zmiana' }}
                </div>
              }
            </div>

            <!-- Usługi (combo — można edytować skład; max jedna z danej grupy wariantów) -->
            <div class="flex flex-col gap-1.5">
              <label class="admin-section-label">Usługi</label>
              @if (servicesResource.isLoading()) {
                <div class="h-10 bg-surface-100 dark:bg-surface-100 animate-pulse rounded-xl"></div>
              } @else if (serviceOptions().length === 0) {
                <p class="text-xs text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-950/40 rounded-xl px-3 py-2 border border-amber-200/80 dark:border-amber-800/60">
                  Pracownik nie ma przypisanych usług.
                </p>
              } @else {
                <div class="flex flex-col gap-3" data-testid="reschedule-service-chips">
                  @for (group of serviceGroups(); track group.key) {
                    <div class="flex flex-col gap-1.5">
                      @if (showCategoryHeaders()) {
                        <!-- Kreska odróżnia nagłówek sekcji od etykiety pola (obie są UPPERCASE). -->
                        <div class="flex items-center gap-2">
                          <span
                            class="text-[0.65rem] font-bold uppercase tracking-[0.12em] text-surface-500 dark:text-surface-400"
                            data-testid="reschedule-service-category"
                          >
                            {{ group.label }}
                          </span>
                          <span
                            class="h-px flex-1 bg-surface-200 dark:bg-surface-200"
                            aria-hidden="true"
                          ></span>
                        </div>
                      }
                      <div class="flex flex-wrap gap-2">
                        @for (opt of group.options; track opt.value) {
                          <button
                            type="button"
                            [attr.aria-pressed]="isServiceSelected(opt.value)"
                            [disabled]="isSubmitting() || isServiceDisabled(opt)"
                            (click)="toggleService(opt.value)"
                            [class.ring-2]="isServiceSelected(opt.value)"
                            [class.ring-primary]="isServiceSelected(opt.value)"
                            [class.bg-primary-50]="isServiceSelected(opt.value)"
                            [class.dark:bg-primary-900/20]="isServiceSelected(opt.value)"
                            [class.text-primary]="isServiceSelected(opt.value)"
                            [class.border-primary]="isServiceSelected(opt.value)"
                            [class.opacity-40]="isServiceDisabled(opt)"
                            class="px-3 py-1.5 rounded-full border border-surface-200 dark:border-surface-200 text-sm font-bold hover:border-primary transition-colors bg-surface-0 dark:bg-surface-50 text-surface-700 dark:text-surface-300 disabled:cursor-not-allowed"
                          >
                            {{ opt.label }}@if (isGroupAlternative(opt)) {<span class="ml-1 text-[10px] font-semibold text-amber-600">↺</span>}
                          </button>
                        }
                      </div>
                    </div>
                  }
                </div>
                @if (serviceIds().length > 0) {
                  <div class="flex flex-col gap-1.5">
                    <label class="admin-section-label">Czas trwania wizyty (min)</label>
                    <div class="flex items-center gap-2">
                      <p-inputnumber
                        [ngModel]="effectiveDurationMinutes()"
                        (ngModelChange)="onDurationChange($event)"
                        [disabled]="isSubmitting()"
                        [min]="1"
                        [max]="1440"
                        [step]="5"
                        [showButtons]="true"
                        [useGrouping]="false"
                        inputStyleClass="w-24"
                        data-testid="reschedule-duration-input"
                      />
                      @if (customDurationMinutes() !== null) {
                        <span class="text-xs font-semibold text-primary">
                          czas własny (standard {{ standardDurationMinutes() }} min)
                        </span>
                      }
                    </div>
                    <p class="text-xs text-surface-500 dark:text-surface-400">
                      Klientka w rezerwacji online widzi standardowy czas usługi.
                    </p>
                  </div>
                }
              }
            </div>

            <!-- Data -->
            <div class="flex flex-col gap-1.5">
              <label for="reschedule-date" class="admin-section-label">Nowa data</label>
              <app-appointment-date-picker
                inputId="reschedule-date"
                [employeeId]="employeeId()"
                [serviceId]="serviceIds().at(0) ?? ''"
                [value]="newDate()"
                [minDate]="minDate"
                [disabled]="isSubmitting()"
                (valueChange)="onDateChange($event)"
              />
            </div>

            <!-- Sloty / Godzina -->
            @if (offScheduleMode()) {
              <div class="flex items-start gap-3 rounded-xl border border-sky-300/80 dark:border-sky-700/60 bg-sky-50 dark:bg-sky-950/30 px-3 py-2.5" data-testid="off-schedule-banner">
                <i class="pi pi-calendar-times mt-0.5 text-sky-700 dark:text-sky-300" aria-hidden="true"></i>
                <div class="flex flex-col gap-0.5 flex-1 min-w-0">
                  <span class="text-sm font-semibold text-sky-900 dark:text-sky-100">Zmiana poza grafikiem</span>
                  <span class="text-xs text-sky-800/85 dark:text-sky-200/85 leading-relaxed">
                    Pomijamy grafik i godziny pracy — wpisz dowolną godzinę. Zablokuje tylko kolizja z inną wizytą.
                  </span>
                </div>
                <button
                  type="button"
                  (click)="setOffScheduleMode(false)"
                  [disabled]="isSubmitting()"
                  class="shrink-0 rounded-lg px-2 py-1 text-xs font-bold text-sky-800 dark:text-sky-200 hover:bg-sky-100/70 dark:hover:bg-sky-900/40 transition-colors disabled:opacity-50"
                  aria-label="Wyjdź z trybu zmiany poza grafikiem"
                >
                  Anuluj tryb
                </button>
              </div>
              <div class="flex flex-col gap-1.5">
                <label for="reschedule-offsch-time" class="admin-section-label">Godzina rozpoczęcia</label>
                <p-date-picker
                  inputId="reschedule-offsch-time"
                  [timeOnly]="true"
                  hourFormat="24"
                  [showIcon]="true"
                  [readonlyInput]="true"
                  [fluid]="true"
                  appendTo="body"
                  [ngModel]="selectedTimeAsDate()"
                  (ngModelChange)="onManualTimeChange($event)"
                  [disabled]="isSubmitting()"
                />
              </div>
            } @else if (employeeId() && serviceIds().length > 0 && newDate()) {
              <div class="flex flex-col gap-1.5">
                <label class="admin-section-label">Godzina</label>
                <app-slot-picker
                  [slots]="slotsResource.value()"
                  [value]="selectedSlot()"
                  [disabled]="isSubmitting()"
                  [loading]="slotsResource.isLoading() || appointmentDetail.isLoading()"
                  [loadError]="slotsLoadError()"
                  (valueChange)="onSlotPicked($event)"
                />
              </div>
              <button
                type="button"
                (click)="setOffScheduleMode(true)"
                [disabled]="isSubmitting()"
                class="self-start inline-flex items-center gap-1.5 text-xs font-medium text-surface-500 dark:text-surface-400 hover:text-sky-600 dark:hover:text-sky-400 transition-colors disabled:opacity-50"
                data-testid="off-schedule-toggle"
              >
                <i class="pi pi-calendar-times text-[11px]" aria-hidden="true"></i>
                Zmień termin poza grafikiem
              </button>
            }

            @if (submitError()) {
              <p
                class="rounded-xl bg-red-50 dark:bg-red-950/30 border border-red-200/70 dark:border-red-800/50 px-3 py-2 text-xs text-red-700 dark:text-red-300"
              >
                {{ submitError() }}
              </p>
            }
          </div>
        }
      </div>

      <div drawer-footer class="flex items-center justify-end gap-2">
        <button
          type="button"
          [disabled]="isSubmitting()"
          (click)="onCancel()"
          class="rounded-xl border border-surface-300 dark:border-surface-600 font-bold py-2 px-4 text-surface-700 hover:border-primary/45 transition-colors disabled:opacity-50"
        >
          Anuluj
        </button>
        <button
          type="button"
          [disabled]="!canSubmit() || isSubmitting()"
          (click)="onSubmit()"
          class="rounded-xl bg-surface-900 dark:bg-surface-100 text-surface-0 dark:text-surface-900 font-bold py-2 px-4 transition-opacity hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          @if (isSubmitting()) {
            <i class="pi pi-spin pi-spinner mr-2" aria-hidden="true"></i>
          }
          Zmień termin
        </button>
      </div>
    </app-admin-drawer>
  `,
})
export class RescheduleAppointmentDialogComponent {
  /** Wizyta do reschedule. `null` → drawer zamknięty. */
  readonly appointment = input<AppointmentPreviewDto | null>(null);
  readonly isDesktop = input<boolean>(false);
  /**
   * Czy użytkownik może przepisać wizytę na innego pracownika. Owner/Manager/TeamFull = true.
   * Pracownik scoped (OwnCalendarOnly / TeamReadOnly) = false → selektor pracownika zablokowany
   * (backend i tak odrzuciłby zmianę `employeeId` przez CanMutateEmployeeSchedule).
   */
  readonly allowEmployeeChange = input<boolean>(true);

  readonly closeRequested = output<void>();
  readonly success = output<string>();

  private readonly appointmentsClient = inject(AppointmentsClient);
  private readonly employeesClient = inject(EmployeesClient);
  private readonly servicesClient = inject(ServicesClient);
  private readonly serviceCategoriesClient = inject(ServiceCategoriesClient);
  private readonly messages = inject(MessageService);

  protected readonly isVisible = computed(() => this.appointment() != null);

  /** Wybrany pracownik (default = oryginalny pracownik wizyty). */
  protected readonly employeeId = signal<string>('');
  /** Wybrane usługi combo (default = istniejący skład wizyty, jeśli pracownik je oferuje). */
  protected readonly serviceIds = signal<string[]>([]);
  protected readonly MAX_COMBO_SERVICES = 5;
  /** Wybrana data w formacie `yyyy-MM-dd`. */
  protected readonly newDate = signal<string>('');
  protected readonly selectedSlot = signal<string | null>(null);
  /** Zmiana „poza grafikiem" — pomija grafik/godziny pracy, wysyła ignoreSchedule=true. */
  protected readonly offScheduleMode = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly slotsLoadError = signal(false);

  /**
   * Marker "użytkownik nie tknął jeszcze pracownika w tej sesji drawera".
   * Gdy true → preselect usługi celuje w oryginalną z `appointmentDetail`; po zmianie
   * pracownika auto-pick wybiera pierwszą dostępną.
   */
  private readonly originalEmployeeKept = signal(true);

  protected readonly minDate = startOfDay(new Date());

  protected readonly employeesResource = rxResource({
    stream: () => this.employeesClient.getEmployees(),
  });

  protected readonly servicesResource = rxResource({
    params: () => this.employeeId(),
    stream: ({ params: empId }) => {
      if (!empId) return of<ServiceOption[]>([]);
      return forkJoin({
        assigned: this.employeesClient.getEmployeeServices(empId),
        all: this.servicesClient.getServices(null),
      }).pipe(
        map(({ assigned, all }) => {
          // Iterujemy KATALOG (posortowany po OrderIndex, Name), nie listę przypisań —
          // `getEmployeeServices` nie ma OrderBy, więc dawałaby przypadkową kolejność.
          const assignedById = new Map(
            (assigned ?? []).filter((x) => x.serviceId).map((x) => [x.serviceId!, x]),
          );
          return (all ?? [])
            .filter((svc) => svc.id && assignedById.has(svc.id))
            .map((svc) => {
              const link = assignedById.get(svc.id!)!;
              return {
                label: svc.name ?? 'Usługa',
                value: svc.id!,
                group: comboGroupKey(svc.comboGroup),
                categoryId: svc.categoryId ?? null,
                // Czas per-pracownik (override EmployeeService lub katalog) — do sumy standardowej.
                duration: link.customDuration ?? svc.durationInMinutes ?? 0,
              } satisfies ServiceOption;
            });
        }),
        catchError(() => of<ServiceOption[]>([])),
      );
    },
  });

  /**
   * Pełen `AppointmentDto` — preview nie ma `serviceId`. Potrzebny do preselectu oryginalnej
   * usługi po otwarciu drawera.
   */
  protected readonly appointmentDetail = rxResource({
    params: () => this.appointment()?.id ?? undefined,
    stream: ({ params: id }) => {
      if (!id) return of(undefined);
      return this.appointmentsClient.getAppointmentById(id).pipe(
        catchError(() => of(undefined)),
      );
    },
  });

  protected readonly slotsResource = rxResource({
    params: () => {
      const emp = this.employeeId();
      const svc = this.serviceIds();
      const date = this.newDate();
      if (!emp || svc.length === 0 || !date) return undefined;
      return [date, emp, svc.join(',')].join('\x1e');
    },
    defaultValue: [] as AppointmentSlotDto[],
    stream: () => {
      const emp = this.employeeId();
      const svc = this.serviceIds();
      const date = this.newDate();
      if (!emp || svc.length === 0 || !date) return of([] as AppointmentSlotDto[]);
      this.slotsLoadError.set(false);
      return this.appointmentsClient
        .getAvailableSlots(toDateOnlyApi(date), emp, svc)
        .pipe(
          catchError(() => {
            this.slotsLoadError.set(true);
            return of([] as AppointmentSlotDto[]);
          }),
        );
    },
  });

  protected readonly employeeOptions = computed(() =>
    (this.employeesResource.value() ?? []).map((e) => ({
      label: [e.firstName, e.lastName].filter(Boolean).join(' ') || 'Pracownik',
      value: e.id!,
    })),
  );

  protected readonly serviceOptions = computed(() => this.servicesResource.value() ?? []);

  /** Kategorie katalogu — niezależne od pracownika, więc ładowane raz. Błąd → lista płaska. */
  protected readonly categoriesResource = rxResource({
    defaultValue: [] as ServiceCategoryDto[],
    stream: () =>
      this.serviceCategoriesClient
        .getServiceCategories()
        .pipe(catchError(() => of([] as ServiceCategoryDto[]))),
  });

  /** Usługi w sekcjach kategorii — układ listy wyboru. */
  protected readonly serviceGroups = computed(() =>
    groupServicesByCategory(this.serviceOptions(), this.categoriesResource.value() ?? []),
  );

  protected readonly showCategoryHeaders = computed(() =>
    shouldShowCategoryHeaders(this.serviceGroups()),
  );

  /** Suma standardowych czasów wybranych usług (minuty) — domyślna długość bloku. */
  protected readonly standardDurationMinutes = computed(() => {
    const selected = new Set(this.serviceIds());
    return this.serviceOptions()
      .filter((o) => selected.has(o.value))
      .reduce((sum, o) => sum + (o.duration ?? 0), 0);
  });

  /** Niestandardowy czas trwania (minuty) wpisany przez personel; `null` = użyj standardowej sumy. */
  protected readonly customDurationMinutes = signal<number | null>(null);

  protected readonly effectiveDurationMinutes = computed(
    () => this.customDurationMinutes() ?? this.standardDurationMinutes(),
  );

  protected onDurationChange(value: number | null): void {
    this.customDurationMinutes.set(
      value == null || value === this.standardDurationMinutes() ? null : value,
    );
  }

  /** Etykieta wybranego pracownika — pole tylko-do-odczytu, gdy `allowEmployeeChange` = false. */
  protected readonly selectedEmployeeLabel = computed(
    () => this.employeeOptions().find((o) => o.value === this.employeeId())?.label ?? '',
  );

  protected readonly canSubmit = computed(() => {
    if (
      !this.appointment()?.id ||
      !this.employeeId() ||
      this.serviceIds().length === 0 ||
      !this.newDate() ||
      !this.selectedSlot()
    ) {
      return false;
    }
    // Poza grafikiem godzina wpisywana ręcznie — wymagamy formatu HH:mm.
    if (this.offScheduleMode() && !/^\d{2}:\d{2}(:\d{2})?$/.test(this.selectedSlot() ?? '')) return false;
    return true;
  });

  /** Wybrana godzina (selectedSlot 'HH:mm') jako Date dla p-date-picker [timeOnly]. */
  protected readonly selectedTimeAsDate = computed<Date | null>(() => {
    const s = this.selectedSlot();
    if (!s) return null;
    const m = /^(\d{2}):(\d{2})(?::\d{2})?$/.exec(s);
    if (!m) return null;
    const d = new Date();
    d.setHours(Number(m[1]), Number(m[2]), 0, 0);
    return d;
  });

  protected isServiceSelected(id: string): boolean {
    return this.serviceIds().includes(id);
  }
  protected isGroupAlternative(opt: { value: string; group: string }): boolean {
    if (!opt.group || this.isServiceSelected(opt.value)) return false;
    return this.serviceOptions().some((o) => o.group === opt.group && this.isServiceSelected(o.value));
  }
  protected isServiceDisabled(opt: { value: string; group: string }): boolean {
    return (
      this.serviceIds().length >= this.MAX_COMBO_SERVICES &&
      !this.isServiceSelected(opt.value) &&
      !this.isGroupAlternative(opt)
    );
  }
  /** Przełącza usługę w combo (reguła grup wariantów). Zmiana składu czyści slot. */
  protected toggleService(id: string): void {
    const opts = this.serviceOptions();
    const groupOf = (x: string) => opts.find((o) => o.value === x)?.group ?? '';
    this.serviceIds.set(toggleServiceSelection(this.serviceIds(), id, groupOf, this.MAX_COMBO_SERVICES));
    this.selectedSlot.set(null);
    // Zmiana składu → pole czasu wraca do nowej standardowej sumy.
    this.customDurationMinutes.set(null);
  }

  constructor() {
    /**
     * Reset stanu na każdy `appointment` (otwarcie/zmiana). Pracownik startuje od oryginalnego —
     * usługę domyśli osobny efekt, gdy options pracownika się załadują.
     */
    effect(() => {
      const a = this.appointment();
      untracked(() => {
        this.offScheduleMode.set(false);
        if (!a) {
          this.employeeId.set('');
          this.serviceIds.set([]);
          this.newDate.set('');
          this.selectedSlot.set(null);
          this.submitError.set(null);
          this.originalEmployeeKept.set(true);
          return;
        }
        const dateRaw = a.date as unknown as string | Date | undefined;
        this.employeeId.set(a.employeeId ?? '');
        this.serviceIds.set([]);
        this.newDate.set(this.toIsoDate(dateRaw) ?? formatYyyyMmDd(new Date()));
        this.selectedSlot.set(null);
        this.submitError.set(null);
        this.originalEmployeeKept.set(true);
      });
    });

    /**
     * Preselect składu: gdy pracownika nie zmieniono — wybierz ISTNIEJĄCE usługi wizyty (combo lub
     * pojedynczą), o ile pracownik je oferuje. Po zmianie pracownika lub gdy oryginalne usługi
     * nie są dostępne — pierwsza z listy (staff może dobrać resztę ręcznie).
     */
    effect(() => {
      const options = this.serviceOptions();
      const detail = this.appointmentDetail.value();
      untracked(() => {
        if (this.serviceIds().length > 0 || options.length === 0) return;
        const optionIds = new Set(options.map((o) => o.value));

        if (this.originalEmployeeKept()) {
          const existing = (detail?.services ?? [])
            .map((s) => s.serviceId!)
            .filter((id) => optionIds.has(id));
          if (existing.length > 0) {
            this.serviceIds.set(existing);
            // Zachowaj bieżący niestandardowy czas w polu (prefill), gdy pracownik/usługi bez zmian.
            this.customDurationMinutes.set(detail?.customDurationMinutes ?? null);
            return;
          }
          if (detail?.serviceId && optionIds.has(detail.serviceId)) {
            this.serviceIds.set([detail.serviceId]);
            return;
          }
        }
        this.serviceIds.set([options[0].value]);
      });
    });
  }

  protected readonly currentDateLabel = computed(() => {
    const a = this.appointment();
    if (!a?.date) return '';
    const d = new Date(a.date as unknown as string);
    if (Number.isNaN(d.getTime())) return '';
    return d.toLocaleDateString('pl-PL', { weekday: 'long', day: 'numeric', month: 'long' });
  });

  protected onCancel(): void {
    if (this.isSubmitting()) return;
    this.closeRequested.emit();
  }

  protected onEmployeeChange(id: string): void {
    if (id === this.employeeId()) return;
    this.employeeId.set(id);
    this.serviceIds.set([]);
    this.customDurationMinutes.set(null);
    this.selectedSlot.set(null);
    this.originalEmployeeKept.set(false);
  }

  protected onDateChange(value: string): void {
    this.newDate.set(value ?? '');
    this.selectedSlot.set(null);
  }

  protected setOffScheduleMode(value: boolean): void {
    if (this.offScheduleMode() === value) return;
    this.offScheduleMode.set(value);
    // Godzina była ze slotu z grafiku — wyczyść przy zmianie trybu.
    this.selectedSlot.set(null);
    this.submitError.set(null);
  }

  protected onManualTimeChange(value: Date | null | undefined): void {
    if (!value) { this.selectedSlot.set(null); return; }
    const hh = value.getHours().toString().padStart(2, '0');
    const mm = value.getMinutes().toString().padStart(2, '0');
    this.selectedSlot.set(`${hh}:${mm}`);
  }

  protected selectSlot(slot: AppointmentSlotDto): void {
    if (!slot.slot) return;
    this.selectedSlot.set(slot.slot);
  }

  /** Slot-picker → wybór z listy (preferred lub other). */
  protected onSlotPicked(value: string): void {
    this.selectedSlot.set(value);
  }

  protected onSubmit(): void {
    const a = this.appointment();
    const emp = this.employeeId();
    const serviceIds = this.serviceIds();
    const slot = this.selectedSlot();
    const date = this.newDate();
    if (!a?.id || !emp || serviceIds.length === 0 || !slot || !date) return;
    // Backend `DateOnly` chce `yyyy-MM-dd`; generowany klient robi `JSON.stringify(body)`,
    // więc `Date.toISOString()` dawałby `2026-05-17T22:00:00.000Z` (z UTC-offsetem dnia!).
    // Wysyłamy gołego stringa, który JSON.stringify zostawia bez zmian — DateOnly model
    // binder ASP.NET Core akceptuje ten format wprost.
    const override = this.customDurationMinutes();
    const body = {
      employeeId: emp,
      serviceIds,
      date: date as unknown as Date,
      startTime: slot,
      // Poza grafikiem: backend pomija grafik/godziny pracy (blokuje tylko kolizja).
      ...(this.offScheduleMode() ? { ignoreSchedule: true } : {}),
      // Niestandardowy czas dołączamy tylko gdy zmieniony; null = backend zachowa bieżący override.
      ...(override !== null ? { customDurationMinutes: override } : {}),
    } as RescheduleAppointmentRequest;
    this.isSubmitting.set(true);
    this.submitError.set(null);
    this.appointmentsClient.rescheduleAppointment(a.id, body).subscribe({
      next: (id) => {
        this.isSubmitting.set(false);
        this.messages.add({
          severity: 'success',
          summary: 'Termin zmieniony',
          detail: 'Klient zostanie powiadomiony o nowym terminie.',
          life: 3000,
        });
        this.success.emit(id ?? a.id!);
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        const status = (err as { status?: number })?.status;
        if (status === 409) {
          this.submitError.set('Ten slot został już zajęty. Wybierz inny.');
        } else if (status === 400) {
          this.submitError.set('Wybrany termin nie spełnia ograniczeń (godziny pracy, wolne dni).');
        } else if (status === 403) {
          this.submitError.set('Nie masz uprawnień do zmiany tej wizyty.');
        } else {
          this.submitError.set('Nie udało się zmienić terminu. Spróbuj ponownie.');
        }
      },
    });
  }

  protected serviceLine(a: AppointmentPreviewDto): string {
    const name = a.serviceName?.trim();
    return name && name !== '' ? name : 'Wizyta';
  }

  protected shortTime(t: string | undefined): string {
    return (t ?? '').substring(0, 5);
  }

  private toIsoDate(value: string | Date | undefined): string | null {
    if (value == null) return null;
    if (typeof value === 'string') {
      const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
      if (m) return `${m[1]}-${m[2]}-${m[3]}`;
    }
    const d = typeof value === 'string' ? new Date(value) : value;
    if (Number.isNaN(d.getTime())) return null;
    return formatYyyyMmDd(d);
  }
}
