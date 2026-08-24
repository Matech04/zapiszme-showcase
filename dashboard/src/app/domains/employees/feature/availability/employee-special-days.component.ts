import { Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { SelectButtonModule } from 'primeng/selectbutton';
import { catchError, of } from 'rxjs';
import { AuthSessionService } from '@core/auth/auth-session.service';
import {
  API_BASE_URL,
  AppointmentPreviewDto,
  EmployeeScheduleDto,
  EmployeesClient,
  ScheduleOverrideDto,
  ShiftTemplateDto,
  ShiftTemplatesClient,
  SlotGenerationMode,
  TimeRangeDto,
} from '@core/api/api-client';
import { ScheduleOverridesApiService } from '@core/api/schedule-overrides-api.service';
import { fetchAppointmentsRange } from '@core/api/appointments-range';
import { HttpClient } from '@angular/common/http';
import { environment } from '@env/environment';
import { safeBackWith } from '@core/navigation/safe-back';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { statusVariantFromIdOrName } from '@core/theme/status-tokens';
import { FormFieldCalendarComponent } from '@shared/ui/forms/form-field-calendar.component';
import { TimeBlock } from '@domains/employees/feature/availability/weekly-schedule/ui/time-block/time-block';
import { SlotTimeRow } from '@domains/employees/feature/availability/weekly-schedule/ui/slot-time-row/slot-time-row';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Tooltip } from 'primeng/tooltip';

