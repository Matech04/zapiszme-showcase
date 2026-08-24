import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { form, FormField, required } from '@angular/forms/signals';
import { catchError, EMPTY, forkJoin, map, of, throwError } from 'rxjs';
import {
  API_BASE_URL,
  AppointmentsClient,
  AppointmentSlotDto,
  CreateAppointmentCommand,
  CustomersClient,
  EmployeesClient,
  ServiceCategoriesClient,
  ServiceCategoryDto,
  ServicesClient,
} from '@core/api/api-client';
import {
  groupServicesByCategory,
  shouldShowCategoryHeaders,
} from '../../data-access/service-category-groups.util';
import { createFormActions } from '@shared/utils/createFormActions';
import { HasUnsavedChanges } from '@core/guards/has-unsaved-changes';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { FormFieldCalendarComponent } from '@shared/ui/forms/form-field-calendar.component';
import {
  FormFieldSelectComponent,
  type SelectOptionGroup,
} from '@shared/ui/forms/form-field-select.component';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
/** Opcja usługi w polu wyboru — `categoryId` służy wyłącznie podziałowi listy na sekcje. */
interface ServiceOption {
  label: string;
  value: string;
  categoryId: string | null;
}

interface AppointmentFormModel {
  employeeId: string;
  serviceId: string;
  customerId: string;
  customerPhone: string;
  date: string;
  startTime: string;
}

function todayYyyyMmDd(): string {
  const t = new Date();
  return `${t.getFullYear()}-${String(t.getMonth() + 1).padStart(2, '0')}-${String(t.getDate()).padStart(2, '0')}`;
}

/**
 * Heurystyka anty-„wizyta w przeszłości”: porównujemy w strefie przeglądarki, bo nie znamy tu
 * strefy salonu. Backend i tak waliduje w `EnsureStartNotInPast` w strefie tenanta — to tylko
 * UX guard, żeby nie pozwolić klikać minionych slotów (np. `?time=` z URL "Wypełnij gap").
 */
function isPastTimeToday(dateYmd: string, hhmm: string): boolean {
  if (dateYmd !== todayYyyyMmDd()) return false;
  const m = /^(\d{2}):(\d{2})/.exec(hhmm.trim());
  if (!m) return false;
  const now = new Date();
  const target = new Date();
  target.setHours(parseInt(m[1], 10), parseInt(m[2], 10), 0, 0);
  return target.getTime() < now.getTime();
}

/** Klucz żądania do rxResource — bez tego loader nie uruchamia się ponownie (patrz `params` w resource). */
function slotsRequestKey(m: Pick<AppointmentFormModel, 'employeeId' | 'serviceId' | 'date'>): string {
  return [m.employeeId, m.serviceId, m.date].join('\x1e');
}

function normalizeStartTimeForApi(t: string): string {
  const p = t.trim().split(':').map((x) => x.trim());
  if (p.length >= 3) {
    return `${p[0].padStart(2, '0')}:${p[1].padStart(2, '0')}:${p[2].padStart(2, '0')}`;
  }
  if (p.length >= 2) {
    return `${p[0].padStart(2, '0')}:${p[1].padStart(2, '0')}:00`;
  }
  return '08:00:00';
}

function appointmentDtoToModel(row: unknown): AppointmentFormModel {
  const r = row as Record<string, unknown>;
  const dateRaw = r['date'] ?? r['Date'];
  let dateStr = todayYyyyMmDd();
  if (typeof dateRaw === 'string') {
    dateStr = dateRaw.split('T')[0];
  }
  const st = (r['startTime'] ?? r['StartTime']) as string | undefined;
  return {
    employeeId: String(r['employeeId'] ?? r['EmployeeId'] ?? ''),
    serviceId: String(r['serviceId'] ?? r['ServiceId'] ?? ''),
    customerId: String(r['customerId'] ?? r['CustomerId'] ?? ''),
    customerPhone: '',
    date: dateStr,
    startTime: st ? normalizeStartTimeForApi(st) : '',
  };
}

