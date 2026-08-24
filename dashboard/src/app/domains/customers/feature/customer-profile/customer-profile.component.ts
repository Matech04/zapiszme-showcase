import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { rxResource } from '@angular/core/rxjs-interop';
import { lastValueFrom, of } from 'rxjs';
import {
  AppointmentPreviewDto,
  AppointmentsClient,
  CustomerDataExportDto,
  CustomerDto,
  CustomersClient,
} from '@core/api/api-client';
import { CommonModule } from '@angular/common';
import { ConfirmationService, MessageService } from 'primeng/api';
import { AuthSessionService } from '@core/auth/auth-session.service';
import {
  AppointmentStatusVariant,
  statusTokens,
  statusVariantFromIdOrName,
} from '@core/theme/status-tokens';

interface CustomerKpi {
  label: string;
  value: string;
  hint: string;
  icon: string;
}

interface CustomerTag {
  label: string;
  classes: string;
  icon: string;
}

@Component({
  selector: 'app-customer-profile',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="admin-page-shell admin-page-pad-for-bottom-nav">
      <div class="admin-page-container max-w-[1100px]">
        <!-- HEADER / HERO -->
        @if (customer.isLoading()) {
          <div class="admin-glass-card admin-page-hero animate-pulse h-44"></div>
        } @else if (customer.error()) {
          <div class="admin-card text-red-600 dark:text-red-400 text-sm">
            Nie udało się wczytać danych klienta.
          </div>
        } @else if (customer.value(); as c) {
          <header class="admin-glass-card admin-page-hero">
            <div class="flex flex-col sm:flex-row sm:items-start gap-5">
              <a
                [routerLink]="['/admin', 'customers']"
                class="w-11 h-11 shrink-0 rounded-2xl border border-surface-200 dark:border-surface-200 bg-white/85 dark:bg-surface-50 flex items-center justify-center text-surface-600 dark:text-surface-300"
                aria-label="Wróć do listy klientów"
              >
                <i class="pi pi-chevron-left text-lg" aria-hidden="true"></i>
              </a>

              <div
                class="grid size-16 sm:size-20 place-items-center rounded-2xl bg-slate-900 text-white dark:bg-white dark:text-slate-900 text-xl font-black shadow-lg shrink-0"
              >
                {{ initials(c) }}
              </div>

              <div class="flex-1 min-w-0">
                <span class="admin-section-label text-primary mb-2 block">
                  Profil klienta
                </span>
                <h1 class="admin-page-title text-surface-900 mb-2">
                  {{ c.firstName }} {{ c.lastName }}
                </h1>
                <div class="flex flex-wrap gap-2">
                  @for (t of tags(); track t.label) {
                    <span class="admin-chip" [ngClass]="t.classes">
                      <i [class]="t.icon" class="text-[10px]"></i>
                      {{ t.label }}
                    </span>
                  }
                </div>
              </div>

              <div class="flex flex-wrap gap-2 sm:flex-nowrap shrink-0">
                <a
                  routerLink="/admin/schedule"
                  [queryParams]="{ customerId: c.id }"
                  class="inline-flex items-center gap-2 rounded-full bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 px-4 py-2.5 font-bold text-xs uppercase tracking-wider"
                >
                  <i class="pi pi-calendar-plus"></i>
                  Umów wizytę
                </a>
                @if (c.phoneNumber) {
                  <a
                    [href]="'tel:' + c.phoneNumber"
                    class="inline-flex items-center gap-2 rounded-full border border-surface-300 dark:border-surface-600 px-4 py-2.5 font-bold text-xs uppercase tracking-wider hover:border-primary/45 transition-colors text-surface-800"
                  >
                    <i class="pi pi-phone"></i>
                    Zadzwoń
                  </a>
                  <a
                    [href]="'sms:' + c.phoneNumber"
                    class="inline-flex items-center gap-2 rounded-full border border-surface-300 dark:border-surface-600 px-4 py-2.5 font-bold text-xs uppercase tracking-wider hover:border-primary/45 transition-colors text-surface-800"
                  >
                    <i class="pi pi-comment"></i>
                    SMS
                  </a>
                }
                @if (c.id) {
                  <a
                    [routerLink]="['/admin', 'customers', 'edit', c.id]"
                    class="inline-flex items-center gap-2 rounded-full border border-surface-300 dark:border-surface-600 px-4 py-2.5 font-bold text-xs uppercase tracking-wider hover:border-primary/45 transition-colors text-surface-800"
                  >
                    <i class="pi pi-pencil"></i>
                    Edytuj
                  </a>
                }
                @if (c.id && canManageWhitelist()) {
                  <button
                    type="button"
                    [disabled]="whitelistBusy()"
                    (click)="toggleWhitelist(c)"
                    class="inline-flex items-center gap-2 rounded-full border px-4 py-2.5 font-bold text-xs uppercase tracking-wider transition-colors disabled:opacity-50"
                    [class.bg-emerald-50]="c.isWhitelisted"
                    [class.dark:bg-emerald-950/35]="c.isWhitelisted"
                    [class.border-emerald-300/60]="c.isWhitelisted"
                    [class.text-emerald-700]="c.isWhitelisted"
                    [class.dark:text-emerald-300]="c.isWhitelisted"
                    [class.border-surface-300]="!c.isWhitelisted"
                    [class.dark:border-surface-600]="!c.isWhitelisted"
                    [class.text-surface-800]="!c.isWhitelisted"
                    [class.dark:text-surface-900]="!c.isWhitelisted"
                    [attr.aria-pressed]="c.isWhitelisted ? 'true' : 'false'"
                  >
                    <i
                      class="pi"
                      [class.pi-check-circle]="c.isWhitelisted"
                      [class.pi-user-plus]="!c.isWhitelisted"
                    ></i>
                    {{ c.isWhitelisted ? 'Na whitelist' : 'Dodaj do whitelisty' }}
                  </button>
                }
              </div>
            </div>
          </header>

          <!-- KPI -->
          <section class="grid grid-cols-2 lg:grid-cols-4 gap-3">
            @for (k of kpis(); track k.label) {
              <div class="admin-kpi-card">
                <div class="flex items-center justify-between">
                  <span class="admin-kpi-label">{{ k.label }}</span>
                  <i [class]="k.icon" class="text-amber-600 dark:text-amber-400"></i>
                </div>
                <span class="admin-kpi-value text-surface-900">{{ k.value }}</span>
                <span class="text-xs text-surface-500 dark:text-surface-400">{{ k.hint }}</span>
              </div>
            }
          </section>

          <!-- 2-column body -->
          <div class="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <aside class="admin-card lg:col-span-1 space-y-5">
              <h2 class="admin-h3 text-surface-900">Dane kontaktowe</h2>
              <div class="space-y-4">
                <div>
                  <p class="admin-section-label text-surface-500 mb-1">Email</p>
                  <p class="text-surface-900 break-all">{{ c.email || '—' }}</p>
                </div>
                <div>
                  <p class="admin-section-label text-surface-500 mb-1">Telefon</p>
                  <p class="text-surface-900">{{ c.phoneNumber || '—' }}</p>
                </div>
                <div>
                  <p class="admin-section-label text-surface-500 mb-1">Instagram</p>
                  @if (instagramNick(); as ig) {
                    <a
                      [href]="'https://instagram.com/' + ig"
                      target="_blank"
                      rel="noopener"
                      class="inline-flex items-center gap-2 font-bold text-primary hover:underline"
                    >
                      <i class="pi pi-instagram text-sm" aria-hidden="true"></i>
                      {{ '@' + ig }}
                    </a>
                  } @else {
                    <p class="text-surface-900">—</p>
                  }
                </div>
                <div>
                  <p class="admin-section-label text-surface-500 mb-1">Klient od</p>
                  <p class="text-surface-900">{{ formatDate(c.createdAt) }}</p>
                </div>
              </div>

              <div class="admin-divider"></div>

              <h3 class="admin-h3 text-surface-900">Preferencje</h3>
              <div class="space-y-3">
                <div>
                  <p class="admin-section-label text-surface-500 mb-1">Ulubiona usługa</p>
                  <p class="text-surface-900">{{ favoriteService() || '—' }}</p>
                </div>
                <div>
                  <p class="admin-section-label text-surface-500 mb-1">Ulubiony pracownik</p>
                  <p class="text-surface-900">{{ favoriteEmployee() || '—' }}</p>
                </div>
              </div>

              @if (c.generalNotes?.trim()) {
                <div class="admin-divider"></div>
                <div>
                  <p class="admin-section-label text-surface-500 mb-2">Notatki</p>
                  <p class="text-sm text-surface-700 dark:text-surface-300 whitespace-pre-wrap">
                    {{ c.generalNotes }}
                  </p>
                </div>
              }
            </aside>

            <section class="lg:col-span-2 admin-card">
              <div class="flex items-center justify-between gap-4 mb-4">
                <div>
                  <p class="admin-section-label text-primary">Historia</p>
                  <h2 class="admin-h3 text-surface-900">Wizyty</h2>
                </div>
                @if (!appointments.isLoading() && !appointments.error()) {
                  <span class="text-xs font-bold text-surface-500 uppercase tracking-wider">
                    {{ visitCountLabel(orderedAppointments()) }}
                  </span>
                }
              </div>

              @if (appointments.isLoading()) {
                <div class="grid gap-2">
                  @for (s of [0,1,2,3]; track s) {
                    <div class="h-16 rounded-2xl bg-surface-100 dark:bg-surface-100 animate-pulse"></div>
                  }
                </div>
              } @else if (appointments.error()) {
                <p class="text-sm text-red-600 dark:text-red-400 py-8 text-center">
                  Nie udało się wczytać historii wizyt.
                </p>
              } @else if (orderedAppointments().length === 0) {
                <div class="rounded-2xl border border-dashed border-surface-200 dark:border-surface-200 p-8 text-center">
                  <i class="pi pi-calendar text-2xl text-surface-400 mb-2 block"></i>
                  <p class="text-surface-600 dark:text-surface-400 text-sm">
                    Brak zarejestrowanych wizyt dla tego klienta.
                  </p>
                </div>
              } @else {
                <ul class="grid gap-2">
                  @for (apt of orderedAppointments(); track trackApt($index, apt)) {
                    <li>
                      @if (apt.id) {
                        <a
                          [routerLink]="['/admin', 'schedule']"
                          [queryParams]="{ appointment: apt.id }"
                          class="block rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 p-4 hover:border-primary/45 transition-colors"
                        >
                          <ng-container *ngTemplateOutlet="row; context: { $implicit: apt }" />
                        </a>
                      } @else {
                        <div class="rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 p-4">
                          <ng-container *ngTemplateOutlet="row; context: { $implicit: apt }" />
                        </div>
                      }
                    </li>
                  }
                </ul>
              }
            </section>
          </div>

          <!-- RODO — realizacja żądań klienta (owner-only, polityka BusinessManagement) -->
          @if (c.id && canManageRodo()) {
            <section class="admin-card space-y-4">
              <div>
                <span class="admin-section-label text-primary mb-1 block">RODO</span>
                <h2 class="admin-h3 text-surface-900">Dane osobowe</h2>
                <p class="text-sm text-surface-600 dark:text-surface-400 mt-1">
                  Realizacja żądań klienta: dostęp i przenoszalność danych (art. 15 i 20)
                  oraz usunięcie danych (art. 17).
                </p>
              </div>

              <div class="flex flex-col sm:flex-row gap-2">
                <button
                  type="button"
                  [disabled]="exportBusy()"
                  (click)="exportData(c)"
                  class="inline-flex items-center justify-center gap-2 rounded-full border border-surface-300 dark:border-surface-600 px-4 py-2.5 font-bold text-xs uppercase tracking-wider hover:border-primary/45 transition-colors text-surface-800 disabled:opacity-50"
                >
                  <i class="pi" [class.pi-download]="!exportBusy()" [class.pi-spinner]="exportBusy()" [class.pi-spin]="exportBusy()"></i>
                  Eksportuj dane (JSON)
                </button>

                <button
                  type="button"
                  [disabled]="deleteBusy()"
                  (click)="confirmDelete(c)"
                  class="inline-flex items-center justify-center gap-2 rounded-full border border-red-300/60 dark:border-red-800/60 bg-red-50/80 dark:bg-red-950/35 text-red-700 dark:text-red-300 px-4 py-2.5 font-bold text-xs uppercase tracking-wider hover:border-red-400 transition-colors disabled:opacity-50"
                >
                  <i class="pi" [class.pi-trash]="!deleteBusy()" [class.pi-spinner]="deleteBusy()" [class.pi-spin]="deleteBusy()"></i>
                  Usuń klienta
                </button>
              </div>

              <p class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed">
                <strong>Usunięcie jest nieodwracalne</strong> — kasuje imię, nazwisko, e-mail,
                telefon, Instagram i notatki, także te zapisane przy wizytach. Historia wizyt (terminy,
                usługi, kwoty) zostaje jako dane księgowe. Przy wizytach klient pokaże się wtedy
                jako „Klient usunięty”.
              </p>
            </section>
          }
        }
      </div>
    </div>

    <ng-template #row let-apt>
      <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
        <div class="min-w-0">
          <p class="font-bold text-surface-900 truncate">
            {{ apt.serviceName || 'Usługa' }}
          </p>
          <p class="text-sm text-surface-600 dark:text-surface-400 mt-0.5 truncate">
            {{ apt.employeeFirstName }} {{ apt.employeeLastName }}
          </p>
        </div>
        <div class="text-left sm:text-right shrink-0 flex sm:block items-center gap-3">
          <p class="text-sm font-medium text-surface-900">
            {{ formatDate(apt.date) }}
          </p>
          <p class="text-xs text-surface-500 font-mono mt-0.5">
            {{ apt.startTime ?? '—' }} – {{ apt.endTime ?? '—' }}
          </p>
          <span
            class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-full border border-current/30 mt-2 inline-block"
            [class]="statusClass(apt)"
          >
            {{ statusLabel(apt) }}
          </span>
        </div>
      </div>
    </ng-template>
  `,
})
export class CustomerProfileComponent {
  private customersClient = inject(CustomersClient);
  private appointmentsClient = inject(AppointmentsClient);
  private messages = inject(MessageService);
  private readonly auth = inject(AuthSessionService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly router = inject(Router);

  /** Whitelist to polityka API `StaffManagement` → właścicielka i manager. */
  protected readonly canManageWhitelist = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager' || role === 'systemAdmin';
  });

  /**
   * Eksport i anonimizacja to polityka API `BusinessManagement` → Owner + Admin (support).
   * Manager i pracownik ich NIE dostają: zrzut pełnego PII i nieodwracalne usunięcie danych
   * nie mogą wisieć na współdzielonym terminalu „Recepcja” (Kiosk).
   */
  protected readonly canManageRodo = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'systemAdmin';
  });

  /** Parametr trasy `customers/:id` (withComponentInputBinding). */
  id = input.required<string>();
  readonly whitelistBusy = signal<boolean>(false);
  readonly exportBusy = signal<boolean>(false);
  readonly deleteBusy = signal<boolean>(false);

  customer = rxResource({
    stream: () => {
      const cid = this.id();
      if (!cid) return of(undefined as CustomerDto | undefined);
      return this.customersClient.getCustomer(cid);
    },
  });

  /** Nick IG klienta (bez `@`), null gdy niepodany. */
  readonly instagramNick = computed(() => {
    const ig = this.customer.value()?.instagramNick?.trim();
    return ig ? ig : null;
  });

  appointments = rxResource({
    stream: () => {
      const cid = this.id();
      if (!cid) return of([] as AppointmentPreviewDto[]);
      return this.appointmentsClient.getCustomerAppointments(cid);
    },
    defaultValue: [] as AppointmentPreviewDto[],
  });

  /** Statusy techniczne związane z trzymaniem slotu / OTP — nie mają znaczenia dla operatora w historii. */
  private readonly visibleAppointments = computed(() => {
    return this.appointments.value().filter((a) => {
      const v = statusVariantFromIdOrName(a.status?.id, a.status?.name);
      return v !== 'pending' && v !== 'awaitingOtp';
    });
  });

  /** Sortowane od najnowszych. */
  readonly orderedAppointments = computed(() => {
    return [...this.visibleAppointments()].sort((a, b) => {
      const da = a.date ? new Date(a.date as unknown as string).getTime() : 0;
      const db = b.date ? new Date(b.date as unknown as string).getTime() : 0;
      return db - da;
    });
  });

  readonly kpis = computed<CustomerKpi[]>(() => {
    const list = this.visibleAppointments();
    const completed = list.filter(
      (a) => statusVariantFromIdOrName(a.status?.id, a.status?.name) === 'completed',
    ).length;
    const canceled = list.filter(
      (a) => statusVariantFromIdOrName(a.status?.id, a.status?.name) === 'canceled',
    ).length;
    const noShowRate = list.length > 0 ? Math.round((canceled / list.length) * 100) : 0;
    const last = this.orderedAppointments().find((x) => x.date);

    return [
      {
        label: 'Wizyty łącznie',
        value: String(list.length),
        hint: completed + ' zakończonych',
        icon: 'pi pi-history',
      },
      {
        label: 'Anulowane',
        value: String(canceled),
        hint: noShowRate + '% wszystkich',
        icon: 'pi pi-times-circle',
      },
      {
        label: 'Ostatnia wizyta',
        value: last?.date ? this.formatDate(last.date) : '—',
        hint: last?.serviceName ?? 'Brak danych',
        icon: 'pi pi-calendar-clock',
      },
      {
        label: 'Aktywność',
        value: this.activityLabel(),
        hint: list.length === 0 ? 'Bez wizyt' : 'Liczone z ostatniej wizyty',
        icon: 'pi pi-bolt',
      },
    ];
  });

  readonly tags = computed<CustomerTag[]>(() => {
    const out: CustomerTag[] = [];
    const c = this.customer.value();
    const list = this.visibleAppointments();
    if (!c) return out;
    const completed = list.filter(
      (a) => statusVariantFromIdOrName(a.status?.id, a.status?.name) === 'completed',
    ).length;
    const canceled = list.filter(
      (a) => statusVariantFromIdOrName(a.status?.id, a.status?.name) === 'canceled',
    ).length;
    if (completed >= 10) {
      out.push({
        label: 'VIP',
        icon: 'pi pi-star-fill',
        classes:
          '!border-amber-300/60 !bg-amber-50/80 dark:!bg-amber-950/35 text-amber-700 dark:text-amber-300',
      });
    }
    const created = c.createdAt ? new Date(c.createdAt as unknown as string) : null;
    if (created) {
      const days = (Date.now() - created.getTime()) / 86400000;
      if (days < 30) {
        out.push({
          label: 'Nowy klient',
          icon: 'pi pi-sparkles',
          classes:
            '!border-emerald-300/60 !bg-emerald-50/80 dark:!bg-emerald-950/35 text-emerald-700 dark:text-emerald-300',
        });
      }
    }
    if (list.length >= 4 && canceled / list.length > 0.3) {
      out.push({
        label: 'Częsty no-show',
        icon: 'pi pi-exclamation-triangle',
        classes:
          '!border-red-300/60 !bg-red-50/80 dark:!bg-red-950/35 text-red-700 dark:text-red-300',
      });
    }
    return out;
  });

  readonly favoriteService = computed(() => this.mostCommon(this.visibleAppointments().map((a) => a.serviceName ?? '')));

  readonly favoriteEmployee = computed(() =>
    this.mostCommon(
      this.visibleAppointments().map((a) =>
        [a.employeeFirstName, a.employeeLastName].filter((s): s is string => !!s).join(' '),
      ),
    ),
  );

  trackApt(index: number, apt: AppointmentPreviewDto): string {
    return apt.id ?? `row-${index}`;
  }

  async toggleWhitelist(c: CustomerDto): Promise<void> {
    if (!c.id || this.whitelistBusy()) return;
    const next = !c.isWhitelisted;
    this.whitelistBusy.set(true);
    try {
      await lastValueFrom(
        this.customersClient.setWhitelist(c.id, { isWhitelisted: next }),
      );
      this.messages.add({
        severity: 'success',
        summary: next ? 'Dodano do whitelisty' : 'Usunięto z whitelisty',
        detail: next
          ? 'Klient może rezerwować w trybie tylko zaproszeni.'
          : 'Klient nie będzie mógł rezerwować w trybie tylko zaproszeni.',
        life: 3500,
      });
      this.customer.reload();
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Nie udało się zaktualizować',
        detail: 'Spróbuj ponownie.',
        life: 4000,
      });
    } finally {
      this.whitelistBusy.set(false);
    }
  }

  /** RODO art. 15/20 — zrzut danych klienta do pliku JSON, do wydania na żądanie. */
  async exportData(c: CustomerDto): Promise<void> {
    if (!c.id || this.exportBusy()) return;
    this.exportBusy.set(true);
    try {
      const data = await lastValueFrom(this.customersClient.exportCustomerData(c.id));
      this.downloadJson(data, this.exportFileName(c));
      this.messages.add({
        severity: 'success',
        summary: 'Dane wyeksportowane',
        detail: 'Plik JSON został pobrany.',
        life: 3500,
      });
    } catch {
      // errorInterceptor pokaże toast.
    } finally {
      this.exportBusy.set(false);
    }
  }

  /** RODO art. 17 — „Usuń klienta” trwale kasuje PII (nie jest to soft-delete). */
  confirmDelete(c: CustomerDto): void {
    if (!c.id || this.deleteBusy()) return;
    this.confirmation.confirm({
      header: 'Usunięcie klienta',
      message:
        `Trwale usunąć klienta ${c.firstName} ${c.lastName}? ` +
        'Operacja jest nieodwracalna — imię, nazwisko, e-mail, telefon, Instagram i notatki ' +
        '(także te przy wizytach) zostaną skasowane. Historia wizyt zostaje.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Tak, usuń',
      rejectLabel: 'Anuluj',
      acceptButtonStyleClass: 'p-button-danger p-button-text',
      rejectButtonStyleClass: 'p-button-text p-button-secondary',
      accept: () => void this.deleteCustomer(c.id!),
    });
  }

  private async deleteCustomer(customerId: string): Promise<void> {
    this.deleteBusy.set(true);
    try {
      await lastValueFrom(this.customersClient.deleteCustomer(customerId));
      this.messages.add({
        severity: 'success',
        summary: 'Klient usunięty',
        detail: 'Dane osobowe klienta zostały trwale usunięte.',
        life: 3500,
      });
      // Usunięcie ustawia IsActive=false → profil znika spod globalnego query-filtra,
      // więc powrót na listę zamiast reloadu, który skończyłby się 404.
      await this.router.navigate(['/admin', 'customers']);
    } catch {
      // errorInterceptor pokaże toast.
    } finally {
      this.deleteBusy.set(false);
    }
  }

  private downloadJson(data: CustomerDataExportDto, fileName: string): void {
    const blob = new Blob([JSON.stringify(data, null, 2)], {
      type: 'application/json;charset=utf-8',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  private exportFileName(c: CustomerDto): string {
    const name = [c.firstName, c.lastName]
      .filter((s): s is string => !!s?.trim())
      .join('-')
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-zA-Z0-9-]/g, '')
      .toLowerCase();
    return `dane-osobowe-${name || c.id}.json`;
  }

  protected statusLabel(apt: AppointmentPreviewDto): string {
    return statusTokens(statusVariantFromIdOrName(apt.status?.id, apt.status?.name)).label;
  }

  protected statusClass(apt: AppointmentPreviewDto): string {
    return statusTokens(statusVariantFromIdOrName(apt.status?.id, apt.status?.name)).textClasses;
  }

  protected initials(c: CustomerDto): string {
    const f = (c.firstName ?? '').trim();
    const l = (c.lastName ?? '').trim();
    if (f && l) return (f[0] + l[0]).toUpperCase();
    if (f) return f.slice(0, 2).toUpperCase();
    if (l) return l.slice(0, 2).toUpperCase();
    return '??';
  }

  visitCountLabel(rows: AppointmentPreviewDto[]): string {
    const n = rows.length;
    if (n === 0) return '0 wizyt';
    if (n === 1) return '1 wizyta';
    const mod10 = n % 10;
    const mod100 = n % 100;
    if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14)) {
      return `${n} wizyty`;
    }
    return `${n} wizyt`;
  }

  formatDate(d: Date | string | undefined): string {
    if (d == null) return '—';
    const s = typeof d === 'string' ? d.split('T')[0] : '';
    if (s && /^\d{4}-\d{2}-\d{2}$/.test(s)) {
      const [y, m, day] = s.split('-').map(Number);
      return `${day}.${String(m).padStart(2, '0')}.${y}`;
    }
    const x = typeof d === 'string' ? new Date(d) : d;
    if (Number.isNaN((x as Date).getTime?.())) return '—';
    const dt = x as Date;
    return `${dt.getDate()}.${String(dt.getMonth() + 1).padStart(2, '0')}.${dt.getFullYear()}`;
  }

  private activityLabel(): string {
    const last = this.orderedAppointments().find((x) => x.date);
    if (!last?.date) return '—';
    const d = new Date(last.date as unknown as string);
    const days = Math.floor((Date.now() - d.getTime()) / 86400000);
    if (days <= 0) return 'Dziś';
    if (days === 1) return 'Wczoraj';
    if (days < 7) return `${days} dni temu`;
    if (days < 30) return `${Math.floor(days / 7)} tyg. temu`;
    if (days < 365) return `${Math.floor(days / 30)} mies. temu`;
    return `${Math.floor(days / 365)} lat temu`;
  }

  private mostCommon(values: string[]): string {
    const trimmed = values.filter((v) => v.trim() !== '');
    if (!trimmed.length) return '';
    const counts = new Map<string, number>();
    for (const v of trimmed) counts.set(v, (counts.get(v) ?? 0) + 1);
    let bestKey = trimmed[0];
    let bestCnt = 0;
    for (const [k, n] of counts.entries()) {
      if (n > bestCnt) {
        bestCnt = n;
        bestKey = k;
      }
    }
    return bestKey;
  }
}