@Component({
  selector: 'app-employee-special-days',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    Button,
    FormsModule,
    ToggleSwitch,
    FormFieldCalendarComponent,
    TimeBlock,
    SlotTimeRow,
    SelectButtonModule,
    Tooltip,
  ],
  template: `
    <div [class]="embedded() ? '' : 'min-h-full bg-surface-50 dark:bg-surface-950'">
      <div [class]="embedded() ? '' : 'max-w-3xl mx-auto px-4 sm:px-6 py-8 sm:py-10'">
        @if (!embedded()) {
        <nav
          class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1"
        >
          <a [routerLink]="hubLink()" class="hover:text-primary transition-colors">{{ hubLabel() }}</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <a
            [routerLink]="availabilityHubLink()"
            class="hover:text-primary transition-colors"
            >Dostępność</a
          >
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <span class="text-surface-700 dark:text-surface-300">Godziny dnia</span>
        </nav>

        <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 mb-8">
          <div>
            <h1 class="text-2xl sm:text-3xl font-bold text-surface-900 tracking-tight mb-2">
              Ustaw godziny na ten dzień
            </h1>
            <p class="text-surface-600 dark:text-surface-400 text-sm sm:text-base max-w-xl leading-relaxed">
              Zmień godziny pracy w pojedynczym dniu (np. „w piątek tylko do 14") bez zmiany całego grafiku.
              Dla kilkudniowych nieobecności użyj <strong>Urlopy i chorobowe</strong>.
            </p>
          </div>

          <div class="flex flex-col sm:flex-row gap-2 sm:gap-3 shrink-0 w-full sm:w-auto">
            <p-button
              label="Wróć"
              severity="secondary"
              [outlined]="true"
              styleClass="w-full sm:w-auto"
              [disabled]="overridesData.isLoading()"
              (onClick)="goBack()"
            />
            <p-button
              label="Panel dostępności"
              icon="pi pi-th-large"
              severity="secondary"
              styleClass="w-full sm:w-auto"
              (onClick)="goToAvailabilityHub()"
            />
          </div>
        </div>

        <div
          class="rounded-2xl border border-surface-200/80 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-5 shadow-sm mb-6 sm:mb-8"
        >
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
        }

        <div
          [class]="embedded()
            ? 'rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-6 shadow-sm'
            : 'rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-6 shadow-sm mb-8'"
        >
          <h2 class="text-base font-bold text-surface-900 mb-4">
            {{ editingLabel() }}
          </h2>

          <div class="grid grid-cols-1 gap-6">
            <app-form-field-calendar
              id="special-day-date"
              data-tour="special-day-date"
              label="Data"
              testId="special-day-date"
              [(value)]="draftDate"
            />

            @if (currentDayLabel(); as label) {
              <div class="rounded-xl border border-sky-300/60 dark:border-sky-700/40 bg-sky-50/60 dark:bg-sky-950/20 px-4 py-3 flex items-start gap-2 text-sm text-sky-900 dark:text-sky-100">
                <i class="pi pi-info-circle text-xs mt-1 shrink-0"></i>
                <span>{{ label }}</span>
              </div>
            }

            @if (appointmentsCount() > 0) {
              <div class="rounded-xl border border-amber-300/70 dark:border-amber-700/50 bg-amber-50/70 dark:bg-amber-950/30 px-4 py-3 flex items-start gap-2 text-sm text-amber-900 dark:text-amber-100">
                <i class="pi pi-exclamation-triangle text-xs mt-1 shrink-0"></i>
                <span>
                  W tym dniu masz <strong>{{ appointmentsCount() }}</strong>
                  {{ appointmentsCount() === 1 ? 'zaplanowaną wizytę' : 'zaplanowanych wizyt' }}.
                  Zmiana godzin pracy może je wyrzucić poza grafik.
                </span>
              </div>
            }

            <div data-tour="special-day-working" class="flex flex-row items-center justify-between gap-4 rounded-xl border border-surface-200 dark:border-surface-200 px-4 py-3">
              <div>
                <p class="text-sm font-bold text-surface-900">Pracujesz w tym dniu</p>
                <p class="text-xs text-surface-500 dark:text-surface-400 mt-0.5">
                  Wyłącz, aby zablokować rezerwacje w tym dniu (dzień wolny).
                </p>
              </div>
              <p-toggleswitch
                [ngModel]="draftIsWorking()"
                (ngModelChange)="onWorkingToggle($event)"
                inputId="special-day-working"
                ariaLabel="Praca w wybranym dniu"
              />
            </div>

            @if (draftIsWorking()) {
              <div class="flex flex-col gap-2">
                <label class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 px-0.5">
                  Sposób ustalania godzin w tym dniu
                </label>
                <p-selectbutton
                  data-testid="special-day-mode"
                  [options]="modeOptions"
                  [ngModel]="overrideMode()"
                  (ngModelChange)="setOverrideMode($event)"
                  optionLabel="label"
                  optionValue="value"
                  [allowEmpty]="false"
                  ariaLabel="Sposób ustalania godzin w tym dniu"
                />
                <p class="text-xs text-surface-500 dark:text-surface-400 px-0.5">
                  @if (isFixedMode()) {
                    Ustalone godziny — klient wybiera tylko z godzin, które sam(a) wpisujesz na ten dzień (np. 9:00, 12:00, 15:00).
                  } @else {
                    Elastyczne godziny — klient wybiera dowolną wolną godzinę w Twoich blokach pracy; sloty tworzą się automatycznie.
                  }
                </p>
              </div>
            }

            @if (draftIsWorking()) {
              <div>
                <!-- Szablony — dostępne od razu jako chipy (zamiast ukrytego menu) -->
                @if (templatesData.isLoading()) {
                  <div class="flex flex-wrap gap-2 mb-3" aria-hidden="true">
                    <span class="h-8 w-24 rounded-full bg-surface-100 dark:bg-surface-100 animate-pulse"></span>
                    <span class="h-8 w-20 rounded-full bg-surface-100 dark:bg-surface-100 animate-pulse"></span>
                  </div>
                } @else if (templateChips().length) {
                  <div class="mb-3">
                    <p class="text-[11px] font-semibold uppercase tracking-wider text-surface-400 dark:text-surface-500 mb-1.5 flex items-center gap-1">
                      <i class="pi pi-bolt text-[10px]" aria-hidden="true"></i> Szablony
                    </p>
                    <div class="flex flex-wrap gap-2" data-testid="special-day-template-chips">
                      @for (tpl of templateChips(); track tpl.id) {
                        <button
                          type="button"
                          (click)="applyTemplate(tpl)"
                          [attr.aria-pressed]="appliedTemplateId() === tpl.id"
                          [pTooltip]="templateSummary(tpl)"
                          tooltipPosition="top"
                          [showDelay]="300"
                          class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full border text-sm font-semibold transition-colors hover:border-primary"
                          [class.border-primary]="appliedTemplateId() === tpl.id"
                          [class.bg-primary/10]="appliedTemplateId() === tpl.id"
                          [class.text-primary]="appliedTemplateId() === tpl.id"
                          [class.border-surface-300]="appliedTemplateId() !== tpl.id"
                          [class.dark:border-surface-600]="appliedTemplateId() !== tpl.id"
                          [class.text-surface-700]="appliedTemplateId() !== tpl.id"
                          [class.dark:text-surface-300]="appliedTemplateId() !== tpl.id"
                        >
                          <i class="pi pi-clock text-xs" aria-hidden="true"></i>
                          {{ tpl.name || 'Szablon' }}
                        </button>
                      }
                    </div>
                  </div>
                } @else {
                  <div class="mb-3 flex flex-wrap items-center gap-2">
                    <span class="text-xs text-surface-400 dark:text-surface-500">
                      {{ isFixedMode() ? 'Brak szablonów stałogodzinnych.' : 'Brak szablonów przedziałowych.' }}
                    </span>
                    <a
                      [routerLink]="['/admin/resources/shift-templates/new']"
                      class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full border border-dashed border-primary/40 text-xs font-semibold text-primary hover:bg-primary/5 transition-colors"
                    >
                      <i class="pi pi-plus text-[10px]" aria-hidden="true"></i> Utwórz szablon
                    </a>
                  </div>
                }

                <h3 class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 mb-2">
                  {{ isFixedMode() ? 'Godziny startu' : 'Bloki pracy' }}
                </h3>

                @if (isFixedMode()) {
                  <div class="flex flex-col gap-2.5">
                    @for (t of draftFixedTimes(); track $index) {
                      <app-slot-time-row
                        [time]="t"
                        (changedTime)="updateFixedTime($index, $event)"
                        (deleteTime)="removeFixedTime($index)"
                      />
                    } @empty {
                      <p class="text-sm text-surface-500 dark:text-surface-400 text-center py-2">
                        Dodaj co najmniej jedną godzinę startu.
                      </p>
                    }
                  </div>

                  <button
                    type="button"
                    data-testid="special-day-add-fixed-time"
                    class="mt-3 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-primary/35 dark:border-primary/40 bg-transparent py-3 px-3 text-sm font-semibold text-primary hover:bg-primary/5 transition-colors"
                    (click)="addFixedTime()"
                  >
                    <i class="pi pi-plus text-xs" aria-hidden="true"></i>
                    Dodaj godzinę
                  </button>
                } @else {
                  <div class="flex flex-col gap-2.5">
                    @for (block of draftWorkRanges(); track $index) {
                      <app-time-block
                        [block]="block"
                        (changedBlock)="updateWorkRange($index, $event)"
                        (deleteBlock)="removeWorkRange($index)"
                      />
                    } @empty {
                      <p class="text-sm text-surface-500 dark:text-surface-400 text-center py-2">
                        Dodaj co najmniej jeden blok pracy.
                      </p>
                    }
                  </div>

                  <button
                    type="button"
                    class="mt-3 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-primary/35 dark:border-primary/40 bg-transparent py-3 px-3 text-sm font-semibold text-primary hover:bg-primary/5 transition-colors"
                    (click)="addWorkRange()"
                  >
                    <i class="pi pi-plus text-xs" aria-hidden="true"></i>
                    Dodaj blok pracy
                  </button>
                }
              </div>

              @if (!isFixedMode()) {
              <div class="pt-2 border-t border-surface-200/70 dark:border-surface-100">
                <h3 class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 mb-2">
                  Przerwy
                </h3>

                <div class="flex flex-col gap-2.5">
                  @for (br of draftBreaks(); track $index) {
                    <app-time-block
                      [block]="br"
                      (changedBlock)="updateBreak($index, $event)"
                      (deleteBlock)="removeBreak($index)"
                    />
                  } @empty {
                    <p class="text-sm text-surface-500 dark:text-surface-400 italic py-1">
                      Brak zdefiniowanych przerw.
                    </p>
                  }
                </div>

                <button
                  type="button"
                  class="mt-3 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-surface-300/70 dark:border-surface-200 bg-transparent py-3 px-3 text-sm font-semibold text-surface-700 hover:bg-surface-100 dark:hover:bg-surface-100/70 transition-colors"
                  (click)="addBreak()"
                >
                  <i class="pi pi-plus text-xs" aria-hidden="true"></i>
                  Dodaj przerwę
                </button>
              </div>
              }
            }

            @if (formError()) {
              <p class="text-sm text-red-600 dark:text-red-400">{{ formError() }}</p>
            }

            @if (!embedded()) {
            <div class="flex flex-col sm:flex-row gap-2 sm:justify-end pt-2">
              <p-button
                label="Anuluj edycję"
                severity="secondary"
                [outlined]="true"
                styleClass="w-full sm:w-auto"
                [disabled]="saving()"
                (onClick)="resetDraft()"
              />
              <p-button
                label="Zapisz godziny dnia"
                data-testid="special-day-save"
                data-tour="special-day-save"
                icon="pi pi-check"
                styleClass="w-full sm:w-auto font-semibold"
                [loading]="saving()"
                [disabled]="overridesData.isLoading()"
                (onClick)="saveDraft()"
              />
            </div>
            }
          </div>
        </div>

        @if (!embedded()) {
        @if (overridesData.isLoading()) {
          <div class="rounded-2xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-12 text-center text-surface-500">
            Wczytywanie…
          </div>
        } @else if (sortedOverrides().length === 0) {
          <div
            class="rounded-2xl border-2 border-dashed border-surface-200 dark:border-surface-100 p-12 text-center bg-surface-0 dark:bg-surface-50/60"
          >
            <div class="w-16 h-16 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mx-auto mb-4">
              <i class="pi pi-star text-2xl text-surface-400"></i>
            </div>
            <h3 class="text-lg font-bold text-surface-900 mb-2">Brak ustawionych dni</h3>
            <p class="text-surface-600 dark:text-surface-400 text-sm max-w-sm mx-auto">
              Ustaw godziny pierwszego dnia powyżej — pojawi się na liście poniżej.
            </p>
          </div>
        } @else {
          <div class="flex flex-col gap-3 sm:gap-4">
            <h3 class="text-sm font-bold uppercase tracking-wider text-surface-500 px-0.5">Ustawione dni</h3>
            @for (row of sortedOverrides(); track overrideTrack(row)) {
              <div
                class="group rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm transition-[box-shadow,border-color] hover:shadow-md dark:hover:border-surface-700"
              >
                <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
                  <div class="min-w-0 space-y-1">
                    <p class="text-base sm:text-lg font-semibold text-surface-900">
                      {{ formatOverrideHeading(row) }}
                    </p>
                    <p class="text-sm text-surface-600 dark:text-surface-400">
                      {{ formatOverrideDetail(row) }}
                    </p>
                  </div>
                  <div class="flex items-center gap-2 shrink-0">
                    <p-button
                      label="Edytuj"
                      icon="pi pi-pencil"
                      severity="secondary"
                      [outlined]="true"
                      styleClass="!text-sm"
                      (onClick)="loadIntoDraft(row)"
                    />
                    <p-button
                      icon="pi pi-trash"
                      [rounded]="true"
                      [text]="true"
                      severity="danger"
                      [disabled]="removingKey() !== null"
                      [loading]="removingKey() === overrideDateKey(row)"
                      styleClass="!w-9 !h-9 !p-0"
                      (onClick)="confirmRemove(row)"
                      [attr.aria-label]="'Usuń godziny dnia: ' + formatOverrideHeading(row)"
                    />
                  </div>
                </div>
              </div>
            }
          </div>
        }
        }
      </div>
    </div>
  `
})
export class EmployeeSpecialDaysComponent {
  private employeesService = inject(EmployeesClient);
  private templatesClient = inject(ShiftTemplatesClient);
  private auth = inject(AuthSessionService);
  private scheduleOverridesApi = inject(ScheduleOverridesApiService);
  private http = inject(HttpClient);
  private apiBaseUrl = inject(API_BASE_URL, { optional: true }) ?? environment.apiBaseUrl;
  private location = inject(Location);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private confirmationService = inject(ConfirmationService);
  private messageService = inject(MessageService);

