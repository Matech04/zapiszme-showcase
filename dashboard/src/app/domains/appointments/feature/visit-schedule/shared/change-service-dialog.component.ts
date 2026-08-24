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
import { MessageService } from 'primeng/api';
import {
  AppointmentPreviewDto,
  AppointmentsClient,
  EmployeesClient,
  ServiceCategoriesClient,
  ServiceCategoryDto,
  ServicesClient,
} from '@core/api/api-client';
import {
  groupServicesByCategory,
  shouldShowCategoryHeaders,
} from '../../../data-access/service-category-groups.util';
import { AdminDrawerComponent } from './admin-drawer.component';
import { comboGroupKey, toggleServiceSelection } from './combo-select.util';

/** Opcja usługi w dialogu. `group` = grupa wariantów, `categoryId` = podział katalogowy. */
interface ServiceOption {
  label: string;
  value: string;
  group: string;
  categoryId: string | null;
}

/**
 * Dialog „Zmień usługę" — dedykowana, szybka podmiana zabiegu w obrębie ZAREZERWOWANEGO terminu.
 *
 * W odróżnieniu od `reschedule-appointment-dialog` NIE rusza pracownika/daty/godziny — zmienia
 * jedynie skład usług (combo: max jedna z danej grupy wariantów). Backend
 * (`PATCH /api/Appointments/{id}/services`) przelicza czas trwania i cenę i sprawdza, czy nowy
 * (dłuższy) zabieg mieści się w slocie. Gdy nie — zwraca 409, a tu pokazujemy podpowiedź, by
 * użyć „Zmień termin" i dobrać pasującą godzinę.
 *
 * Pełen `AppointmentDto` dociągamy po `id` (preview nie ma `serviceId`) — do preselectu
 * bieżącego składu usług.
 */
@Component({
  selector: 'app-change-service-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, AdminDrawerComponent],
  template: `
    <app-admin-drawer
      [isOpen]="isVisible()"
      [isDesktop]="isDesktop()"
      title="Zmień usługę"
      label="Wizyta"
      [closeable]="!isSubmitting()"
      (closeRequested)="onCancel()"
    >
      <div drawer-body>
        @if (appointment(); as a) {
          <div class="flex flex-col gap-5">
            <div
              class="rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-surface-50/70 dark:bg-surface-50/40 px-4 py-3"
            >
              <p class="admin-section-label text-primary">Wizyta (termin bez zmian)</p>
              <p class="text-sm font-bold text-surface-900 mt-0.5">
                {{ serviceLine(a) }}
              </p>
              <p class="text-xs text-surface-600 dark:text-surface-300 mt-0.5 font-mono">
                {{ currentDateLabel() }} • {{ shortTime(a.startTime) }} – {{ shortTime(a.endTime) }}
              </p>
            </div>

            <!-- Usługi (combo — można edytować skład; max jedna z danej grupy wariantów) -->
            <div class="flex flex-col gap-1.5">
              <label class="admin-section-label">Usługi</label>
              @if (servicesResource.isLoading() || appointmentDetail.isLoading()) {
                <div class="h-10 bg-surface-100 dark:bg-surface-100 animate-pulse rounded-xl"></div>
              } @else if (serviceOptions().length === 0) {
                <p class="text-xs text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-950/40 rounded-xl px-3 py-2 border border-amber-200/80 dark:border-amber-800/60">
                  Pracownik nie ma przypisanych usług.
                </p>
              } @else {
                <div class="flex flex-col gap-3" data-testid="change-service-chips">
                  @for (group of serviceGroups(); track group.key) {
                    <div class="flex flex-col gap-1.5">
                      @if (showCategoryHeaders()) {
                        <!-- Kreska odróżnia nagłówek sekcji od etykiety pola (obie są UPPERCASE). -->
                        <div class="flex items-center gap-2">
                          <span
                            class="text-[0.65rem] font-bold uppercase tracking-[0.12em] text-surface-500 dark:text-surface-400"
                            data-testid="change-service-category"
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
                @if (serviceIds().length > 1) {
                  <p class="text-xs text-surface-500 dark:text-surface-400">
                    {{ serviceIds().length }} usługi — kalendarz blokuje sumę czasów.
                  </p>
                }
              }
            </div>

            @if (submitError(); as err) {
              <p
                class="rounded-xl bg-red-50 dark:bg-red-950/30 border border-red-200/70 dark:border-red-800/50 px-3 py-2 text-xs text-red-700 dark:text-red-300"
              >
                {{ err }}
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
          Zapisz usługę
        </button>
      </div>
    </app-admin-drawer>
  `,
})
export class ChangeServiceDialogComponent {
  /** Wizyta do zmiany usługi. `null` → drawer zamknięty. */
  readonly appointment = input<AppointmentPreviewDto | null>(null);
  readonly isDesktop = input<boolean>(false);

  readonly closeRequested = output<void>();
  readonly success = output<string>();

