import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomerDto, CustomersClient } from '@core/api/api-client';
import { ButtonModule } from 'primeng/button';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { lastValueFrom } from 'rxjs';
import { CustomerCardComponent } from '../../ui/customer-card/customer-card.component';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';

type CustomerListFilter = 'all' | 'new' | 'inactive' | 'whitelisted';
type CustomerListSort = 'recent' | 'alpha' | 'oldest';

@Component({
  selector: 'app-customer-list',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, CustomerCardComponent, EmptyStateComponent],
  template: `
    <div class="admin-glass-card rounded-4xl p-4 sm:p-8">
      <div
        class="flex flex-col sm:flex-row items-start sm:items-end justify-between mb-6 gap-6"
      >
        <div>
          <h2 class="admin-h2 text-surface-900 mb-1">
            Baza klientów
          </h2>
          <p class="text-surface-600 dark:text-surface-400 text-sm">
            Filtruj i przeglądaj dane kontaktowe oraz notatki o klientach salonu.
          </p>
        </div>

        <div class="flex flex-col sm:flex-row items-stretch sm:items-center gap-3 w-full sm:w-auto">
          <div
            class="flex items-center gap-3 bg-white/85 dark:bg-surface-50/70 px-5 py-3 rounded-full border border-surface-200/80 dark:border-surface-200/70"
          >
            <span class="text-[10px] font-bold text-surface-500 uppercase tracking-[0.2em]">Łącznie</span>
            <span class="w-px h-4 bg-surface-300 dark:bg-surface-200"></span>
            <span class="text-xl font-bold text-primary">{{ filtered().length }}</span>
            <span class="text-xs text-surface-500">/ {{ customersCount() }}</span>
          </div>

          @if (canManageWhitelist()) {
            <p-button
              type="button"
              [text]="!selectionMode()"
              severity="secondary"
              (onClick)="toggleSelectionMode()"
              [label]="selectionMode() ? 'ZAKOŃCZ ZAZNACZANIE' : 'ZAZNACZ WIELU'"
              [icon]="selectionMode() ? 'pi pi-times' : 'pi pi-check-square'"
              class="font-bold"
            />
          }

          <p-button
            data-tour="customers-add"
            (onClick)="handleAddCustomer()"
            label="DODAJ KLIENTA"
            class="px-6 py-3 font-bold"
          />
        </div>
      </div>

      @if (selectionMode()) {
        <div
          class="flex flex-wrap items-center justify-between gap-3 mb-4 rounded-2xl border border-amber-300/60 bg-amber-50/70 dark:bg-amber-950/30 px-4 py-3"
        >
          <span class="text-sm font-bold text-amber-900 dark:text-amber-200">
            Zaznaczono: {{ selectedIds().size }}
          </span>
          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              (click)="selectAllVisible()"
              class="px-3 py-1.5 text-xs font-bold uppercase tracking-wider rounded-lg border border-amber-300/60 bg-white/70 hover:bg-white dark:border-amber-300/20 dark:bg-white/5 dark:hover:bg-white/10 dark:text-amber-100"
            >
              Zaznacz widoczne
            </button>
            <button
              type="button"
              (click)="clearSelection()"
              class="px-3 py-1.5 text-xs font-bold uppercase tracking-wider rounded-lg border border-amber-300/60 bg-white/70 hover:bg-white dark:border-amber-300/20 dark:bg-white/5 dark:hover:bg-white/10 dark:text-amber-100"
            >
              Wyczyść
            </button>
            <p-button
              [disabled]="selectedIds().size === 0 || bulkBusy()"
              [loading]="bulkBusy()"
              (onClick)="bulkSetWhitelist(true)"
              icon="pi pi-user-plus"
              label="Whitelist"
              size="small"
            />
            <p-button
              [disabled]="selectedIds().size === 0 || bulkBusy()"
              [loading]="bulkBusy()"
              (onClick)="bulkSetWhitelist(false)"
              icon="pi pi-user-minus"
              severity="secondary"
              label="Usuń z whitelisty"
              size="small"
            />
          </div>
        </div>
      }

      <div class="flex flex-col sm:flex-row sm:flex-wrap items-stretch sm:items-center gap-2 mb-6">
        <div class="relative flex-1 min-w-0 sm:min-w-56">
          <i class="pi pi-search text-xs text-surface-400 absolute left-3 top-1/2 -translate-y-1/2"></i>
          <input
            type="search"
            [ngModel]="search()"
            (ngModelChange)="search.set($event)"
            placeholder="Szukaj po nazwisku, telefonie, e-mailu…"
            class="w-full pl-9 pr-3 py-2.5 rounded-xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 text-sm placeholder:text-surface-400 focus:outline-none focus:border-primary/55 transition-colors"
          />
        </div>
        <div class="flex items-center gap-1 p-1 rounded-xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 overflow-x-auto min-w-0">
          @for (f of filters; track f.id) {
            <button
              type="button"
              (click)="filter.set(f.id)"
              [class.bg-slate-900]="filter() === f.id"
              [class.text-white]="filter() === f.id"
              [class.dark:bg-surface-200]="filter() === f.id"
              [class.dark:text-surface-900]="filter() === f.id"
              class="px-3 py-1.5 text-xs font-bold uppercase tracking-wider rounded-lg text-surface-700 transition-colors"
            >
              {{ f.label }}
            </button>
          }
        </div>
        <select
          [ngModel]="sort()"
          (ngModelChange)="sort.set($event)"
          class="min-w-0 px-3 py-2 rounded-xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 text-xs font-bold uppercase tracking-wider"
        >
          <option value="recent">Najnowsi pierwsi</option>
          <option value="oldest">Najstarsi pierwsi</option>
          <option value="alpha">Alfabetycznie</option>
        </select>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4 sm:gap-6">
        @for (customer of filtered(); track customer.id) {
          <div
            class="relative"
            [class.ring-2]="selectionMode() && customer.id && selectedIds().has(customer.id)"
            [class.ring-amber-500]="selectionMode() && customer.id && selectedIds().has(customer.id)"
            [class.rounded-3xl]="true"
          >
            @if (selectionMode() && customer.id) {
              <button
                type="button"
                (click)="toggleSelection(customer.id)"
                class="absolute top-4 left-4 z-10 w-7 h-7 rounded-md border-2 flex items-center justify-center transition-colors"
                [class.bg-amber-500]="selectedIds().has(customer.id)"
                [class.border-amber-500]="selectedIds().has(customer.id)"
                [class.text-white]="selectedIds().has(customer.id)"
                [class.bg-white]="!selectedIds().has(customer.id)"
                [class.border-surface-300]="!selectedIds().has(customer.id)"
                aria-label="Zaznacz klienta"
              >
                @if (selectedIds().has(customer.id)) {
                  <i class="pi pi-check text-xs"></i>
                }
              </button>
            }
            <app-customer-card
              [customer]="customer"
              [canManage]="canManageBusiness()"
              [canManageWhitelist]="canManageWhitelist()"
              (openProfile)="handleOpenProfile($event)"
              (edit)="handleEditCustomer($event)"
              (delete)="handleDeleteCustomer($event)"
              (toggleWhitelist)="handleToggleWhitelist($event)"
            />
          </div>
        } @empty {
          <app-empty-state
            class="col-span-full"
            icon="pi pi-users"
            title="Brak klientów"
            description="Nie masz jeszcze zapisanych klientów. Dodaj pierwszy wpis, aby zacząć budować bazę."
            ctaLabel="Dodaj klienta"
            ctaIcon="pi pi-plus"
            (ctaClick)="handleAddCustomer()"
          />
        }
      </div>
    </div>
  `,
})
export class CustomerListComponent {
  private customersService = inject(CustomersClient);
  private router = inject(Router);
  private confirmationService = inject(ConfirmationService);
  private messages = inject(MessageService);
  private readonly auth = inject(AuthSessionService);