  id = input.required<string>();

  /** Tryb osadzony (drawer) — tylko edytor, bez nagłówka/listy/przycisków; emituje `saved`/`cancelled`. */
  embedded = input<boolean>(false);
  /** Data startowa (YYYY-MM-DD) podawana w trybie osadzonym zamiast query param `?date`. */
  initialDate = input<string | null>(null);

  /** Emitowane po udanym zapisie (tryb osadzony — rodzic zamyka drawer + odświeża kalendarz). */
  saved = output<void>();
  /** Emitowane przy anulowaniu (tryb osadzony). */
  cancelled = output<void>();

  private isSelfMode = computed(() => this.router.url.startsWith('/admin/my-availability/'));
  protected hubLabel = computed(() => this.isSelfMode() ? 'Moja dostępność' : 'Zarządzanie');
  protected hubLink = computed(() =>
    this.isSelfMode() ? ['/admin/my-availability', this.id()] : '/admin/resources');
  protected availabilityHubLink = computed(() =>
    this.isSelfMode()
      ? ['/admin/my-availability', this.id()]
      : ['/admin/resources/employees', this.id(), 'availability']);

  /**
   * Initial value czytana z query param `?date=YYYY-MM-DD` (np. z modalu kalendarza miesiąca).
   * Inicjowane w field-initializerze — odwołanie do `this.route` jest bezpieczne, bo `route` jest
   * deklarowane wyżej w klasie (kolejność deklaracji = kolejność inicjalizacji native class fields).
   * Plus signal `queryDate` poniżej obsługuje re-navigation gdy Angular re-używa komponent.
   */
  draftDate = model<string | null>(this.readInitialDateFromQuery());

  /** Reaktywne źródło `?date=...` — działa też przy zmianie query bez re-tworzenia komponentu. */
  private readonly queryDate = toSignal(
    this.route.queryParamMap.pipe(map((m) => m.get('date'))),
    { initialValue: this.route.snapshot.queryParamMap.get('date') },
  );

  /** Ostatnio przetworzona wartość `?date` — pozwala odróżnić REALNĄ nawigację (klik dnia w kalendarzu)
   *  od edycji w formularzu (gdzie query się nie zmienia). */
  private lastProcessedQueryDate: string | null | undefined = undefined;

  /**
   * Sync `draftDate` z query param. Reaguje TYLKO na zmianę `?date` (nawigacja z kalendarza miesiąca) —
   * nowy dzień wychodzi z trybu edycji i pozwala efektowi prefill rozstrzygnąć: istnieje wyjątek → edytuj,
   * inaczej → podpowiedz z grafiku. Edycja z listy (bez zmiany query) nie jest tu ruszana.
   */
  private readonly _syncDateFromQuery = effect(() => {
    const qd = this.queryDate();
    if (qd === this.lastProcessedQueryDate) return; // brak realnej zmiany nawigacji
    this.lastProcessedQueryDate = qd;
    if (!qd || !/^\d{4}-\d{2}-\d{2}$/.test(qd)) return;
    this.editingKey.set(null); // nowy dzień z kalendarza → re-ewaluacja (edytuj istniejący / podpowiedz)
    this.appliedTemplateId.set(null);
    this.draftDate.set(qd);
  });