  private readonly appointmentsClient = inject(AppointmentsClient);
  private readonly employeesClient = inject(EmployeesClient);
  private readonly servicesClient = inject(ServicesClient);
  private readonly serviceCategoriesClient = inject(ServiceCategoriesClient);
  private readonly messages = inject(MessageService);

  protected readonly isVisible = computed(() => this.appointment() != null);

  /** Wybrane usługi combo (default = istniejący skład wizyty). */
  protected readonly serviceIds = signal<string[]>([]);
  protected readonly MAX_COMBO_SERVICES = 5;
  protected readonly isSubmitting = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected readonly servicesResource = rxResource({
    params: () => this.appointment()?.employeeId ?? undefined,
    stream: ({ params: empId }) => {
      if (!empId) return of<ServiceOption[]>([]);
      return forkJoin({
        assigned: this.employeesClient.getEmployeeServices(empId),
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
                group: comboGroupKey(svc.comboGroup),
                categoryId: svc.categoryId ?? null,
              } satisfies ServiceOption;
            });
        }),
        catchError(() => of<ServiceOption[]>([])),
      );
    },
  });

  /** Kategorie katalogu — niezależne od wizyty, więc ładowane raz. Błąd → lista płaska. */
  protected readonly categoriesResource = rxResource({
    defaultValue: [] as ServiceCategoryDto[],
    stream: () =>
      this.serviceCategoriesClient
        .getServiceCategories()
        .pipe(catchError(() => of([] as ServiceCategoryDto[]))),
  });

  /** Pełen `AppointmentDto` — preview nie ma `serviceId`/`services`. Potrzebny do preselectu. */
  protected readonly appointmentDetail = rxResource({
    params: () => this.appointment()?.id ?? undefined,
    stream: ({ params: id }) => {
      if (!id) return of(undefined);
      return this.appointmentsClient.getAppointmentById(id).pipe(catchError(() => of(undefined)));
    },
  });

  protected readonly serviceOptions = computed(() => this.servicesResource.value() ?? []);

  /** Usługi w sekcjach kategorii — układ listy wyboru. */
  protected readonly serviceGroups = computed(() =>
    groupServicesByCategory(this.serviceOptions(), this.categoriesResource.value() ?? []),
  );

  protected readonly showCategoryHeaders = computed(() =>
    shouldShowCategoryHeaders(this.serviceGroups()),
  );

  protected readonly canSubmit = computed(
    () => !!this.appointment()?.id && this.serviceIds().length > 0,
  );

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
  /** Przełącza usługę w combo (reguła grup wariantów). */
  protected toggleService(id: string): void {
    const opts = this.serviceOptions();
    const groupOf = (x: string) => opts.find((o) => o.value === x)?.group ?? '';
    this.serviceIds.set(toggleServiceSelection(this.serviceIds(), id, groupOf, this.MAX_COMBO_SERVICES));
    this.submitError.set(null);
  }

  constructor() {
    // Reset stanu na każde otwarcie/zmianę wizyty.
    effect(() => {
      const a = this.appointment();
      untracked(() => {
        this.serviceIds.set([]);
        this.isSubmitting.set(false);
        this.submitError.set(null);
        void a;
      });
    });

    // Preselect bieżącego składu usług, gdy dojadą opcje pracownika i pełne DTO wizyty.
    effect(() => {
      const options = this.serviceOptions();
      const detail = this.appointmentDetail.value();
      untracked(() => {
        if (this.serviceIds().length > 0 || options.length === 0) return;
        const optionIds = new Set(options.map((o) => o.value));
        const existing = (detail?.services ?? [])
          .map((s) => s.serviceId!)
          .filter((id) => optionIds.has(id));
        if (existing.length > 0) {
          this.serviceIds.set(existing);
          return;
        }
        if (detail?.serviceId && optionIds.has(detail.serviceId)) {
          this.serviceIds.set([detail.serviceId]);
        }
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

  protected onSubmit(): void {
    const a = this.appointment();
    const serviceIds = this.serviceIds();
    if (!a?.id || serviceIds.length === 0) return;
    this.isSubmitting.set(true);
    this.submitError.set(null);
    this.appointmentsClient.changeAppointmentServices(a.id, { serviceIds }).subscribe({
      next: (id) => {
        this.isSubmitting.set(false);
        this.messages.add({
          severity: 'success',
          summary: 'Usługa zmieniona',
          detail: 'Czas i cena wizyty zostały przeliczone.',
          life: 3000,
        });
        this.success.emit(id ?? a.id!);
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        const status = (err as { status?: number })?.status;
        if (status === 409) {
          this.submitError.set(
            'Nowa usługa nie mieści się w tym terminie. Użyj „Zmień termin", aby wybrać pasującą godzinę.',
          );
        } else if (status === 400) {
          this.submitError.set('Wybrany skład usług jest nieprawidłowy.');
        } else if (status === 403) {
          this.submitError.set('Nie masz uprawnień do zmiany tej wizyty.');
        } else {
          this.submitError.set('Nie udało się zmienić usługi. Spróbuj ponownie.');
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
}
