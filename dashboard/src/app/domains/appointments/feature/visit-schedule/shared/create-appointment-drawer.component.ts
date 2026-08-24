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
import { rxResource } from '@angular/core/rxjs-interop';
import { catchError, forkJoin, of } from 'rxjs';
import { map } from 'rxjs/operators';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { InputNumber } from 'primeng/inputnumber';
import { MessageService } from 'primeng/api';
import {
  AppointmentsClient,
  AppointmentSlotDto,
  CreateAppointmentCommand,
  CustomersClient,
  EmployeesClient,
  ServiceCategoriesClient,
  ServiceCategoryDto,
  ServicesClient,
} from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { FormDrawerShellComponent } from '@shared/ui/drawer/form-drawer-shell.component';
import {
  groupServicesByCategory,
  shouldShowCategoryHeaders,
} from '../../../data-access/service-category-groups.util';
import { SlotPickerComponent } from './slot-picker.component';
import { AppointmentDatePickerComponent } from './appointment-date-picker.component';
import { startOfDay } from './date-utils';
import { toggleServiceSelection } from './combo-select.util';

export interface CreateAppointmentContext {
  employeeId?: string;
  date: string; // yyyy-MM-dd
  /**
   * Godzina do wstępnego zaznaczenia (klik w kafelek „Wolny termin" grafiku statycznego), 'HH:mm'.
   * Dopasowywana do faktycznie dostępnego slotu po prefiksie HH:mm, gdy sloty się załadują.
   */
  startTime?: string;
  /**
   * Pre-fill dla flow „Umów ponownie" — gdy user klika rebook na zakończonej wizycie,
   * od razu uzupełniamy klienta i usługę. Data pozostaje pusta (`date: ''`) — user wybiera nową.
   */
  prefill?: {
    serviceId?: string;
    customerId?: string;
    customerMode?: 'list' | 'guest';
  };
}

/** Opcja usługi w combo: usługa główna albo dodatek (isAddon) z listą dozwolonych dodatków. */
interface ServiceComboOption {
  label: string;
  value: string;
  group: string;
  /** Kategoria katalogowa — wyłącznie do podziału listy na sekcje. Nie mylić z `group`. */
  categoryId: string | null;
  isAddon: boolean;
  addonIds: string[];
  /** Czas trwania usługi rozwiązany dla pracownika (customDuration ?? katalog). Do sumy standardowej. */
  duration: number;
}

function toDateOnlyApi(yyyyMmDd: string): Date {
  return { toISOString: () => yyyyMmDd } as unknown as Date;
}

function normalizeStartTime(t: string): string {
  const p = t.trim().split(':').map((x) => x.trim());
  if (p.length >= 3) return `${p[0].padStart(2, '0')}:${p[1].padStart(2, '0')}:${p[2].padStart(2, '0')}`;
  if (p.length >= 2) return `${p[0].padStart(2, '0')}:${p[1].padStart(2, '0')}:00`;
  return '08:00:00';
}