  /** Ostatnio przetworzona wartość `initialDate` — analogicznie do query, ale dla trybu osadzonego (drawer). */
  private lastProcessedInitialDate: string | null | undefined = undefined;

  /** Sync `draftDate` z `initialDate` (tryb osadzony/drawer) — reaguje tylko na zmianę inputu. */
  private readonly _syncInitialDate = effect(() => {
    const id = this.initialDate();
    if (id === this.lastProcessedInitialDate) return;
    this.lastProcessedInitialDate = id;
    if (!id || !/^\d{4}-\d{2}-\d{2}$/.test(id)) return;
    this.editingKey.set(null);
    this.appliedTemplateId.set(null);
    this.draftDate.set(id);
  });

  draftIsWorking = signal(true);
  draftWorkRanges = signal<{ startTime: string; endTime: string }[]>([
    { startTime: '09:00', endTime: '17:00' }
  ]);
  draftBreaks = signal<{ startTime: string; endTime: string }[]>([]);
  /** Stałe godziny startu wyjątku (tryb FixedStartTimes). HH:mm. */
  draftFixedTimes = signal<string[]>([]);
  saving = signal(false);
  removingKey = signal<string | null>(null);

  /** Gdy użytkownik edytuje istniejący wpis — ten sam klucz co w `overrideDateKey`. */
  editingKey = signal<string | null>(null);

  /** Id ostatnio zastosowanego szablonu — do podświetlenia chipa (feedback). Czyszczone przy zmianie kontekstu. */
  appliedTemplateId = signal<string | null>(null);

  employeeData = rxResource({
    stream: () => this.employeesService.getEmployee(this.id())
  });

  /** Globalny (domyślny) tryb pracownika — podpowiedź startowa dla nowego wyjątku. */
  employeeMode = computed<SlotGenerationMode>(
    () => this.employeeData.value()?.slotGenerationMode ?? SlotGenerationMode.Grid,
  );

  /**
   * Tryb edytowanego wyjątku — wybierany PER DZIEŃ (kosmetyczka może budować grafik samymi
   * wyjątkami, bez grafiku tygodniowego, i mieszać tryby per dzień). Ustawiany przez efekt
   * prefilla / wczytanie istniejącego wyjątku, edytowalny przełącznikiem.
   */
  overrideMode = signal<SlotGenerationMode>(SlotGenerationMode.Grid);

  isFixedMode = computed(() => this.overrideMode() === SlotGenerationMode.FixedStartTimes);

  setOverrideMode(mode: SlotGenerationMode) {
    this.overrideMode.set(mode);
    this.appliedTemplateId.set(null); // inny tryb = inny zestaw chipów
    this.formError.set(null);
  }

  protected readonly modeOptions = [
    { label: 'Elastyczne godziny', value: SlotGenerationMode.Grid },
    { label: 'Ustalone godziny', value: SlotGenerationMode.FixedStartTimes },
  ];
  protected readonly SlotGenerationMode = SlotGenerationMode;

  overridesData = rxResource({
    stream: () => this.employeesService.getScheduleOverrides(this.id()),
    defaultValue: [] as ScheduleOverrideDto[]
  });