  /**
   * Usunięcie klienta = polityka API `BusinessManagement` → tylko właścicielka.
   * Pracownik/manager: odczyt + dodawanie/edycja.
   */
  protected readonly canManageBusiness = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'systemAdmin';
  });

  /**
   * Whitelist (pojedynczo i masowo) = polityka API `StaffManagement` → właścicielka i manager.
   * Zdejmuje z klienta wymóg zadatku, więc pracownik jej nie dotyka.
   */
  protected readonly canManageWhitelist = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager' || role === 'systemAdmin';
  });

  customers = rxResource({
    stream: () => this.customersService.getCustomers(),
    defaultValue: [] as CustomerDto[],
  });

  readonly search = signal('');
  readonly filter = signal<CustomerListFilter>('all');
  readonly sort = signal<CustomerListSort>('recent');
  readonly selectionMode = signal<boolean>(false);
  readonly selectedIds = signal<Set<string>>(new Set<string>());
  readonly bulkBusy = signal<boolean>(false);

  protected readonly filters: { id: CustomerListFilter; label: string }[] = [
    { id: 'all', label: 'Wszyscy' },
    { id: 'new', label: 'Nowi (30 dni)' },
    { id: 'inactive', label: 'Nieaktywni (>90 dni)' },
    { id: 'whitelisted', label: 'Whitelista' },
  ];

  /** Lista po filtrach i sortowaniu — bazujemy na `createdAt` (brak danych o ostatniej wizycie w `CustomerDto`). */
  readonly filtered = computed<CustomerDto[]>(() => {
    const q = this.search().trim().toLowerCase();
    const f = this.filter();
    const now = Date.now();
    let list = this.customers.value().filter((c) => {
      if (q) {
        const haystack = [c.firstName, c.lastName, c.email, c.phoneNumber]
          .filter((s): s is string => !!s)
          .join(' ')
          .toLowerCase();
        if (!haystack.includes(q)) return false;
      }
      const created = c.createdAt ? new Date(c.createdAt as unknown as string).getTime() : 0;
      const days = (now - created) / 86400000;
      if (f === 'new' && days > 30) return false;
      if (f === 'inactive' && days < 90) return false;
      if (f === 'whitelisted' && !c.isWhitelisted) return false;
      return true;
    });

    const s = this.sort();
    list = [...list].sort((a, b) => {
      if (s === 'alpha') {
        const an = `${a.lastName ?? ''} ${a.firstName ?? ''}`.trim().toLowerCase();
        const bn = `${b.lastName ?? ''} ${b.firstName ?? ''}`.trim().toLowerCase();
        return an.localeCompare(bn);
      }
      const ad = a.createdAt ? new Date(a.createdAt as unknown as string).getTime() : 0;
      const bd = b.createdAt ? new Date(b.createdAt as unknown as string).getTime() : 0;
      return s === 'oldest' ? ad - bd : bd - ad;
    });
    return list;
  });

  customersCount = computed(() => this.customers.value().length);

  handleAddCustomer() {
    void this.router.navigate(['/admin/customers/new']);
  }

  handleOpenProfile(id: string | undefined) {
    if (!id) return;
    void this.router.navigate(['/admin/customers', id]);
  }

  handleEditCustomer(id: string) {
    void this.router.navigate(['/admin/customers/edit', id]);
  }

  handleDeleteCustomer(id: string) {
    this.confirmationService.confirm({
      message: 'Czy na pewno chcesz usunąć tego klienta?',
      header: 'Potwierdzenie usunięcia',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Tak',
      rejectLabel: 'Nie',
      accept: () => {
        this.customersService.deleteCustomer(id).subscribe({
          next: () => this.customers.reload(),
          error: (err) => console.error('Błąd podczas usuwania klienta:', err),
        });
      },
    });
  }

  toggleSelectionMode(): void {
    this.selectionMode.update((v) => !v);
    if (!this.selectionMode()) {
      this.clearSelection();
    }
  }

  toggleSelection(id: string): void {
    this.selectedIds.update((set) => {
      const next = new Set(set);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  selectAllVisible(): void {
    const ids = this.filtered()
      .map((c) => c.id)
      .filter((id): id is string => !!id);
    this.selectedIds.set(new Set(ids));
  }

  clearSelection(): void {
    this.selectedIds.set(new Set<string>());
  }

  async handleToggleWhitelist(payload: { id: string; whitelisted: boolean }): Promise<void> {
    try {
      await lastValueFrom(
        this.customersService.setWhitelist(payload.id, { isWhitelisted: payload.whitelisted }),
      );
      this.messages.add({
        severity: 'success',
        summary: payload.whitelisted ? 'Dodano do whitelisty' : 'Usunięto z whitelisty',
        detail: payload.whitelisted
          ? 'Klient może teraz rezerwować w trybie tylko zaproszeni.'
          : 'Klient nie będzie mógł rezerwować w trybie tylko zaproszeni.',
        life: 3500,
      });
      this.customers.reload();
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Nie udało się zaktualizować',
        detail: 'Spróbuj ponownie.',
        life: 4000,
      });
    }
  }

  async bulkSetWhitelist(value: boolean): Promise<void> {
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;
    this.bulkBusy.set(true);
    try {
      const result = await lastValueFrom(
        this.customersService.bulkWhitelist({ customerIds: ids, isWhitelisted: value }),
      );
      this.messages.add({
        severity: 'success',
        summary: value ? 'Zaktualizowano whitelistę' : 'Usunięto z whitelisty',
        detail: `Zmieniono ${result.updatedCount ?? ids.length} z ${ids.length} klientów.`,
        life: 4000,
      });
      this.clearSelection();
      this.selectionMode.set(false);
      this.customers.reload();
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Bulk update nie powiódł się',
        detail: 'Spróbuj ponownie lub odśwież listę.',
        life: 4500,
      });
    } finally {
      this.bulkBusy.set(false);
    }
  }
}