@Component({
  selector: 'app-create-appointment-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, RouterLink, FormDrawerShellComponent, Select, DatePicker, InputNumber, SlotPickerComponent, AppointmentDatePickerComponent],
  template: `
    @if (embedded()) {
      <!-- Tryb osadzony (mobilny arkusz z zakładkami) — bez własnego drawer-shell; stopkę daje rodzic. -->
      <div class="flex flex-col gap-5">
        <ng-container [ngTemplateOutlet]="bodyTpl" />
      </div>
    } @else {
      <app-form-drawer-shell
        [isOpen]="isVisible()"
        title="Nowa wizyta"
        label="Kalendarz"
        [submitLabel]="submitLabel()"
        [submitDisabled]="!canSubmit() || isSubmitting()"
        [submitting]="isSubmitting()"
        (submitClicked)="onSubmit()"
        (closeRequested)="onCancel()"
      >
        <div drawer-body class="flex flex-col gap-5">
          <ng-container [ngTemplateOutlet]="bodyTpl" />
        </div>
      </app-form-drawer-shell>
    }

    <ng-template #bodyTpl>

        <!-- Tryb: wizyta w przeszłości — baner aktywny tylko po wejściu w tryb -->
        @if (pastMode()) {
          <div class="flex items-start gap-3 rounded-xl border border-amber-300/80 dark:border-amber-700/60 bg-amber-50 dark:bg-amber-950/30 px-3 py-2.5">
            <i class="pi pi-history mt-0.5 text-amber-700 dark:text-amber-300" aria-hidden="true"></i>
            <div class="flex flex-col gap-0.5 flex-1 min-w-0">
              <span class="text-sm font-semibold text-amber-900 dark:text-amber-100">
                Tryb: wizyta zakończona w przeszłości
              </span>
              <span class="text-xs text-amber-800/85 dark:text-amber-200/85 leading-relaxed">
                Zapisze się ze statusem „Zakończona" — do uzupełnienia historii klienta.
              </span>
            </div>
            <button
              type="button"
              (click)="setPastMode(false)"
              [disabled]="isSubmitting()"
              class="shrink-0 rounded-lg px-2 py-1 text-xs font-bold text-amber-800 dark:text-amber-200 hover:bg-amber-100/70 dark:hover:bg-amber-900/40 transition-colors disabled:opacity-50"
              aria-label="Wyjdź z trybu wizyty w przeszłości"
            >
              Anuluj tryb
            </button>
          </div>
        }

        <!-- Tryb: zapis poza grafikiem — jawnie pomija godziny pracy/grafik -->
        @if (offScheduleMode()) {
          <div class="flex items-start gap-3 rounded-xl border border-sky-300/80 dark:border-sky-700/60 bg-sky-50 dark:bg-sky-950/30 px-3 py-2.5" data-testid="off-schedule-banner">
            <i class="pi pi-calendar-times mt-0.5 text-sky-700 dark:text-sky-300" aria-hidden="true"></i>
            <div class="flex flex-col gap-0.5 flex-1 min-w-0">
              <span class="text-sm font-semibold text-sky-900 dark:text-sky-100">
                Tryb: zapis poza grafikiem
              </span>
              <span class="text-xs text-sky-800/85 dark:text-sky-200/85 leading-relaxed">
                Pomijamy grafik i godziny pracy — wpisz dowolną godzinę. Zablokuje tylko kolizja z inną wizytą.
              </span>
            </div>
            <button
              type="button"
              (click)="setOffScheduleMode(false)"
              [disabled]="isSubmitting()"
              class="shrink-0 rounded-lg px-2 py-1 text-xs font-bold text-sky-800 dark:text-sky-200 hover:bg-sky-100/70 dark:hover:bg-sky-900/40 transition-colors disabled:opacity-50"
              aria-label="Wyjdź z trybu zapisu poza grafikiem"
            >
              Anuluj tryb
            </button>
          </div>
        }

        <!-- Pracownik — selektor pomijany dla salonu jednoosobowego (auto-przypisany) oraz dla
             pracownika scoped (OwnCalendarOnly / TeamReadOnly), który może tworzyć tylko własne
             wizyty — wtedy employeeId jest auto-ustawiony na zalogowanego pracownika. -->
        @if (allowEmployeeChange() && employeeOptions().length > 1) {
          <div class="flex flex-col gap-1.5" data-tour="appointment-employee">
            <label class="admin-section-label">Pracownik</label>
            <p-select
              [options]="employeeOptions()"
              [ngModel]="employeeId()"
              (ngModelChange)="onEmployeeChange($event)"
              placeholder="Wybierz pracownika"
              [disabled]="isSubmitting()"
              [fluid]="true"
              appendTo="body"
            />
          </div>
        }

        <!-- Usługi (combo — można wybrać kilka; max jedna z danej grupy wariantów) -->
        <div class="flex flex-col gap-1.5" data-tour="appointment-services">
          <label class="admin-section-label">Usługi</label>
          @if (!employeeId()) {
            <p class="text-sm text-surface-500 dark:text-surface-400">Najpierw wybierz pracownika.</p>
          } @else if (servicesResource.isLoading()) {
            <div class="h-10 bg-surface-100 dark:bg-surface-100 animate-pulse rounded-xl"></div>
          } @else if (mainOptions().length === 0) {
            <p class="text-xs text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-950/40 rounded-xl px-3 py-2 border border-amber-200/80 dark:border-amber-800/60">
              Pracownik nie ma przypisanych usług.
            </p>
          } @else {
            <div class="flex flex-col gap-3" data-testid="create-service-chips">
              @for (group of mainGroups(); track group.key) {
                <div class="flex flex-col gap-1.5">
                  @if (showCategoryHeaders()) {
                    <!-- Kreska odróżnia nagłówek sekcji od etykiety pola (obie są UPPERCASE). -->
                    <div class="flex items-center gap-2">
                      <span
                        class="text-[0.65rem] font-bold uppercase tracking-[0.12em] text-surface-500 dark:text-surface-400"
                        data-testid="create-service-category"
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
                        {{ opt.label }}
                        @if (isGroupAlternative(opt)) {
                          <span class="ml-1 text-[10px] font-semibold text-amber-600">↺</span>
                        }
                      </button>
                    }
                  </div>
                </div>
              }
            </div>

            @if (availableAddonOptions().length > 0) {
              <label class="admin-section-label mt-1">Dodatki</label>
              <div class="flex flex-wrap gap-2" data-testid="create-addon-chips">
                @for (opt of availableAddonOptions(); track opt.value) {
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
                    class="px-3 py-1.5 rounded-full border border-dashed border-surface-300 dark:border-surface-300 text-sm font-bold hover:border-primary transition-colors bg-surface-0 dark:bg-surface-50 text-surface-700 dark:text-surface-300 disabled:cursor-not-allowed"
                  >
                    + {{ opt.label }}
                  </button>
                }
              </div>
            }

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
                    data-testid="create-duration-input"
                  />
                  @if (customDurationMinutes() !== null) {
                    <span class="text-xs font-semibold text-primary" data-testid="create-duration-custom-badge">
                      czas własny (standard {{ standardDurationMinutes() }} min)
                    </span>
                  }
                </div>
                <p class="text-xs text-surface-500 dark:text-surface-400">
                  {{ serviceIds().length > 1 ? serviceIds().length + ' usługi — ' : '' }}kalendarz
                  blokuje ten czas. Klientka w rezerwacji online widzi standardowy czas usługi.
                </p>
              </div>
            }
          }
        </div>

        <!-- Klient -->
        <div class="flex flex-col gap-2.5" data-tour="appointment-customer">
          <label class="admin-section-label">Klient</label>
          <div class="flex flex-wrap gap-2">
            @for (m of customerModes; track m.value) {
              <button
                type="button"
                (click)="setCustomerMode(m.value)"
                [disabled]="isSubmitting()"
                class="px-3 py-1.5 rounded-xl text-sm font-semibold border transition-colors disabled:opacity-50"
                [class.border-primary]="customerMode() === m.value"
                [class.bg-primary/10]="customerMode() === m.value"
                [class.text-primary]="customerMode() === m.value"
                [class.border-surface-300]="customerMode() !== m.value"
                [class.dark:border-surface-600]="customerMode() !== m.value"
                [class.text-surface-700]="customerMode() !== m.value"
                [class.dark:text-surface-300]="customerMode() !== m.value"
              >{{ m.label }}</button>
            }
          </div>

          @if (customerMode() === 'list') {
            <p-select
              [options]="customerOptions()"
              [ngModel]="customerId()"
              (ngModelChange)="customerId.set($event)"
              placeholder="Wybierz klienta"
              [disabled]="isSubmitting() || customersResource.isLoading()"
              [filter]="true"
              filterBy="label"
              [fluid]="true"
              appendTo="body"
            />
            @if (!customersResource.isLoading() && customerOptions().length === 0) {
              <p class="text-xs text-surface-500 dark:text-surface-400">
                Brak klientów — dodaj przez CRM lub wybierz inny tryb.
              </p>
            }
          } @else if (customerMode() === 'phone') {
            <input
              type="tel"
              [ngModel]="customerPhone()"
              (ngModelChange)="customerPhone.set($event)"
              [disabled]="isSubmitting()"
              placeholder="np. 500 600 700"
              class="w-full rounded-xl border border-surface-300 dark:border-surface-600 bg-white dark:bg-surface-50 px-3 py-2.5 text-sm text-surface-900 placeholder:text-surface-400 focus:outline-none focus:ring-2 focus:ring-primary/40 focus:border-primary/50 disabled:opacity-50"
            />
            <p class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed">
              Jeśli numeru nie ma w bazie, zostanie utworzony nowy klient.
            </p>
          } @else {
            <p class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed">
              Wizyta bez danych klienta — w kalendarzu wyświetli się jako „Gość".
            </p>
          }
        </div>

        <!-- Data -->
        <div class="flex flex-col gap-1.5" data-tour="appointment-date">
          <label for="create-date" class="admin-section-label">Data wizyty</label>
          <app-appointment-date-picker
            inputId="create-date"
            [employeeId]="pastMode() ? '' : employeeId()"
            [serviceId]="pastMode() ? '' : primaryServiceId()"
            [value]="date()"
            [minDate]="pastMode() ? undefined : minDate"
            [maxDate]="pastMode() ? minDate : undefined"
            [showLegend]="!pastMode()"
            [disabled]="isSubmitting()"
            (valueChange)="onDateChange($event)"
          />
        </div>

        <!-- Sloty / Godzina -->
        @if (manualTimeMode()) {
          <div class="flex flex-col gap-1.5">
            <label for="create-past-time" class="admin-section-label">Godzina rozpoczęcia</label>
            <p-date-picker
              inputId="create-past-time"
              [timeOnly]="true"
              hourFormat="24"
              [showIcon]="true"
              [readonlyInput]="true"
              [fluid]="true"
              appendTo="body"
              [ngModel]="pastTimeAsDate()"
              (ngModelChange)="onPastTimeChange($event)"
              [disabled]="isSubmitting()"
            />
          </div>
        } @else if (employeeId() && serviceIds().length > 0 && date()) {
          <div class="flex flex-col gap-1.5">
            <label class="admin-section-label">Godzina</label>
            <app-slot-picker
              [slots]="slotsResource.value()"
              [value]="slot()"
              [disabled]="isSubmitting()"
              [loading]="slotsResource.isLoading()"
              [loadError]="slotsLoadError()"
              (valueChange)="slot.set($event)"
            />

            <!-- Fallback gdy brak dostępnych slotów: sugerujemy najczęstsze powody + CTA do edycji grafiku -->
            @if (showNoSlotsHint()) {
              <div class="rounded-xl border border-amber-300/60 dark:border-amber-700/40 bg-amber-50/70 dark:bg-amber-950/30 p-3 mt-2 text-xs text-amber-900 dark:text-amber-100">
                <p class="font-bold mb-1.5 flex items-center gap-1.5">
                  <i class="pi pi-info-circle" aria-hidden="true"></i>
                  Brak dostępnych godzin
                </p>
                <p class="text-amber-800 dark:text-amber-200/90 leading-relaxed mb-2">
                  Najczęstsze powody: brak grafiku w tym dniu, urlop, lub wszystkie sloty są zajęte.
                </p>
                <div class="flex flex-wrap gap-2">
                  <button
                    type="button"
                    (click)="setOffScheduleMode(true)"
                    [disabled]="isSubmitting()"
                    class="inline-flex items-center gap-1.5 rounded-full bg-sky-600 text-white hover:bg-sky-700 px-3 py-1.5 text-[11px] font-bold transition-colors disabled:opacity-50"
                  >
                    <i class="pi pi-calendar-times"></i>
                    Zapisz poza grafikiem
                  </button>
                  <a
                    [routerLink]="scheduleEditRouterLink()"
                    (click)="onCancel()"
                    class="inline-flex items-center gap-1.5 rounded-full bg-amber-500 text-amber-950 hover:bg-amber-600 px-3 py-1.5 text-[11px] font-bold transition-colors"
                  >
                    <i class="pi pi-calendar"></i>
                    Sprawdź grafik
                  </a>
                </div>
              </div>
            }
          </div>
        }

        @if (submitError()) {
          <p class="rounded-xl bg-red-50 dark:bg-red-950/30 border border-red-200/70 dark:border-red-800/50 px-3 py-2 text-xs text-red-700 dark:text-red-300">
            {{ submitError() }}
          </p>
        }

        @if (!pastMode() && !offScheduleMode()) {
          <div class="pt-3 mt-1 border-t border-surface-200/70 dark:border-surface-200/60 flex flex-col gap-2.5">
            <p class="text-xs font-semibold uppercase tracking-wider text-surface-400 dark:text-surface-500">
              Inne opcje zapisu
            </p>
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
              <button
                type="button"
                (click)="setOffScheduleMode(true)"
                [disabled]="isSubmitting()"
                class="flex items-center gap-3 rounded-xl border-2 border-sky-300 dark:border-sky-700/70 bg-sky-50/70 dark:bg-sky-950/30 px-3.5 py-3 text-left hover:border-sky-500 hover:bg-sky-100/70 dark:hover:bg-sky-900/40 transition-colors disabled:opacity-50"
                data-testid="off-schedule-toggle"
              >
                <span class="shrink-0 grid place-items-center w-9 h-9 rounded-full bg-sky-500/15 text-sky-700 dark:text-sky-300">
                  <i class="pi pi-calendar-times text-base" aria-hidden="true"></i>
                </span>
                <span class="flex flex-col min-w-0">
                  <span class="text-sm font-bold text-sky-900 dark:text-sky-100">Zapisz poza grafikiem</span>
                  <span class="text-xs text-sky-700/80 dark:text-sky-300/80">Dowolna godzina, bez grafiku</span>
                </span>
              </button>
              <button
                type="button"
                (click)="setPastMode(true)"
                [disabled]="isSubmitting()"
                class="flex items-center gap-3 rounded-xl border-2 border-surface-200 dark:border-surface-700 bg-surface-50/70 dark:bg-surface-900/30 px-3.5 py-3 text-left hover:border-amber-400 hover:bg-amber-50/60 dark:hover:bg-amber-950/20 transition-colors disabled:opacity-50"
              >
                <span class="shrink-0 grid place-items-center w-9 h-9 rounded-full bg-amber-500/15 text-amber-700 dark:text-amber-300">
                  <i class="pi pi-history text-base" aria-hidden="true"></i>
                </span>
                <span class="flex flex-col min-w-0">
                  <span class="text-sm font-bold text-surface-800">Wizyta z przeszłości</span>
                  <span class="text-xs text-surface-500 dark:text-surface-400">Uzupełnij historię klienta</span>
                </span>
              </button>
            </div>
          </div>
        }

      </ng-template>
  `,
})
export class CreateAppointmentDrawerComponent {
  readonly context = input<CreateAppointmentContext | null>(null);
  /**
   * Czy użytkownik może tworzyć wizyty dla innych pracowników. Owner/Manager/TeamFull = true.
   * Pracownik scoped = false → selektor pracownika ukryty, wizyta trafia na jego własne id.
   */
  readonly allowEmployeeChange = input<boolean>(true);
  /** Tryb osadzony (mobilny arkusz z zakładkami) — bez własnego drawer-shell; stopkę daje rodzic. */
  readonly embedded = input<boolean>(false);