@Component({
  selector: 'app-appointment-form',
  standalone: true,
  imports: [
    FormLayoutComponent,
    FormFieldCalendarComponent,
    FormFieldSelectComponent,
    FormFieldComponent,
    FormField,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-form-layout
      [title]="formTitle()"
      (submit)="FormActions.handleSubmit()"
      (cancel)="FormActions.handleCancel()"
      [isEdit]="isEditMode()"
      [confirmOnCancel]="true"
      [hasUnsavedChanges]="FormActions.hasUnsavedChanges()"
      [testId]="'appointment-form'"
    >
      @if (isEditMode() && appointmentToEdit.isLoading()) {
        <div class="flex justify-center py-16">
          <i class="pi pi-spin pi-spinner text-3xl text-primary" aria-hidden="true"></i>
        </div>
      }
      @if (isEditMode() && appointmentToEdit.error()) {
        <p class="text-sm text-red-600 dark:text-red-400 text-center">Nie udało się wczytać wizyty.</p>
      }

      @if (!isEditMode() || (appointmentToEdit.hasValue() && appointmentToEdit.value())) {
      <div class="grid grid-cols-1 gap-6">
        <app-form-field-select
          testId="appointment-employee"
          label="Pracownik"
          id="employeeId"
          placeholder="Wybierz pracownika"
          [formField]="appointmentForm.employeeId"
          [options]="employeeOptions()"
        />

        <app-form-field-select
          testId="appointment-service"
          label="Usługa"
          id="serviceId"
          placeholder="Najpierw wybierz pracownika"
          [formField]="appointmentForm.serviceId"
          [options]="employeeServiceOptions.value() ?? []"
          [optionGroups]="serviceOptionGroups()"
        />

        @if (
          appointmentModel().employeeId &&
          !employeeServiceOptions.error() &&
          (employeeServiceOptions.value()?.length === 0) &&
          !employeeServiceOptions.isLoading()
        ) {
          <p class="text-sm text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-950/40 rounded-xl px-4 py-3 border border-amber-200/80 dark:border-amber-800/60">
            Ten pracownik nie ma przypisanych usług. Przypisz usługi w panelu zasobów (karta pracownika).
          </p>
        }

        @if (isEditMode()) {
          <div class="flex flex-col gap-2">
            <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1"
              >Klient</label
            >
            <p class="text-surface-900 px-1">{{ editCustomerLabel() }}</p>
          </div>
        } @else {
          <div class="flex flex-col gap-3">
            <span
              class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1"
              >Klient</span
            >
            <div class="flex flex-wrap gap-2 px-1">
              <button
                type="button"
                class="px-4 py-2 rounded-xl text-sm font-semibold border transition-colors"
                [class.border-primary]="customerEntryMode() === 'list'"
                [class.bg-primary/10]="customerEntryMode() === 'list'"
                [class.text-primary]="customerEntryMode() === 'list'"
                [class.border-surface-300]="customerEntryMode() !== 'list'"
                [class.dark:border-surface-600]="customerEntryMode() !== 'list'"
                (click)="setCustomerEntryMode('list')"
              >
                Z listy
              </button>
              <button
                type="button"
                class="px-4 py-2 rounded-xl text-sm font-semibold border transition-colors"
                [class.border-primary]="customerEntryMode() === 'phone'"
                [class.bg-primary/10]="customerEntryMode() === 'phone'"
                [class.text-primary]="customerEntryMode() === 'phone'"
                [class.border-surface-300]="customerEntryMode() !== 'phone'"
                [class.dark:border-surface-600]="customerEntryMode() !== 'phone'"
                (click)="setCustomerEntryMode('phone')"
              >
                Numer telefonu
              </button>
              <button
                type="button"
                class="px-4 py-2 rounded-xl text-sm font-semibold border transition-colors"
                [class.border-primary]="customerEntryMode() === 'guest'"
                [class.bg-primary/10]="customerEntryMode() === 'guest'"
                [class.text-primary]="customerEntryMode() === 'guest'"
                [class.border-surface-300]="customerEntryMode() !== 'guest'"
                [class.dark:border-surface-600]="customerEntryMode() !== 'guest'"
                (click)="setCustomerEntryMode('guest')"
              >
                Gość
              </button>
            </div>

            @if (customerEntryMode() === 'list') {
              <app-form-field-select
                testId="appointment-customer"
                label="Wybór klienta"
                id="customerId"
                placeholder="Wybierz klienta"
                [formField]="appointmentForm.customerId"
                [options]="customerOptions()"
              />
            } @else if (customerEntryMode() === 'phone') {
              <app-form-field
                testId="appointment-customer-phone"
                label="Numer telefonu"
                id="customerPhone"
                type="tel"
                placeholder="np. 500 600 700"
                [formField]="appointmentForm.customerPhone"
              />
              <p class="text-xs text-surface-500 dark:text-surface-400 px-1 leading-relaxed">
                Jeśli numeru nie ma w bazie, zostanie utworzony nowy klient z tym telefonem (imię i nazwisko możesz
                uzupełnić później w CRM).
              </p>
            } @else {
              <p class="text-sm text-surface-600 dark:text-surface-400 px-1 leading-relaxed">
                Wizyta bez danych klienta — w kalendarzu wyświetli się etykieta „Gość”.
              </p>
            }
          </div>
        }

        @if (
          !isEditMode() &&
          customerEntryMode() === 'list' &&
          (customers.value()?.length ?? 0) === 0 &&
          !customers.isLoading()
        ) {
          <p class="text-sm text-surface-600 dark:text-surface-400">
            Brak klientów w bazie — wybierz „Numer telefonu”, „Gość” albo dodaj klienta przez API / CRM.
          </p>
        }

        <app-form-field-calendar
          testId="appointment-date"
          label="Data wizyty"
          id="date"
          [notBefore]="todayYyyyMmDd"
          [formField]="appointmentForm.date"
        />

        <app-form-field-select
          testId="appointment-slot"
          label="Godzina rozpoczęcia"
          id="startTime"
          placeholder="Wybierz dostępny termin"
          [formField]="appointmentForm.startTime"
          [options]="slotOptions()"
        />

        @if (slotList.error()) {
          <p class="text-sm text-red-600 dark:text-red-400">
            Nie udało się pobrać wolnych terminów. Sprawdź datę i spróbuj ponownie.
          </p>
        }

        @if (
          !slotList.error() &&
          appointmentModel().employeeId &&
          appointmentModel().serviceId &&
          appointmentModel().date &&
          !slotList.isLoading() &&
          slotList.hasValue() &&
          (slotList.value().length === 0)
        ) {
          <p class="text-sm text-surface-600 dark:text-surface-400">
            Brak wolnych terminów dla tej kombinacji — zmień datę, usługę lub pracownika.
          </p>
        }

        @if (slotList.isLoading()) {
          <p class="text-sm text-surface-500 flex items-center gap-2">
            <i class="pi pi-spin pi-spinner" aria-hidden="true"></i>
            Szukanie wolnych terminów…
          </p>
        }
      </div>
      }
    </app-form-layout>
  `,
})
export class AppointmentFormComponent implements HasUnsavedChanges {
  private route = inject(ActivatedRoute);
  private http = inject(HttpClient);
  private apiBaseUrl = inject(API_BASE_URL);
  private appointmentsClient = inject(AppointmentsClient);
  private employeesClient = inject(EmployeesClient);
  private servicesClient = inject(ServicesClient);
  private serviceCategoriesClient = inject(ServiceCategoriesClient);
  private customersClient = inject(CustomersClient);
  /** Nowa wizyta: klient z listy, numer lub gość (bez danych). */
  protected customerEntryMode = signal<'list' | 'phone' | 'guest'>('list');

  protected setCustomerEntryMode(mode: 'list' | 'phone' | 'guest'): void {
    this.customerEntryMode.set(mode);
    if (mode === 'list') {
      this.appointmentModel.update((u) => ({ ...u, customerPhone: '' }));
    } else if (mode === 'phone') {
      this.appointmentModel.update((u) => ({ ...u, customerId: '' }));
    } else {
      this.appointmentModel.update((u) => ({ ...u, customerId: '', customerPhone: '' }));
    }
  }

  /** Trasa `appointment/:appointmentId/edit`. */
  private editAppointmentId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('appointmentId') ?? '')),
    { initialValue: '' }
  );

  readonly isEditMode = computed(() => !!this.editAppointmentId());
  readonly formTitle = computed(() => (this.editAppointmentId() ? 'termin wizyty' : 'Nowa wizyta'));

  readonly todayYyyyMmDd = todayYyyyMmDd();

  appointmentToEdit = rxResource({
    params: () => this.editAppointmentId(),
    stream: ({ params: id }) => {
      if (!id) {
        return of(null);
      }
      return this.http
        .get<unknown>(`${this.apiBaseUrl}/api/Appointments/${encodeURIComponent(id)}`)
        .pipe(catchError(() => of(null)));
    },
  });

  editCustomerLabel = computed(() => {
    const v = this.appointmentToEdit.value();
    if (!v) return '—';
    const r = v as Record<string, unknown>;
    if (r['isGuest'] === true || r['IsGuest'] === true) {
      return 'Gość';
    }
    const fn = r['customerFirstName'] ?? r['CustomerFirstName'];
    const ln = r['customerLastName'] ?? r['CustomerLastName'];
    return [fn, ln].filter(Boolean).join(' ') || '—';
  });

  appointmentModel = signal<AppointmentFormModel>({
    employeeId: '',
    serviceId: '',
    customerId: '',
    customerPhone: '',
    date: todayYyyyMmDd(),
    startTime: '',
  });

  private formInit = false;
  private prevEmployeeId = '';
  private prevServiceDateKey = '';

  employees = rxResource({
    stream: () => this.employeesClient.getEmployees(),
  });

  customers = rxResource({
    stream: () => this.customersClient.getCustomers(),
  });

  employeeServiceOptions = rxResource({
    /** Bez `params` rxResource ładuje się raz i nie reaguje na zmianę pracownika. */
    params: () => this.appointmentModel().employeeId,
    stream: ({ params: employeeId }) => {
      if (!employeeId) {
        return of<ServiceOption[]>([]);
      }
      return forkJoin({
        assigned: this.employeesClient.getEmployeeServices(employeeId),
        all: this.servicesClient.getServices(null),
      }).pipe(
        map(({ assigned, all }) => {
          // Iterujemy KATALOG (posortowany po OrderIndex, Name), nie listę przypisań —
          // `getEmployeeServices` nie ma OrderBy, więc dawałaby przypadkową kolejność.
          const assignedIds = new Set((assigned ?? []).map((x) => x.serviceId).filter(Boolean));
          return (all ?? [])
            .filter((svc) => svc.id && assignedIds.has(svc.id))
            .map((svc) => {
              return {
                label: svc.name ?? 'Usługa',
                value: svc.id!,
                categoryId: svc.categoryId ?? null,
              } satisfies ServiceOption;
            });
        }),
        catchError(() => of<ServiceOption[]>([]))
      );
    },
  });

  /** Kategorie katalogu — niezależne od pracownika, więc ładowane raz. Błąd → lista płaska. */
  serviceCategories = rxResource({
    defaultValue: [] as ServiceCategoryDto[],
    stream: () =>
      this.serviceCategoriesClient
        .getServiceCategories()
        .pipe(catchError(() => of([] as ServiceCategoryDto[]))),
  });

  /**
   * Usługi w sekcjach kategorii dla `p-select`. `null` = podziału nie ma (jedna sekcja),
   * wtedy pole renderuje płaską listę z `employeeServiceOptions`.
   */
  serviceOptionGroups = computed<SelectOptionGroup[] | null>(() => {
    const groups = groupServicesByCategory<ServiceOption>(
      this.employeeServiceOptions.value() ?? [],
      this.serviceCategories.value() ?? [],
    );
    if (!shouldShowCategoryHeaders(groups)) return null;
    return groups.map((g) => ({
      label: g.label,
      items: g.options.map(({ label, value }) => ({ label, value })),
    }));
  });

  /**
   * Wygenerowany NSwag wysyła `date` jako pełne ISO (…T00:00:00.000Z), co często daje 400 przy wiązaniu DateOnly.
   * Tutaj jawny `yyyy-MM-dd` w query — bez edycji api-client.ts.
   */
  slotList = rxResource({
    params: () => slotsRequestKey(this.appointmentModel()),
    defaultValue: [] as AppointmentSlotDto[],
    stream: ({ params: key }) => {
      const [employeeId, serviceId, dateStr] = key.split('\x1e');
      if (!employeeId || !serviceId || !dateStr) {
        return of<AppointmentSlotDto[]>([]);
      }
      return this.http
        .get<AppointmentSlotDto[]>(`${this.apiBaseUrl}/api/Appointments/available-slots`, {
          params: {
            date: dateStr,
            employeeId,
            serviceIds: [serviceId],
          },
        })
        .pipe(catchError(() => of<AppointmentSlotDto[]>([])));
    },
  });

  employeeOptions = computed(() => {
    const list = this.employees.value() ?? [];
    return list.map((e) => ({
      label: [e.firstName, e.lastName].filter(Boolean).join(' ') || 'Pracownik',
      value: e.id!,
    }));
  });

  customerOptions = computed(() => {
    const list = this.customers.value() ?? [];
    return list.map((c) => ({
      label: [c.firstName, c.lastName].filter(Boolean).join(' ') || c.phoneNumber || 'Klient',
      value: c.id!,
    }));
  });

  slotOptions = computed(() => {
    if (this.slotList.error()) {
      return [] as { label: string; value: string }[];
    }
    const slots = this.slotList.value() ?? [];
    const opts = slots
      .map((s) => {
        const raw =
          (s as { slot?: string; Slot?: string }).slot ??
          (s as { slot?: string; Slot?: string }).Slot;
        if (raw == null || String(raw).trim() === '') {
          return null;
        }
        const slot = String(raw).trim();
        return {
          label: slot,
          value: normalizeStartTimeForApi(slot),
        };
      })
      .filter((x): x is { label: string; value: string } => x !== null);
    const cur = this.appointmentModel().startTime?.trim();
    if (!cur) return opts;
    const norm = normalizeStartTimeForApi(cur);
    if (opts.some((o) => o.value === norm)) return opts;
    // Fallback (dolepienie aktualnej wartości startTime jako opcji) ma sens tylko w trybie EDYCJI —
    // istniejąca wizyta zajmuje swój slot i przez to nie ma jej na liście available-slots.
    // W trybie CREATE nie dopuszczamy „lewych" godzin (z URL lub po zmianie daty) — user musi
    // wybrać z faktycznej listy backendu, która już odsiewa przeszłość.
    if (!this.isEditMode()) return opts;
    return [{ label: norm, value: norm }, ...opts];
  });

  appointmentForm = form(this.appointmentModel, (schemaPath) => {
    required(schemaPath.employeeId, { message: 'Wybierz pracownika' });
    required(schemaPath.serviceId, { message: 'Wybierz usługę' });
    required(schemaPath.date, { message: 'Wybierz datę' });
    required(schemaPath.startTime, { message: 'Wybierz godzinę' });
  });

  protected FormActions = createFormActions<AppointmentFormModel, string>(
    {
      create: (data) => {
        /**
         * NSwag ma `date?: Date` — `JSON.stringify(new Date(...))` daje ISO z `T00:00:00.000Z`,
         * co często psuje wiązanie `DateOnly` i kończy się 400. Wysyłamy `yyyy-MM-dd` jak w query slotów.
         */
        const mode = this.customerEntryMode();
        if (mode === 'guest') {
          const cmd: CreateAppointmentCommand = {
            employeeId: data.employeeId,
            serviceIds: [data.serviceId],
            date: data.date as unknown as Date,
            startTime: normalizeStartTimeForApi(data.startTime),
            createAsBooked: true,
          };
          return this.appointmentsClient.createAppointment(cmd);
        }
        if (mode === 'list') {
          if (!data.customerId?.trim()) {
            return throwError(() => new Error('Wybierz klienta z listy.'));
          }
          const cmd: CreateAppointmentCommand = {
            employeeId: data.employeeId,
            serviceIds: [data.serviceId],
            customerId: data.customerId,
            date: data.date as unknown as Date,
            startTime: normalizeStartTimeForApi(data.startTime),
            createAsBooked: true,
          };
          return this.appointmentsClient.createAppointment(cmd);
        }
        const raw = data.customerPhone?.trim() ?? '';
        const digits = raw.replace(/\D/g, '');
        if (digits.length < 9) {
          return throwError(
            () => new Error('Podaj co najmniej 9 cyfr numeru telefonu (np. numer krajowy).')
          );
        }
        const cmd: CreateAppointmentCommand = {
          employeeId: data.employeeId,
          serviceIds: [data.serviceId],
          customerPhone: raw,
          date: data.date as unknown as Date,
          startTime: normalizeStartTimeForApi(data.startTime),
          createAsBooked: true,
        };
        return this.appointmentsClient.createAppointment(cmd);
      },
      update: (id, data) =>
        this.http
          .patch(`${this.apiBaseUrl}/api/Appointments/${encodeURIComponent(id)}/reschedule`, {
            employeeId: data.employeeId,
            serviceIds: [data.serviceId],
            date: data.date,
            startTime: normalizeStartTimeForApi(data.startTime),
          })
          .pipe(map(() => id)),
      delete: () => EMPTY,
      get: () => EMPTY,
    },
    this.appointmentForm as any,
    this.appointmentModel,
    () => this.editAppointmentId() || undefined,
    {
      successMessage: 'Zapisano.',
      redirectUrl: () => {
        // Po zapisie (utworzenie i edycja) wracamy na kalendarz pracownika — osobna strona
        // szczegółów wizyty została usunięta (deep-linki otwierają drawer w kalendarzu).
        const eid = this.appointmentModel().employeeId;
        return eid ? ['/admin', 'schedule', eid] : ['/admin', 'schedule'];
      },
    }
  );

  constructor() {
    const editId = this.route.snapshot.paramMap.get('appointmentId');
    if (!editId) {
      const q = this.route.snapshot.queryParamMap;
      const eid = q.get('employeeId') ?? '';
      const date = q.get('date') ?? todayYyyyMmDd();
      // `time` przychodzi z click-to-create na osi czasu (np. `08:30`).
      const time = q.get('time') ?? '';
      const isValidTime = /^\d{2}:\d{2}$/.test(time);
      // Gdy "Wypełnij gap" linkuje do minionej godziny w dniu dzisiejszym — nie prefillujemy,
      // żeby user musiał wybrać poprawny slot z listy zamiast dostać 400 po submit.
      const startTime = isValidTime && !isPastTimeToday(date, time) ? time : '';
      this.appointmentModel.set({
        employeeId: eid,
        serviceId: '',
        customerId: '',
        customerPhone: '',
        date,
        startTime,
      });
      this.prevEmployeeId = eid;
      this.prevServiceDateKey = `|${date}`;
    }

    effect(() => {
      const id = this.editAppointmentId();
      if (!id) return;
      if (!this.appointmentToEdit.hasValue()) return;
      const row = this.appointmentToEdit.value();
      if (!row) return;
      untracked(() => {
        const m = appointmentDtoToModel(row);
        this.prevEmployeeId = m.employeeId;
        this.prevServiceDateKey = `${m.serviceId}|${m.date}`;
        this.appointmentModel.set(m);
        this.formInit = true;
      });
    });

    effect(() => {
      const m = this.appointmentModel();
      if (!this.formInit) {
        this.formInit = true;
        return;
      }
      if (m.employeeId !== this.prevEmployeeId) {
        this.prevEmployeeId = m.employeeId;
        this.prevServiceDateKey = `|${m.date}`;
        untracked(() =>
          this.appointmentModel.update((u) => ({ ...u, serviceId: '', startTime: '' }))
        );
        return;
      }
      const key = `${m.serviceId}|${m.date}`;
      if (key !== this.prevServiceDateKey) {
        this.prevServiceDateKey = key;
        untracked(() => this.appointmentModel.update((u) => ({ ...u, startTime: '' })));
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return this.FormActions.hasUnsavedChanges();
  }
}
