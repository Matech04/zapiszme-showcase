import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { rxResource } from '@angular/core/rxjs-interop';
import { catchError, finalize, of } from 'rxjs';
import {
  API_BASE_URL,
  EmployeeServiceDto,
  EmployeesClient,
  ServiceDto,
  ServicesClient,
} from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { Button } from 'primeng/button';
import { Select } from 'primeng/select';
import { Checkbox } from 'primeng/checkbox';
import { MessageService, ConfirmationService } from 'primeng/api';

/**
 * Surowe przypisanie usługi do pracownika. `customDuration`/`customAmount` === null oznacza
 * „dziedzicz z katalogu usługi" (override nie ustawiony) — wtedy efektywny czas/cena bierze się
 * z ServiceDto. Wartość niepusta to świadomy override per-pracownik.
 */
type NormalizedAssignment = {
  serviceId: string;
  customDuration: number | null;
  customAmount: number | null;
  customCurrency: string | null;
};

/** Wiersz listy przypisanych usług z policzonymi wartościami efektywnymi (override ?? katalog). */
type AssignedRow = {
  serviceId: string;
  duration: number;
  amount: number;
  currency: string;
  inheritsDuration: boolean;
  inheritsPrice: boolean;
  serviceName: string;
  catalogHint: string | null;
};

function normalizeMoney(raw: unknown): { amount: number; currency: string } {
  if (raw == null) return { amount: 0, currency: 'PLN' };
  const o = raw as Record<string, unknown>;
  const amount = Number(o['amount'] ?? o['Amount'] ?? 0);
  const currency = String(o['currency'] ?? o['Currency'] ?? 'PLN');
  return { amount: Number.isFinite(amount) ? amount : 0, currency: currency || 'PLN' };
}

function normalizeAssignment(d: EmployeeServiceDto): NormalizedAssignment {
  const raw = d as Record<string, unknown>;
  const serviceId = String(d.serviceId ?? raw['ServiceId'] ?? '');
  const rawDuration = d.customDuration ?? raw['CustomDuration'] ?? null;
  const parsedDuration = rawDuration == null ? null : Math.round(Number(rawDuration));
  const customDuration =
    parsedDuration != null && Number.isFinite(parsedDuration) && parsedDuration >= 0
      ? parsedDuration
      : null;
  const rawPrice = d.customPrice ?? raw['CustomPrice'] ?? null;
  let customAmount: number | null = null;
  let customCurrency: string | null = null;
  if (rawPrice != null) {
    const m = normalizeMoney(rawPrice);
    customAmount = m.amount;
    customCurrency = m.currency;
  }
  return { serviceId, customDuration, customAmount, customCurrency };
}