  readonly closeRequested = output<void>();
  readonly success = output<string>();

  private readonly appointmentsClient = inject(AppointmentsClient);
  private readonly employeesClient = inject(EmployeesClient);
  private readonly servicesClient = inject(ServicesClient);
  private readonly serviceCategoriesClient = inject(ServiceCategoriesClient);
  private readonly customersClient = inject(CustomersClient);
  private readonly messages = inject(MessageService);
  private readonly auth = inject(AuthSessionService);

  protected readonly customerModes = [
    { value: 'list' as const, label: 'Z listy' },
    { value: 'phone' as const, label: 'Numer telefonu' },
    { value: 'guest' as const, label: 'Gość' },
  ] as const;

  protected readonly minDate = startOfDay(new Date());
  protected readonly isVisible = computed(() => this.context() != null);

  // Form state
  protected readonly employeeId = signal('');
  /** Wybrane usługi combo (pierwsza = główna). Single = 1 element. */
  protected readonly serviceIds = signal<string[]>([]);
  protected readonly primaryServiceId = computed(() => this.serviceIds()[0] ?? '');
  protected readonly MAX_COMBO_SERVICES = 5;
  protected readonly date = signal('');
  protected readonly slot = signal<string | null>(null);
  protected readonly customerId = signal('');
  protected readonly customerPhone = signal('');
  protected readonly customerMode = signal<'list' | 'phone' | 'guest'>('guest');
  protected readonly pastMode = signal(false);
  /** Zapis „poza grafikiem" — pomija grafik/godziny pracy, wysyła ignoreSchedule=true. */
  protected readonly offScheduleMode = signal(false);
  /** Oba tryby (przeszłość / poza grafikiem) zastępują siatkę slotów ręcznym pickerem godziny. */
  protected readonly manualTimeMode = computed(() => this.pastMode() || this.offScheduleMode());
  protected readonly isSubmitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly slotsLoadError = signal(false);