  /**
   * Szablony zmian to zasób salonu — API zwraca je tylko dla `StaffManagement`. Pracownik ich nie
   * pobiera (dostałby 403); pusta lista chowa sekcję „Szablony" nad edytorem dnia.
   */
  private canUseTemplates = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager';
  });

  templatesData = rxResource({
    params: () => this.canUseTemplates(),
    stream: ({ params: allowed }) =>
      allowed ? this.templatesClient.getShiftTemplates() : of([] as ShiftTemplateDto[]),
    defaultValue: [] as ShiftTemplateDto[],
  });

  /** Wszystkie grafiki pracownika — używane do wyliczenia, jaki grafik aktualnie obowiązuje dla wybranej daty. */
  schedulesData = rxResource({
    stream: () => this.employeesService.getEmployeeSchedules(this.id()),
    defaultValue: [] as EmployeeScheduleDto[],
  });

  /**
   * Wizyty na wybrany dzień (dla pracownika). Używane jako ostrzeżenie inline + confirmation pre-save.
   * `silent: true` — to background check, brak wizyt nie jest błędem, błąd HTTP nie pokazuje toasta.
   */
  appointmentsOnDate = rxResource({
    // Klucz stringowy zamiast literału obiektowego — inaczej każda emisja zależności (nawet
    // do tej samej wartości) miała nową tożsamość i wymuszała ponowny fetch wizyt dnia.
    params: () => `${this.draftDate() ?? ''}\u001e${this.id() ?? ''}`,
    stream: ({ params }) => {
      const [rawDate, employeeId] = params.split('\u001e');
      const d = rawDate ? this.coerceDate(rawDate) : null;
      if (!d) {
        return of([] as AppointmentPreviewDto[]);
      }
      return fetchAppointmentsRange(this.http, this.apiBaseUrl, d, d, employeeId, { silent: true })
        .pipe(catchError(() => of([] as AppointmentPreviewDto[])));
    },
    defaultValue: [] as AppointmentPreviewDto[],
  });

  /**
   * Realne wizyty w danym dniu — z pominięciem statusów, które nie blokują grafiku:
   *   - `canceled` (anulowane to nie „zaplanowana wizyta"),
   *   - `awaitingOtp` (anonimowy hold publicznej rezerwacji przed weryfikacją OTP; wygasa sam).
   * Bez tego ostrzeżenie „masz X zaplanowanych wizyt" liczyło też anulowane i porzucone holdy
   * (`GET /api/Appointments` zwraca wszystkie statusy), zawyżając licznik (np. 2 realne → „5").
   */
  private readonly realAppointmentsOnDate = computed(() =>
    (this.appointmentsOnDate.value() ?? []).filter((a) => {
      const st = a.status as { id?: number; name?: string } | undefined;
      const variant = statusVariantFromIdOrName(st?.id ?? null, st?.name ?? null);
      return variant !== 'canceled' && variant !== 'awaitingOtp';
    }),
  );

  appointmentsCount = computed(() => this.realAppointmentsOnDate().length);

  /**
   * Co aktualnie mówi grafik tygodniowy dla wybranej `draftDate`:
   *   - `null` gdy data niewybrana lub brak pasującego grafiku
   *   - `{ workRanges: [], breaks: [], isOffDay: true }` gdy grafik mówi „dzień wolny"
   *   - `{ workRanges, breaks, isOffDay: false }` gdy są godziny pracy
   */
  currentDaySchedule = computed<{ workRanges: { startTime: string; endTime: string }[]; breaks: { startTime: string; endTime: string }[]; fixedStartTimes: string[]; isOffDay: boolean } | null>(() => {
    const dateStr = this.draftDate();
    if (!dateStr) return null;
    const targetDate = this.coerceDate(dateStr);
    if (!targetDate) return null;

    const schedules = this.schedulesData.value() ?? [];
    const activeSchedule = schedules.find((s) => {
      const from = this.coerceDate(s.activeFrom);
      const to = this.coerceDate(s.activeTo);
      if (!from || !to) return false;
      return from.getTime() <= targetDate.getTime() && targetDate.getTime() <= to.getTime();
    });
    if (!activeSchedule) return null;

    const cycles = Math.max(1, activeSchedule.numberOfCycles ?? 1);
    const from = this.coerceDate(activeSchedule.activeFrom)!;
    const fromMidnight = new Date(from.getFullYear(), from.getMonth(), from.getDate());
    const targetMidnight = new Date(targetDate.getFullYear(), targetDate.getMonth(), targetDate.getDate());
    const daysDiff = Math.round((targetMidnight.getTime() - fromMidnight.getTime()) / 86400000);
    const weekInCycle = ((Math.floor(daysDiff / 7) % cycles) + cycles) % cycles;
    const cycleIndex = weekInCycle * 7 + targetDate.getDay();

    const dayEntry = (activeSchedule.days ?? []).find((d) => d.cycleIndex === cycleIndex);

    const workRanges = (dayEntry?.workRanges ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => ({ startTime: this.trimTime(r.startTime), endTime: this.trimTime(r.endTime) }));
    const breaks = (dayEntry?.breaks ?? [])
      .filter((b) => !!b?.startTime && !!b?.endTime)
      .map((b) => ({ startTime: this.trimTime(b.startTime), endTime: this.trimTime(b.endTime) }));
    const fixedStartTimes = (dayEntry?.fixedStartTimes ?? [])
      .filter((t) => !!t)
      .map((t) => this.trimTime(t));

    // Bazowy grafik jest w GLOBALNYM trybie pracownika (override per-dzień to osobna decyzja użytkownika).
    const baseFixed = this.employeeMode() === SlotGenerationMode.FixedStartTimes;
    const isOffDay = baseFixed ? fixedStartTimes.length === 0 : workRanges.length === 0;
    return { workRanges, breaks, fixedStartTimes, isOffDay };
  });

  /** Sformatowany opis aktualnego grafiku dla wybranej daty (do bannera w UI). */
  currentDayLabel = computed<string | null>(() => {
    const cs = this.currentDaySchedule();
    if (!cs) return null;
    if (cs.isOffDay) return 'W tym dniu domyślnie nie pracujesz (dzień wolny z grafiku).';
    if (this.employeeMode() === SlotGenerationMode.FixedStartTimes) {
      const times = cs.fixedStartTimes.slice().sort((a, b) => a.localeCompare(b)).join(', ');
      return `Aktualnie z grafiku (stałe godziny): ${times}`;
    }
    const work = cs.workRanges
      .slice()
      .sort((a, b) => a.startTime.localeCompare(b.startTime))
      .map((r) => `${r.startTime}–${r.endTime}`)
      .join(', ');
    const breaksLabel = cs.breaks.length
      ? ` · przerwy: ${cs.breaks.map((b) => `${b.startTime}–${b.endTime}`).join(', ')}`
      : '';
    return `Aktualnie z grafiku: ${work}${breaksLabel}`;
  });

  /**
   * Pre-fill draft z aktualnego grafiku, gdy:
   *   - user wybierze nową datę (draftDate set)
   *   - NIE edytujemy już zapisanego wyjątku (editingKey === null)
   *   - aktualny grafik jest znany
   * To dramatycznie zmniejsza chaos: zamiast statycznego 09:00-17:00, user widzi swoje rzeczywiste godziny tego dnia.
   */
  private readonly _autoPrefillFromSchedule = effect(() => {
    const dateStr = this.draftDate();
    if (!dateStr) return;
    if (this.editingKey() !== null) return;
    if (this.overridesData.isLoading()) return; // poczekaj aż znamy istniejące wyjątki

    // Jeśli na ten dzień JUŻ istnieje wyjątek → wejdź od razu w jego edycję (zamiast pustego formularza).
    const existing = (this.overridesData.value() ?? []).find(
      (o) => this.overrideDateKeyFromDate(o.date) === dateStr,
    );
    if (existing) {
      this.loadIntoDraft(existing);
      return;
    }

    // Nowy wyjątek dla wybranej daty → tryb startowo = globalny tryb pracownika (użytkownik może przełączyć).
    const baseMode = this.employeeMode();
    this.overrideMode.set(baseMode);

    const cs = this.currentDaySchedule();
    if (!cs) return; // brak grafiku bazowego (np. „papierowy kalendarz") → zostają domyślne wartości draftu

    if (cs.isOffDay) {
      this.draftIsWorking.set(false);
      this.draftWorkRanges.set([]);
      this.draftBreaks.set([]);
      this.draftFixedTimes.set([]);
    } else if (baseMode === SlotGenerationMode.FixedStartTimes) {
      this.draftIsWorking.set(true);
      this.draftFixedTimes.set([...cs.fixedStartTimes]);
      this.draftWorkRanges.set([]);
      this.draftBreaks.set([]);
    } else {
      this.draftIsWorking.set(true);
      this.draftWorkRanges.set(cs.workRanges.map((r) => ({ ...r })));
      this.draftBreaks.set(cs.breaks.map((b) => ({ ...b })));
      this.draftFixedTimes.set([]);
    }
  });

  /**
   * Szablony pasujące do trybu wyjątku (stałe godziny vs przedziały) — pokazywane od razu jako chipy.
   * Szablon w drugim trybie nic by tu nie zrobił, więc go pomijamy.
   */
  templateChips = computed<ShiftTemplateDto[]>(() => {
    const mode = this.isFixedMode() ? SlotGenerationMode.FixedStartTimes : SlotGenerationMode.Grid;
    return (this.templatesData.value() ?? [])
      .filter((t) => (t.slotGenerationMode ?? SlotGenerationMode.Grid) === mode)
      .slice()
      .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? '', 'pl'));
  });

  /** Krótkie podsumowanie szablonu (tooltip chipa) — np. „9:00–17:00 · 1 przerwa" lub „9:00, 12:00". */
  templateSummary(tpl: ShiftTemplateDto): string {
    if ((tpl.slotGenerationMode ?? SlotGenerationMode.Grid) === SlotGenerationMode.FixedStartTimes) {
      const times = (tpl.fixedStartTimes ?? [])
        .filter((t) => !!t)
        .map((t) => this.trimTime(t))
        .sort((a, b) => a.localeCompare(b));
      return times.length ? `Godziny: ${times.join(', ')}` : 'Pusty szablon';
    }
    const work = (tpl.workRanges ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => `${this.trimTime(r.startTime)}–${this.trimTime(r.endTime)}`);
    const breakCount = (tpl.breaks ?? []).filter((b) => !!b?.startTime && !!b?.endTime).length;
    const breaksLabel = breakCount
      ? ` · ${breakCount} ${breakCount === 1 ? 'przerwa' : breakCount <= 4 ? 'przerwy' : 'przerw'}`
      : '';
    return work.length ? `${work.join(', ')}${breaksLabel}` : 'Pusty szablon';
  }

  applyTemplate(tpl: ShiftTemplateDto) {
    if (this.isFixedMode()) {
      const times = (tpl.fixedStartTimes ?? []).filter((t) => !!t).map((t) => this.trimTime(t));
      if (!times.length) return;
      this.draftIsWorking.set(true);
      this.draftFixedTimes.set(times);
      this.appliedTemplateId.set(tpl.id ?? null);
      this.formError.set(null);
      return;
    }

    const workRanges = (tpl.workRanges ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => ({ startTime: this.trimTime(r.startTime), endTime: this.trimTime(r.endTime) }));
    const breaks = (tpl.breaks ?? [])
      .filter((b) => !!b?.startTime && !!b?.endTime)
      .map((b) => ({ startTime: this.trimTime(b.startTime), endTime: this.trimTime(b.endTime) }));

    if (!workRanges.length) return;

    this.draftIsWorking.set(true);
    this.draftWorkRanges.set(workRanges);
    this.draftBreaks.set(breaks);
    this.appliedTemplateId.set(tpl.id ?? null);
    this.formError.set(null);
  }

  employeeDisplayName = computed(() => {
    const e = this.employeeData.value();
    if (!e) return 'Pracownik';
    const parts = [e.firstName, e.lastName].filter(Boolean);
    return parts.length ? parts.join(' ') : 'Pracownik';
  });

  employeeSubtitle = computed(() => {
    const e = this.employeeData.value();
    if (!e?.email) return 'Godziny pojedynczych dni';
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

  sortedOverrides = computed(() => {
    const list = this.overridesData.value() ?? [];
    return [...list].sort((a, b) => {
      const da = this.coerceDate(a.date)?.getTime() ?? 0;
      const db = this.coerceDate(b.date)?.getTime() ?? 0;
      return da - db;
    });
  });

  editingLabel = computed(() => {
    const k = this.editingKey();
    return k ? 'Edytuj godziny dnia' : 'Ustaw godziny na ten dzień';
  });

  formError = signal<string | null>(null);

  onWorkingToggle(enabled: boolean) {
    this.draftIsWorking.set(enabled);
    if (this.isFixedMode()) {
      if (enabled && this.draftFixedTimes().length === 0) {
        this.draftFixedTimes.set(['09:00']);
      }
      if (!enabled) {
        this.draftFixedTimes.set([]);
      }
      return;
    }
    if (enabled && this.draftWorkRanges().length === 0) {
      this.draftWorkRanges.set([{ startTime: '09:00', endTime: '17:00' }]);
    }
    if (!enabled) {
      this.draftWorkRanges.set([]);
      this.draftBreaks.set([]);
    }
  }

  addFixedTime() {
    this.draftFixedTimes.update((list) => [...list, '12:00']);
  }

  updateFixedTime(index: number, time: string) {
    this.draftFixedTimes.update((list) => list.map((t, i) => (i === index ? time : t)));
  }

  removeFixedTime(index: number) {
    this.draftFixedTimes.update((list) => list.filter((_, i) => i !== index));
  }

  updateWorkRange(index: number, updated: TimeRangeDto) {
    this.draftWorkRanges.update((blocks) =>
      blocks.map((b, i) =>
        i === index
          ? {
              startTime: (updated.startTime ?? '').substring(0, 5),
              endTime: (updated.endTime ?? '').substring(0, 5),
            }
          : b,
      ),
    );
  }

  removeWorkRange(index: number) {
    this.draftWorkRanges.update((blocks) => blocks.filter((_, i) => i !== index));
  }

  addWorkRange() {
    this.draftWorkRanges.update((blocks) => [...blocks, { startTime: '09:00', endTime: '17:00' }]);
  }

  updateBreak(index: number, updated: TimeRangeDto) {
    this.draftBreaks.update((blocks) =>
      blocks.map((b, i) =>
        i === index
          ? {
              startTime: (updated.startTime ?? '').substring(0, 5),
              endTime: (updated.endTime ?? '').substring(0, 5),
            }
          : b,
      ),
    );
  }

  removeBreak(index: number) {
    this.draftBreaks.update((blocks) => blocks.filter((_, i) => i !== index));
  }

  addBreak() {
    this.draftBreaks.update((blocks) => [...blocks, { startTime: '12:00', endTime: '12:30' }]);
  }

  resetDraft() {
    this.draftDate.set(null);
    this.draftIsWorking.set(true);
    this.draftWorkRanges.set([{ startTime: '09:00', endTime: '17:00' }]);
    this.draftBreaks.set([]);
    this.draftFixedTimes.set([]);
    this.editingKey.set(null);
    this.appliedTemplateId.set(null);
    this.formError.set(null);
  }

  loadIntoDraft(row: ScheduleOverrideDto) {
    const key = this.overrideDateKey(row);
    this.editingKey.set(key);
    this.appliedTemplateId.set(null);
    this.formError.set(null);
    this.draftDate.set(key);

    // Tryb edytowanego wyjątku bierzemy z samego wyjątku (może różnić się od trybu globalnego pracownika).
    const rowMode = row.slotGenerationMode ?? SlotGenerationMode.Grid;
    this.overrideMode.set(rowMode);

    if (rowMode === SlotGenerationMode.FixedStartTimes) {
      const times = (row.fixedStartTimes ?? []).filter((t) => !!t).map((t) => this.trimTime(t));
      this.draftIsWorking.set(times.length > 0);
      this.draftFixedTimes.set(times);
      this.draftWorkRanges.set([]);
      this.draftBreaks.set([]);
      return;
    }

    const workRanges = (row.workRanges ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => ({ startTime: this.trimTime(r.startTime), endTime: this.trimTime(r.endTime) }));
    const breaks = (row.breaks ?? [])
      .filter((b) => !!b?.startTime && !!b?.endTime)
      .map((b) => ({ startTime: this.trimTime(b.startTime), endTime: this.trimTime(b.endTime) }));

    if (!workRanges.length) {
      this.draftIsWorking.set(false);
      this.draftWorkRanges.set([]);
      this.draftBreaks.set([]);
      return;
    }

    this.draftIsWorking.set(true);
    this.draftWorkRanges.set(workRanges);
    this.draftBreaks.set(breaks);
  }

  saveDraft() {
    this.formError.set(null);
    const dateStr = this.draftDate()?.trim() ?? '';
    if (!dateStr) {
      this.formError.set('Wybierz datę.');
      return;
    }

    const working = this.draftIsWorking();
    const isFixed = this.isFixedMode();
    const workRanges = this.draftWorkRanges();
    const breaks = this.draftBreaks();
    const fixedTimes = this.draftFixedTimes().map((t) => t?.trim()).filter((t): t is string => !!t);

    if (working) {
      if (isFixed) {
        if (fixedTimes.length === 0) {
          this.formError.set('Dodaj co najmniej jedną godzinę startu lub oznacz dzień jako wolny.');
          return;
        }
        if (fixedTimes.some((t) => !/^([01]\d|2[0-3]):([0-5]\d)$/.test(t))) {
          this.formError.set('Nieprawidłowy format godziny.');
          return;
        }
        if (new Set(fixedTimes).size !== fixedTimes.length) {
          this.formError.set('Godziny startu się powtarzają.');
          return;
        }
      } else {
        if (workRanges.length === 0) {
          this.formError.set('Dodaj co najmniej jeden blok pracy lub oznacz dzień jako wolny.');
          return;
        }
        const rangeErr = this.validateWorkingRanges(workRanges);
        if (rangeErr) {
          this.formError.set(rangeErr);
          return;
        }
        const breakErr = this.validateBreaks(workRanges, breaks);
        if (breakErr) {
          this.formError.set(breakErr);
          return;
        }
      }
    }

    const apptCount = this.appointmentsCount();
    if (apptCount > 0) {
      this.confirmationService.confirm({
        message: `W tym dniu masz ${apptCount} ${apptCount === 1 ? 'zaplanowaną wizytę' : 'zaplanowanych wizyt'}. Zmiana godzin może je wyrzucić poza grafik — czy na pewno chcesz zapisać godziny tego dnia?`,
        header: 'Potwierdzenie zmiany',
        icon: 'pi pi-exclamation-triangle',
        acceptLabel: 'Tak, zapisz',
        rejectLabel: 'Anuluj',
        accept: () => this.persistOverride(dateStr, working, workRanges, breaks, fixedTimes),
      });
      return;
    }

    this.persistOverride(dateStr, working, workRanges, breaks, fixedTimes);
  }

  private persistOverride(
    dateStr: string,
    working: boolean,
    workRanges: { startTime: string; endTime: string }[],
    breaks: { startTime: string; endTime: string }[],
    fixedTimes: string[],
  ) {
    const isFixed = this.isFixedMode();
    const body: ScheduleOverrideDto = {
      date: dateStr as unknown as Date,
      slotGenerationMode: isFixed ? SlotGenerationMode.FixedStartTimes : SlotGenerationMode.Grid,
      workRanges: !isFixed && working
        ? workRanges.map((r) => ({
            startTime: this.toIsoTimeOnly(r.startTime),
            endTime: this.toIsoTimeOnly(r.endTime),
          }))
        : [],
      breaks: !isFixed && working
        ? breaks.map((b) => ({
            startTime: this.toIsoTimeOnly(b.startTime),
            endTime: this.toIsoTimeOnly(b.endTime),
          }))
        : [],
      fixedStartTimes: isFixed && working
        ? Array.from(new Set(fixedTimes)).sort((a, b) => a.localeCompare(b)).map((t) => this.toIsoTimeOnly(t))
        : [],
    };

    this.saving.set(true);
    this.employeesService.setScheduleOverride(this.id(), body).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.messageService.add({
          severity: 'success',
          summary: 'Zapisano',
          detail: 'Godziny dnia zostały zapisane.',
          life: 4000
        });
        const outsideCount = result?.appointmentsOutsideSchedule?.length ?? 0;
        if (outsideCount > 0) {
          this.messageService.add({
            severity: 'warn',
            summary: 'Wizyty poza nowym grafikiem',
            detail: `${outsideCount} ${plWizytOutside(outsideCount)} ${plJest(outsideCount)} teraz poza nowymi godzinami — przejrzyj je w kalendarzu.`,
            life: 10000,
          });
        }
        this.editingKey.set(this.overrideDateKeyFromYyyyMmDd(dateStr));
        this.overridesData.reload();
        // Tryb osadzony (drawer): rodzic zamyka panel i odświeża kalendarz.
        if (this.embedded()) {
          this.saved.emit();
        }
      },
      error: () => {
        this.saving.set(false);
      }
    });
  }

  overrideDateKey(row: ScheduleOverrideDto): string {
    return this.overrideDateKeyFromDate(row.date);
  }

  overrideDateKeyFromDate(d: Date | string | undefined): string {
    const c = this.coerceDate(d);
    if (!c) return '';
    const y = c.getFullYear();
    const m = String(c.getMonth() + 1).padStart(2, '0');
    const day = String(c.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  overrideDateKeyFromYyyyMmDd(s: string): string {
    return s.trim();
  }

  overrideTrack(row: ScheduleOverrideDto): string {
    return this.overrideDateKey(row) || 'unknown-date';
  }

  confirmRemove(row: ScheduleOverrideDto) {
    const key = this.overrideDateKey(row);
    if (!key) return;

    this.confirmationService.confirm({
      message: 'Usunąć ustawione godziny dla tego dnia? Grafik wróci do powtarzalnego (i urlopów).',
      header: 'Usuwanie godzin dnia',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Tak',
      rejectLabel: 'Nie',
      accept: () => {
        if (!this.coerceDate(row.date)) return;
        this.removingKey.set(key);
        this.scheduleOverridesApi.removeOverride(this.id(), key).subscribe({
          next: () => {
            this.removingKey.set(null);
            if (this.editingKey() === key) {
              this.resetDraft();
            }
            this.messageService.add({
              severity: 'success',
              summary: 'Usunięto',
              detail: 'Godziny dnia zostały usunięte.',
              life: 4000
            });
            this.overridesData.reload();
          },
          error: () => {
            this.removingKey.set(null);
          }
        });
      }
    });
  }

  formatOverrideHeading(row: ScheduleOverrideDto): string {
    const c = this.coerceDate(row.date);
    if (!c) return '—';
    return c.toLocaleDateString('pl-PL', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric'
    });
  }

  formatOverrideDetail(row: ScheduleOverrideDto): string {
    if (row.slotGenerationMode === SlotGenerationMode.FixedStartTimes) {
      const times = (row.fixedStartTimes ?? [])
        .filter((t) => !!t)
        .map((t) => this.trimTime(t))
        .sort((a, b) => a.localeCompare(b));
      return times.length ? `Ustalone godziny: ${times.join(', ')}` : 'Dzień wolny (nadpisanie grafiku)';
    }
    const work = (row.workRanges ?? []).filter((r) => !!r?.startTime && !!r?.endTime);
    if (!work.length) {
      return 'Dzień wolny (nadpisanie grafiku)';
    }
    const segments = work
      .slice()
      .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''))
      .map((r) => `${this.trimTime(r.startTime)}–${this.trimTime(r.endTime)}`);
    const breaks = (row.breaks ?? [])
      .filter((b) => !!b?.startTime && !!b?.endTime)
      .map((b) => `${this.trimTime(b.startTime)}–${this.trimTime(b.endTime)}`);
    const breaksLabel = breaks.length ? ` (przerwy: ${breaks.join(', ')})` : '';
    return `${segments.join(', ')}${breaksLabel}`;
  }

  goBack() {
    safeBackWith(this.location, this.router, this.availabilityHubLink() as string | string[]);
  }

  goToAvailabilityHub() {
    this.router.navigate(this.availabilityHubLink());
  }

  private trimTime(t: string | undefined): string {
    if (!t) return '';
    return t.substring(0, 5);
  }

  /** Wczytanie initial daty z `?date=YYYY-MM-DD` — wywoływane w polowej inicjalizacji draftDate. */
  private readInitialDateFromQuery(): string | null {
    const d = this.route.snapshot.queryParamMap.get('date');
    return d && /^\d{4}-\d{2}-\d{2}$/.test(d) ? d : null;
  }

  /** Sekundy od północy — do porównań i wykrywania nakładania przedziałów. */
  private timeToSecondsFromMidnight(raw: string): number {
    const t = this.toIsoTimeOnly(raw);
    const [hh, mm, ss] = t.split(':').map((x) => parseInt(x, 10));
    return hh * 3600 + mm * 60 + ss;
  }

  private validateWorkingRanges(blocks: { startTime: string; endTime: string }[]): string | null {
    const ranges = blocks.map((b) => ({
      start: this.timeToSecondsFromMidnight(b.startTime),
      end: this.timeToSecondsFromMidnight(b.endTime)
    }));
    for (const r of ranges) {
      if (r.start >= r.end) {
        return 'Godzina zakończenia musi być późniejsza niż początek bloku pracy.';
      }
    }
    const sorted = [...ranges].sort((x, y) => x.start - y.start);
    for (let i = 0; i < sorted.length - 1; i++) {
      const cur = sorted[i];
      const next = sorted[i + 1];
      if (cur.end > next.start) {
        return 'Bloki pracy nie mogą się nakładać — usuń zdublowane lub przecinające się bloki.';
      }
    }
    return null;
  }

  private validateBreaks(
    workBlocks: { startTime: string; endTime: string }[],
    breaks: { startTime: string; endTime: string }[],
  ): string | null {
    const ranges = workBlocks.map((b) => ({
      start: this.timeToSecondsFromMidnight(b.startTime),
      end: this.timeToSecondsFromMidnight(b.endTime),
    }));
    for (const br of breaks) {
      const bs = this.timeToSecondsFromMidnight(br.startTime);
      const be = this.timeToSecondsFromMidnight(br.endTime);
      if (bs >= be) {
        return 'Godzina zakończenia przerwy musi być późniejsza niż rozpoczęcia.';
      }
      if (!ranges.some((r) => r.start <= bs && be <= r.end)) {
        return `Przerwa ${br.startTime}–${br.endTime} nie mieści się w żadnym bloku pracy.`;
      }
    }
    const sorted = [...breaks]
      .map((b) => ({
        start: this.timeToSecondsFromMidnight(b.startTime),
        end: this.timeToSecondsFromMidnight(b.endTime),
      }))
      .sort((x, y) => x.start - y.start);
    for (let i = 0; i < sorted.length - 1; i++) {
      if (sorted[i].end > sorted[i + 1].start) {
        return 'Przerwy nie mogą się nakładać.';
      }
    }
    return null;
  }

  /** Format pod TimeOnly w API (np. "09:00:00"); unika podwójnego ":00" przy już pełnym czasie. */
  private toIsoTimeOnly(raw: string | undefined): string {
    if (!raw?.trim()) return '00:00:00';
    const parts = raw.trim().split(':').map((p) => p.trim());
    const h = (parts[0] ?? '0').padStart(2, '0');
    const m = (parts[1] ?? '0').padStart(2, '0');
    const s = (parts[2] ?? '0').padStart(2, '0');
    return `${h}:${m}:${s}`;
  }

  private coerceDate(value: Date | string | undefined): Date | null {
    if (value == null) return null;
    if (value instanceof Date) {
      return isNaN(value.getTime()) ? null : value;
    }
    const d = new Date(value);
    return isNaN(d.getTime()) ? null : d;
  }
}

function plWizytOutside(n: number): string {
  if (n === 1) return 'wizyta';
  const lastTwo = n % 100;
  const last = n % 10;
  if (lastTwo >= 12 && lastTwo <= 14) return 'wizyt';
  if (last >= 2 && last <= 4) return 'wizyty';
  return 'wizyt';
}

function plJest(n: number): string {
  return n === 1 ? 'jest' : 'są';
}