@Component({
  selector: 'app-employee-assigned-services',
  standalone: true,
  imports: [CommonModule, FormsModule, Select, Button, Checkbox],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      data-tour="employee-services"
      class="rounded-2xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-6 shadow-sm"
    >
      @if (showPanelTitle()) {
        <h2 class="text-sm font-bold uppercase tracking-wider text-surface-500 mb-4">Usługi pracownika</h2>
      }

      @if (!employeeId()) {
        <p class="text-sm text-surface-500 dark:text-surface-400">
          Zapisz pracownika, aby móc przypisać usługi i ustawić dla nich czas oraz cenę.
        </p>
      } @else if (catalog.isLoading() || assigned.isLoading()) {
        <div class="flex justify-center py-10">
          <i class="pi pi-spin pi-spinner text-2xl text-primary" aria-hidden="true"></i>
        </div>
      } @else {
        <div class="space-y-6">
          @if (!canManageAssignments()) {
            <p class="rounded-xl border border-amber-200 dark:border-amber-800/70 bg-amber-50 dark:bg-amber-900/20 px-4 py-3 text-sm text-amber-800 dark:text-amber-200">
              Brak uprawnień do zarządzania tym profilem usług.
            </p>
          }
          @if (assignedRows().length === 0) {
            <p class="text-sm text-surface-500 dark:text-surface-400">
              Brak przypisanych usług — dodaj pierwszą poniżej.
            </p>
          } @else {
            <ul class="space-y-3" role="list">
              @for (row of assignedRows(); track row.serviceId) {
                <li
                  class="rounded-xl border border-surface-200/90 dark:border-surface-200 p-4 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between"
                >
                  <div class="min-w-0 flex-1">
                    <p class="font-semibold text-surface-900 truncate">
                      {{ row.serviceName }}
                    </p>
                    @if (row.catalogHint) {
                      <p class="text-xs text-surface-500 mt-0.5">{{ row.catalogHint }}</p>
                    }
                    @if (editingServiceId() === row.serviceId) {
                      <div class="mt-3 space-y-3">
                        <label class="flex items-center gap-2 text-xs font-medium text-surface-600 dark:text-surface-300">
                          <p-checkbox
                            [ngModel]="editOverride()"
                            (ngModelChange)="editOverride.set($event)"
                            [binary]="true"
                          />
                          Ustaw własny czas i cenę (inaczej dziedziczy z katalogu)
                        </label>
                        @if (editOverride()) {
                          <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
                            <label class="flex flex-col gap-1 text-xs font-medium text-surface-600 dark:text-surface-300">
                              Czas (min)
                              <input
                                type="number"
                                min="0"
                                class="rounded-lg border border-surface-300 dark:border-surface-600 bg-surface-0 dark:bg-surface-950 px-3 py-2 text-sm"
                                [ngModel]="editDuration()"
                                (ngModelChange)="editDuration.set($event === '' ? 0 : +$event)"
                              />
                            </label>
                            <label class="flex flex-col gap-1 text-xs font-medium text-surface-600 dark:text-surface-300">
                              Kwota
                              <input
                                type="number"
                                min="0"
                                step="0.01"
                                class="rounded-lg border border-surface-300 dark:border-surface-600 bg-surface-0 dark:bg-surface-950 px-3 py-2 text-sm"
                                [ngModel]="editAmount()"
                                (ngModelChange)="editAmount.set($event === '' ? 0 : +$event)"
                              />
                            </label>
                            <label class="flex flex-col gap-1 text-xs font-medium text-surface-600 dark:text-surface-300">
                              Waluta
                              <input
                                type="text"
                                maxlength="3"
                                class="rounded-lg border border-surface-300 dark:border-surface-600 bg-surface-0 dark:bg-surface-950 px-3 py-2 text-sm uppercase"
                                [ngModel]="editCurrency()"
                                (ngModelChange)="editCurrency.set($event)"
                              />
                            </label>
                          </div>
                        } @else {
                          <p class="text-xs text-surface-500">Czas i cena będą dziedziczone z katalogu usługi.</p>
                        }
                      </div>
                    } @else {
                      <p class="text-sm text-surface-700 mt-1">
                        <span class="text-surface-500">Czas:</span> {{ row.duration }} min
                        @if (row.inheritsDuration) {
                          <span class="text-xs text-surface-400">(z katalogu)</span>
                        }
                        ·
                        <span class="text-surface-500">Cena:</span>
                        {{ row.amount | number: '1.2-2' }} {{ row.currency }}
                        @if (row.inheritsPrice) {
                          <span class="text-xs text-surface-400">(z katalogu)</span>
                        }
                      </p>
                    }
                  </div>
                  <div class="flex flex-wrap gap-2 shrink-0">
                    @if (editingServiceId() === row.serviceId) {
                      <p-button
                        label="Zapisz"
                        icon="pi pi-check"
                        size="small"
                        [loading]="saving()"
                        [disabled]="saving() || !canManageAssignments()"
                        (onClick)="saveEdit(row.serviceId)"
                      />
                      <p-button
                        label="Anuluj"
                        severity="secondary"
                        size="small"
                        [text]="true"
                        (onClick)="cancelEdit()"
                      />
                    } @else {
                      <p-button
                        label="Edytuj"
                        icon="pi pi-pencil"
                        size="small"
                        [text]="true"
                        [disabled]="!canManageAssignments()"
                        (onClick)="startEdit(row)"
                      />
                      <p-button
                        label="Usuń"
                        icon="pi pi-trash"
                        size="small"
                        severity="danger"
                        [text]="true"
                        [disabled]="!canManageAssignments()"
                        (onClick)="confirmUnassign(row.serviceId, row.serviceName)"
                      />
                    }
                  </div>
                </li>
              }
            </ul>
          }

          <div class="border-t border-surface-200 dark:border-surface-100 pt-5">
            <p class="text-xs font-bold uppercase tracking-wider text-surface-500 mb-3">Dodaj usługę</p>
            @if (addOptions().length === 0) {
              <p class="text-sm text-surface-500">Wszystkie usługi z katalogu są już przypisane.</p>
            } @else {
              <div class="flex flex-col gap-4">
                <p-select
                  [options]="addOptions()"
                  optionLabel="label"
                  optionValue="value"
                  [ngModel]="addServiceId()"
                  (ngModelChange)="onAddServicePicked($event)"
                  placeholder="Wybierz usługę"
                  styleClass="w-full"
                  [showClear]="true"
                />
                <label class="flex items-center gap-2 text-xs font-medium text-surface-600 dark:text-surface-300">
                  <p-checkbox
                    [ngModel]="addOverride()"
                    (ngModelChange)="addOverride.set($event)"
                    [binary]="true"
                  />
                  Ustaw własny czas i cenę (inaczej dziedziczy z katalogu)
                </label>
                @if (addOverride()) {
                  <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
                    <label class="flex flex-col gap-1 text-xs font-medium text-surface-600 dark:text-surface-300">
                      Czas (min)
                      <input
                        type="number"
                        min="0"
                        class="rounded-lg border border-surface-300 dark:border-surface-600 bg-surface-0 dark:bg-surface-950 px-3 py-2 text-sm"
                        [ngModel]="addDuration()"
                        (ngModelChange)="addDuration.set($event === '' ? 0 : +$event)"
                      />
                    </label>
                    <label class="flex flex-col gap-1 text-xs font-medium text-surface-600 dark:text-surface-300">
                      Kwota
                      <input
                        type="number"
                        min="0"
                        step="0.01"
                        class="rounded-lg border border-surface-300 dark:border-surface-600 bg-surface-0 dark:bg-surface-950 px-3 py-2 text-sm"
                        [ngModel]="addAmount()"
                        (ngModelChange)="addAmount.set($event === '' ? 0 : +$event)"
                      />
                    </label>
                    <label class="flex flex-col gap-1 text-xs font-medium text-surface-600 dark:text-surface-300">
                      Waluta
                      <input
                        type="text"
                        maxlength="3"
                        class="rounded-lg border border-surface-300 dark:border-surface-600 bg-surface-0 dark:bg-surface-950 px-3 py-2 text-sm uppercase"
                        [ngModel]="addCurrency()"
                        (ngModelChange)="addCurrency.set($event)"
                      />
                    </label>
                  </div>
                }
                <p-button
                  label="Przypisz usługę"
                  icon="pi pi-plus"
                  [loading]="saving()"
                  [disabled]="saving() || !addServiceId() || !canManageAssignments()"
                  (onClick)="submitAssign()"
                />
              </div>
            }
          </div>
        </div>
      }
    </section>
  `,
})
export class EmployeeAssignedServicesComponent {
  employeeId = input<string | undefined>();
  /** Ukryj nagłówek sekcji (np. gdy tytuł jest na stronie dashboardu). */
  showPanelTitle = input(true);

  private http = inject(HttpClient);
  private apiBaseUrl = inject(API_BASE_URL);
  private employeesClient = inject(EmployeesClient);
  private servicesClient = inject(ServicesClient);
  private auth = inject(AuthSessionService);
  private messageService = inject(MessageService);
  private confirmationService = inject(ConfirmationService);
  private forbiddenMessage = 'Możesz zarządzać tylko własnym profilem usług.';

  protected canManageAssignments = computed(() => {
    const role = this.auth.currentRole();
    if (role === 'owner' || role === 'manager') {
      return true;
    }
    const currentEmployeeId = this.auth.currentEmployeeId();
    const targetEmployeeId = this.employeeId();
    return !!currentEmployeeId && !!targetEmployeeId && currentEmployeeId === targetEmployeeId;
  });

  protected saving = signal(false);
  protected editingServiceId = signal<string | null>(null);
  // Override OFF (domyślnie) = czas/cena dziedziczone z katalogu; ON = własne wartości per-pracownik.
  protected editOverride = signal(false);
  protected editDuration = signal(0);
  protected editAmount = signal(0);
  protected editCurrency = signal('PLN');

  protected addServiceId = signal<string | null>(null);
  protected addOverride = signal(false);
  protected addDuration = signal(30);
  protected addAmount = signal(0);
  protected addCurrency = signal('PLN');

  catalog = rxResource({
    stream: () =>
      this.servicesClient.getServices(null).pipe(catchError(() => of([] as ServiceDto[]))),
    defaultValue: [] as ServiceDto[],
  });

  assigned = rxResource({
    // Klucz skalarny — obiektowy literał psuł porównanie tożsamości i wymuszał refetch.
    params: () => this.employeeId() ?? '',
    stream: ({ params: id }) => {
      if (!id) {
        return of([] as EmployeeServiceDto[]);
      }
      return this.employeesClient.getEmployeeServices(id).pipe(
        catchError(() => of([] as EmployeeServiceDto[]))
      );
    },
    defaultValue: [] as EmployeeServiceDto[],
  });

  private serviceById = computed(() => {
    const map = new Map<string, ServiceDto>();
    for (const s of this.catalog.value() ?? []) {
      if (s.id) map.set(s.id, s);
    }
    return map;
  });

  /**
   * Pozycja usługi w katalogu (ten przychodzi posortowany po OrderIndex, Name).
   * Usługi spoza katalogu (np. nieaktywne) lądują na końcu.
   */
  private catalogRank = computed(() => {
    const rank = new Map<string, number>();
    (this.catalog.value() ?? []).forEach((s, index) => {
      if (s.id) rank.set(s.id, index);
    });
    return rank;
  });

  assignedRows = computed(() => {
    const rank = this.catalogRank();
    // Sortujemy po katalogu — kolejność z `getEmployeeServices` nie jest gwarantowana.
    const list = [...(this.assigned.value() ?? [])].sort(
      (a, b) =>
        (rank.get(a.serviceId ?? '') ?? Number.MAX_SAFE_INTEGER) -
        (rank.get(b.serviceId ?? '') ?? Number.MAX_SAFE_INTEGER),
    );
    const svcMap = this.serviceById();
    return list.map((n0) => {
      const n = normalizeAssignment(n0);
      const svc = svcMap.get(n.serviceId);
      const name = svc?.name ?? n.serviceId;

      // Efektywne wartości: override per-pracownik albo wartość z katalogu (gdy null = dziedziczy).
      const catalogDuration = svc?.durationInMinutes ?? 0;
      const catalogAmount = svc?.price?.amount ?? 0;
      const catalogCurrency = (svc?.price?.currency ?? 'PLN') || 'PLN';
      const duration = n.customDuration ?? catalogDuration;
      const amount = n.customAmount ?? catalogAmount;
      const currency = n.customCurrency ?? catalogCurrency;
      const inheritsDuration = n.customDuration == null;
      const inheritsPrice = n.customAmount == null;

      let catalogHint: string | null = null;
      if (svc?.durationInMinutes != null || svc?.price != null) {
        const parts: string[] = [];
        if (svc?.durationInMinutes != null) {
          parts.push(`katalog: ${svc.durationInMinutes} min`);
        }
        if (svc?.price?.amount != null) {
          parts.push(`${svc.price.amount} ${svc.price.currency ?? ''}`.trim());
        }
        if (parts.length) catalogHint = 'W katalogu: ' + parts.join(', ');
      }
      return {
        serviceId: n.serviceId,
        duration,
        amount,
        currency,
        inheritsDuration,
        inheritsPrice,
        serviceName: name,
        catalogHint,
      };
    });
  });

  addOptions = computed(() => {
    const assignedIds = new Set(
      (this.assigned.value() ?? []).map((x) => normalizeAssignment(x).serviceId).filter(Boolean)
    );
    const out: { label: string; value: string }[] = [];
    for (const s of this.catalog.value() ?? []) {
      if (s.id && !assignedIds.has(s.id)) {
        out.push({ label: s.name ?? s.id, value: s.id });
      }
    }
    return out.sort((a, b) => a.label.localeCompare(b.label, 'pl'));
  });

  protected onAddServicePicked(id: string | null): void {
    this.addServiceId.set(id);
    if (!id) return;
    const svc = this.serviceById().get(id);
    if (svc) {
      this.addDuration.set(
        Math.max(0, Math.round(svc.durationInMinutes ?? 30))
      );
      const p = svc.price;
      this.addAmount.set(p?.amount ?? 0);
      this.addCurrency.set((p?.currency ?? 'PLN').trim() || 'PLN');
    }
  }

  protected startEdit(row: AssignedRow): void {
    if (!this.canManageAssignments()) return;
    this.editingServiceId.set(row.serviceId);
    // Override aktywny, jeśli wiersz ma własny czas albo cenę (nie dziedziczy). Pola prefillujemy
    // wartościami efektywnymi (override albo katalog), żeby włączenie checkboxa miało sensowny start.
    this.editOverride.set(!row.inheritsDuration || !row.inheritsPrice);
    this.editDuration.set(row.duration);
    this.editAmount.set(row.amount);
    this.editCurrency.set(row.currency);
  }

  protected cancelEdit(): void {
    this.editingServiceId.set(null);
  }

  protected saveEdit(serviceId: string): void {
    const empId = this.employeeId();
    if (!empId || this.saving() || !this.canManageAssignments()) return;

    // Override OFF → wyślij puste wartości (null) = wyczyść override, dziedzicz z katalogu.
    let body: { customDuration?: number; amount?: number; currency?: string };
    if (this.editOverride()) {
      const duration = Math.round(this.editDuration());
      const amount = this.editAmount();
      const currency = (this.editCurrency() || 'PLN').trim() || 'PLN';
      if (duration <= 0 || amount < 0 || !Number.isFinite(amount)) {
        this.messageService.add({
          severity: 'warn',
          summary: 'Sprawdź dane',
          detail: 'Podaj poprawny czas (> 0) i kwotę (≥ 0).',
        });
        return;
      }
      body = { customDuration: duration, amount, currency };
    } else {
      body = {};
    }

    this.saving.set(true);
    this.employeesClient
      .updateEmployeeService(empId, serviceId, body)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: 'Zapisano',
            detail: 'Zaktualizowano usługę pracownika.',
          });
          this.editingServiceId.set(null);
          this.assigned.reload();
        },
        error: (err) => this.showApiError(err, 'Nie udało się zapisać zmian.'),
      });
  }

  protected confirmUnassign(serviceId: string, label: string): void {
    const empId = this.employeeId();
    if (!empId || !this.canManageAssignments()) return;
    this.confirmationService.confirm({
      message: `Usunąć przypisanie usługi „${label}” dla tego pracownika?`,
      header: 'Potwierdzenie',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Usuń',
      rejectLabel: 'Anuluj',
      accept: () => this.doUnassign(empId, serviceId),
    });
  }

  private doUnassign(empId: string, serviceId: string): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.employeesClient
      .unassignService(empId, serviceId)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: 'Usunięto',
            detail: 'Usługa została odpięta od pracownika.',
          });
          this.assigned.reload();
        },
        error: (err) => this.showApiError(err, 'Nie udało się usunąć przypisania.'),
      });
  }

  protected submitAssign(): void {
    const empId = this.employeeId();
    const serviceId = this.addServiceId();
    if (!empId || !serviceId || this.saving() || !this.canManageAssignments()) return;

    // Override OFF (domyślnie) → przypisz BEZ custom wartości; czas/cena dziedziczą z katalogu.
    const body: { serviceId: string; customDuration?: number; amount?: number; currency?: string } = {
      serviceId: String(serviceId),
    };
    if (this.addOverride()) {
      const duration = Math.round(this.addDuration());
      const amount = this.addAmount();
      const currency = (this.addCurrency() || 'PLN').trim() || 'PLN';
      if (duration <= 0 || amount < 0 || !Number.isFinite(amount)) {
        this.messageService.add({
          severity: 'warn',
          summary: 'Sprawdź dane',
          detail: 'Podaj poprawny czas (> 0) i kwotę (≥ 0).',
        });
        return;
      }
      const c = currency.trim().toUpperCase();
      body.customDuration = duration;
      body.amount = amount;
      body.currency = /^[A-Z]{3}$/.test(c) ? c : 'PLN';
    }
    this.saving.set(true);
    this.http
      .post(`${this.apiBaseUrl}/api/Employees/${encodeURIComponent(empId)}/services`, body, {
        headers: { 'Content-Type': 'application/json' },
        responseType: 'text',
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: 'Dodano',
            detail: 'Usługa została przypisana do pracownika.',
          });
          this.addServiceId.set(null);
          this.addOverride.set(false);
          this.addDuration.set(30);
          this.addAmount.set(0);
          this.addCurrency.set('PLN');
          this.assigned.reload();
        },
        error: (err) => this.showApiError(err, 'Nie udało się przypisać usługi.'),
      });
  }

  private showApiError(err: unknown, fallbackDetail: string): void {
    const status = (err as { status?: number } | null | undefined)?.status;
    if (status === 403) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Brak dostępu',
        detail: this.forbiddenMessage,
      });
      return;
    }
    this.messageService.add({
      severity: 'error',
      summary: 'Błąd',
      detail: fallbackDetail,
    });
  }
}