  /** Publiczne API dla rodzica w trybie embedded (mobilny arkusz steruje wspólną stopką). */
  readonly submitting = this.isSubmitting.asReadonly();
  readonly submitLabel = computed(() =>
    this.pastMode() ? 'Zapisz wizytę zakończoną' : 'Zarezerwuj',
  );
  /** Wyzwala submit z zewnątrz (stopka arkusza). */
  submit(): void {
    this.onSubmit();
  }

  readonly employeesResource = rxResource({
    stream: () => this.employeesClient.getEmployees(),
  });

  readonly customersResource = rxResource({
    stream: () => this.customersClient.getCustomers(),
  });

  readonly servicesResource = rxResource({
    params: () => this.employeeId(),
    stream: ({ params: empId }) => {
      if (!empId) return of<ServiceComboOption[]>([]);
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
                group: (svc.comboGroup ?? '').trim().toLowerCase(),
                categoryId: svc.categoryId ?? null,
                isAddon: svc.isAddon ?? false,
                addonIds: svc.addonServiceIds ?? [],
                // Czas per-pracownik: override EmployeeService, w razie braku katalogowy czas usługi.
                duration: link.customDuration ?? svc.durationInMinutes ?? 0,
              } satisfies ServiceComboOption;
            });
        }),
        catchError(() => of<ServiceComboOption[]>([]))
      );
    },
  });

  /** Kategorie katalogu — niezależne od pracownika, więc ładowane raz. Błąd → lista płaska. */
  readonly categoriesResource = rxResource({
    defaultValue: [] as ServiceCategoryDto[],
    stream: () =>
      this.serviceCategoriesClient
        .getServiceCategories()
        .pipe(catchError(() => of([] as ServiceCategoryDto[]))),
  });

  readonly slotsResource = rxResource({
    params: () => {
      const emp = this.employeeId();
      const svc = this.serviceIds();
      const d = this.date();
      if (!emp || svc.length === 0 || !d) return undefined;
      return [emp, svc.join(','), d].join('\x1e');
    },
    defaultValue: [] as AppointmentSlotDto[],
    stream: () => {
      const emp = this.employeeId();
      const svc = this.serviceIds();
      const d = this.date();
      if (!emp || svc.length === 0 || !d) return of([] as AppointmentSlotDto[]);
      this.slotsLoadError.set(false);
      return this.appointmentsClient
        .getAvailableSlots(toDateOnlyApi(d), emp, svc)
        .pipe(catchError(() => { this.slotsLoadError.set(true); return of([] as AppointmentSlotDto[]); }));
    },
  });

  readonly employeeOptions = computed(() =>
    (this.employeesResource.value() ?? []).map((e) => ({
      label: [e.firstName, e.lastName].filter(Boolean).join(' ') || 'Pracownik',
      value: e.id!,
    }))
  );

  readonly serviceOptions = computed<ServiceComboOption[]>(() => this.servicesResource.value() ?? []);

  /** Usługi główne (nie-dodatki) — pokazywane zawsze. */
  readonly mainOptions = computed(() => this.serviceOptions().filter((o) => !o.isAddon));

  /** Usługi główne w sekcjach kategorii — układ listy wyboru. */
  readonly mainGroups = computed(() =>
    groupServicesByCategory(this.mainOptions(), this.categoriesResource.value() ?? []),
  );

  readonly showCategoryHeaders = computed(() => shouldShowCategoryHeaders(this.mainGroups()));

  /** Dodatki dostępne dla aktualnie wybranych usług głównych (suma ich list dozwolonych dodatków). */
  readonly availableAddonOptions = computed(() => {
    const selected = new Set(this.serviceIds());
    const allowed = new Set(
      this.mainOptions()
        .filter((o) => selected.has(o.value))
        .flatMap((o) => o.addonIds),
    );
    return this.serviceOptions().filter((o) => o.isAddon && allowed.has(o.value));
  });

  /** Suma standardowych czasów wybranych usług (minuty) — domyślna długość bloku. */
  readonly standardDurationMinutes = computed(() => {
    const selected = new Set(this.serviceIds());
    return this.serviceOptions()
      .filter((o) => selected.has(o.value))
      .reduce((sum, o) => sum + (o.duration ?? 0), 0);
  });

  /**
   * Niestandardowy czas trwania wpisany przez personel (minuty). `null` = użyj standardowej sumy.
   * Nadpisuje długość bloku w kalendarzu (nie zmienia czasu widzianego przez klientkę online).
   */
  protected readonly customDurationMinutes = signal<number | null>(null);

  /** Efektywny czas w polu: override, a gdy brak — standardowa suma. */
  readonly effectiveDurationMinutes = computed(
    () => this.customDurationMinutes() ?? this.standardDurationMinutes(),
  );

  protected onDurationChange(value: number | null): void {
    // Wartość równa standardowi (lub pusta) = brak override → wysyłamy null.
    this.customDurationMinutes.set(
      value == null || value === this.standardDurationMinutes() ? null : value,
    );
  }

  readonly customerOptions = computed(() =>
    (this.customersResource.value() ?? []).map((c) => ({
      label: [c.firstName, c.lastName].filter(Boolean).join(' ') || c.phoneNumber || 'Klient',
      value: c.id!,
    }))
  );

  protected readonly pastTimeAsDate = computed<Date | null>(() => {
    const s = this.slot();
    if (!s) return null;
    const m = /^(\d{2}):(\d{2})(?::\d{2})?$/.exec(s);
    if (!m) return null;
    const d = new Date();
    d.setHours(Number(m[1]), Number(m[2]), 0, 0);
    return d;
  });

  /**
   * Hint o braku slotów — pokazujemy gdy: pełen kontekst wybrany (pracownik+usługa+data),
   * resource się wczytał (nie loading, nie error), a lista pusta. Nie pokazujemy w pastMode
   * (tam time-picker, nie slot picker).
   */
  readonly showNoSlotsHint = computed(() => {
    if (this.manualTimeMode()) return false;
    if (!this.employeeId() || this.serviceIds().length === 0 || !this.date()) return false;
    if (this.slotsResource.isLoading()) return false;
    if (this.slotsLoadError()) return false;
    return (this.slotsResource.value() ?? []).length === 0;
  });

  /** Link do edycji grafiku — zależny od bieżącej roli (owner-as-employee vs admin/manager). */
  readonly scheduleEditRouterLink = computed<string[]>(() => {
    const emp = this.employeeId();
    if (!emp) return ['/admin/my-availability'];
    // Owner edytujący własny grafik vs admin edytujący cudzy
    return this.auth.currentEmployeeId() === emp
      ? ['/admin/my-availability', emp, 'schedules']
      : ['/admin/resources/employees', emp, 'schedules'];
  });

  readonly canSubmit = computed(() => {
    if (!this.employeeId() || this.serviceIds().length === 0 || !this.date() || !this.slot()) return false;
    if (this.manualTimeMode() && !/^\d{2}:\d{2}(:\d{2})?$/.test(this.slot() ?? '')) return false;
    const mode = this.customerMode();
    if (mode === 'list' && !this.customerId()) return false;
    if (mode === 'phone' && this.customerPhone().replace(/\D/g, '').length < 9) return false;
    return true;
  });

  constructor() {
    // Reset / initialize form when drawer opens or closes
    effect(() => {
      const ctx = this.context();
      const employees = this.employeesResource.value();
      untracked(() => {
        if (!ctx) { this.reset(); return; }
        // Resolve employee: context hint → logged-in employee → first from list
        const resolved =
          ctx.employeeId ||
          this.auth.currentEmployeeId() ||
          employees?.[0]?.id ||
          '';
        this.employeeId.set(resolved);
        this.date.set(ctx.date);
        this.slot.set(null);
        this.pastMode.set(false);
        this.offScheduleMode.set(false);
        this.isSubmitting.set(false);
        this.submitError.set(null);
        this.slotsLoadError.set(false);

        // Pre-fill dla „Umów ponownie" — bierzemy serviceId i klienta z poprzedniej wizyty.
        const prefill = ctx.prefill;
        this.serviceIds.set(prefill?.serviceId ? [prefill.serviceId] : []);
        if (prefill?.customerMode === 'list' && prefill.customerId) {
          this.customerId.set(prefill.customerId);
          this.customerPhone.set('');
          this.customerMode.set('list');
        } else {
          this.customerId.set('');
          this.customerPhone.set('');
          this.customerMode.set(prefill?.customerMode === 'guest' ? 'guest' : 'guest');
        }
      });
    });

    // Auto-select first MAIN service when list loads and nothing is selected (pre-pick głównej usługi).
    effect(() => {
      const options = this.mainOptions();
      untracked(() => {
        if (this.serviceIds().length === 0 && options.length > 0) {
          this.serviceIds.set([options[0].value]);
        }
      });
    });

    // Wstępne zaznaczenie slotu z kontekstu (klik w „Wolny termin"): gdy sloty się załadują i nic
    // jeszcze nie wybrano, dopasuj po prefiksie HH:mm (format slotu bywa HH:mm lub HH:mm:ss).
    effect(() => {
      const slots = this.slotsResource.value();
      const ctx = this.context();
      untracked(() => {
        const want = ctx?.startTime;
        if (!want || this.slot() != null || !slots?.length) return;
        const target = want.substring(0, 5);
        const match = slots.find((s) => (s.slot ?? '').substring(0, 5) === target);
        if (match?.slot) this.slot.set(match.slot);
      });
    });
  }

  protected onEmployeeChange(id: string): void {
    this.employeeId.set(id);
    this.serviceIds.set([]);
    this.slot.set(null);
  }

  /** Czy usługa jest wybrana w combo. */
  protected isServiceSelected(id: string): boolean {
    return this.serviceIds().includes(id);
  }

  /** Czy usługa to alternatywa z grupy już zajętej (klik ją podmieni). */
  protected isGroupAlternative(opt: { value: string; group: string }): boolean {
    if (!opt.group || this.isServiceSelected(opt.value)) return false;
    return this.serviceOptions().some(
      (o) => o.group === opt.group && this.isServiceSelected(o.value),
    );
  }

  protected isServiceDisabled(opt: { value: string; group: string }): boolean {
    return (
      this.serviceIds().length >= this.MAX_COMBO_SERVICES &&
      !this.isServiceSelected(opt.value) &&
      !this.isGroupAlternative(opt)
    );
  }

  /**
   * Przełącza usługę w combo z regułą grup wariantów (patrz {@link toggleServiceSelection}).
   * Zmiana składu czyści wybrany slot (dostępność zależy od sumy czasu combo).
   * Po odznaczeniu usługi głównej usuwa „osierocone" dodatki (już niedozwolone).
   */
  protected toggleService(id: string): void {
    const opts = this.serviceOptions();
    const groupOf = (x: string) => opts.find((o) => o.value === x)?.group ?? '';
    let next = toggleServiceSelection(this.serviceIds(), id, groupOf, this.MAX_COMBO_SERVICES);

    // Usuń dodatki, które nie są już dozwolone przez żadną z wybranych usług głównych.
    const selected = new Set(next);
    const allowedAddons = new Set(
      opts.filter((o) => !o.isAddon && selected.has(o.value)).flatMap((o) => o.addonIds),
    );
    next = next.filter((sid) => {
      const opt = opts.find((o) => o.value === sid);
      return !opt?.isAddon || allowedAddons.has(sid);
    });

    this.serviceIds.set(next);
    this.slot.set(null);
    // Zmiana składu → pole czasu wraca do nowej standardowej sumy (kasujemy override).
    this.customDurationMinutes.set(null);
  }

  protected onDateChange(value: string): void {
    this.date.set(value ?? '');
    this.slot.set(null);
  }

  protected setCustomerMode(mode: 'list' | 'phone' | 'guest'): void {
    this.customerMode.set(mode);
    if (mode !== 'list') this.customerId.set('');
    if (mode !== 'phone') this.customerPhone.set('');
  }

  protected onPastTimeChange(value: Date | null | undefined): void {
    if (!value) { this.slot.set(null); return; }
    const hh = value.getHours().toString().padStart(2, '0');
    const mm = value.getMinutes().toString().padStart(2, '0');
    this.slot.set(`${hh}:${mm}`);
  }

  protected setPastMode(value: boolean): void {
    if (this.pastMode() === value) return;
    this.pastMode.set(value);
    if (value) this.offScheduleMode.set(false);
    // Date+slot semantics differ between modes — clear to avoid sending future date with CreateAsCompleted.
    this.date.set('');
    this.slot.set(null);
    this.submitError.set(null);
  }

  protected setOffScheduleMode(value: boolean): void {
    if (this.offScheduleMode() === value) return;
    this.offScheduleMode.set(value);
    if (value) this.pastMode.set(false);
    // Data pozostaje (wciąż przyszłość), ale godzina była ze slotu z grafiku — wyczyść.
    this.slot.set(null);
    this.submitError.set(null);
  }

  protected onCancel(): void {
    this.closeRequested.emit();
  }

  protected onSubmit(): void {
    const emp = this.employeeId();
    const svc = this.serviceIds();
    const d = this.date();
    const s = this.slot();
    if (!emp || svc.length === 0 || !d || !s) return;

    const mode = this.customerMode();
    const past = this.pastMode();
    // Zapis poza grafikiem: backend pomija grafik/godziny pracy (blokuje tylko kolizja).
    // Niestandardowy czas dołączamy tylko gdy personel go zmienił (inaczej null = standard).
    const override = this.customDurationMinutes();
    const baseFlags: Partial<CreateAppointmentCommand> = {
      ...(past
        ? { createAsCompleted: true }
        : { createAsBooked: true, ...(this.offScheduleMode() ? { ignoreSchedule: true } : {}) }),
      ...(override !== null ? { customDurationMinutes: override } : {}),
    };
    let cmd: CreateAppointmentCommand;

    if (mode === 'guest') {
      cmd = { employeeId: emp, serviceIds: svc, date: d as unknown as Date, startTime: normalizeStartTime(s), ...baseFlags };
    } else if (mode === 'list') {
      const cid = this.customerId();
      if (!cid) { this.submitError.set('Wybierz klienta z listy.'); return; }
      cmd = { employeeId: emp, serviceIds: svc, customerId: cid, date: d as unknown as Date, startTime: normalizeStartTime(s), ...baseFlags };
    } else {
      const raw = this.customerPhone().trim();
      if (raw.replace(/\D/g, '').length < 9) { this.submitError.set('Podaj co najmniej 9 cyfr numeru telefonu.'); return; }
      cmd = { employeeId: emp, serviceIds: svc, customerPhone: raw as any, date: d as unknown as Date, startTime: normalizeStartTime(s), ...baseFlags };
    }

    this.isSubmitting.set(true);
    this.submitError.set(null);

    this.appointmentsClient.createAppointment(cmd).subscribe({
      next: (id) => {
        this.isSubmitting.set(false);
        this.messages.add({
          severity: 'success',
          summary: past ? 'Wizyta zapisana' : 'Wizyta dodana',
          detail: past ? 'Wizyta z przeszłości została dodana jako zakończona.' : 'Wizyta została zarezerwowana.',
          life: 3000,
        });
        this.success.emit(id ?? emp);
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        const status = (err as { status?: number })?.status;
        if (status === 409) this.submitError.set('Ten slot jest już zajęty — wybierz inny termin.');
        else if (status === 400) this.submitError.set('Nieprawidłowe dane — sprawdź godziny pracy i spróbuj ponownie.');
        else if (status === 403) this.submitError.set('Brak uprawnień do tworzenia wizyt.');
        else this.submitError.set('Nie udało się dodać wizyty. Spróbuj ponownie.');
      },
    });
  }

  private reset(): void {
    this.employeeId.set('');
    this.serviceIds.set([]);
    this.customDurationMinutes.set(null);
    this.date.set('');
    this.slot.set(null);
    this.customerId.set('');
    this.customerPhone.set('');
    this.customerMode.set('list');
    this.pastMode.set(false);
    this.offScheduleMode.set(false);
    this.isSubmitting.set(false);
    this.submitError.set(null);
    this.slotsLoadError.set(false);
  }
}
