import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { rxResource } from '@angular/core/rxjs-interop';
import { CreateVatRateCommand, UpdateVatRateCommand, VatRatesClient, VatRateDto } from '@core/api/api-client';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { lastValueFrom } from 'rxjs';

function decimalToPercent(value: number | undefined): number {
  if (value == null || Number.isNaN(value)) {
    return 0;
  }
  return Math.round(value * 10_000) / 100;
}

function percentToDecimal(percent: number): number {
  return Math.round(percent * 100) / 10_000;
}

@Component({
  selector: 'app-vat-rates-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, ButtonModule, ToggleSwitch],
  template: `
    <div class="min-h-screen px-4 pb-14 pt-4 sm:px-8 sm:pt-8">
      <div class="max-w-3xl mx-auto space-y-6">
        <header class="admin-glass-card rounded-4xl px-6 py-8 sm:px-8 sm:py-9">
          <span class="admin-section-label text-primary mb-3 block">
            Konfiguracja salonu
          </span>
          <h1
            class="text-3xl sm:text-4xl font-black tracking-tight text-surface-900 leading-tight mb-2"
          >
            Stawki VAT
          </h1>
          <p class="text-surface-700 dark:text-surface-300 text-sm sm:text-base leading-relaxed">
            Stawki w formacie procentowym (np. 23 dla 23&nbsp;%) są zapisywane jako ułamek dziesiętny w
            bazie. Dostęp: właściciel lub manager.
          </p>
        </header>

        <section
          class="admin-glass-card rounded-4xl p-5 sm:p-6 mb-8"
        >
          <h2 class="text-lg font-bold text-surface-900 mb-4">
            {{ editingId() ? 'Edycja stawki' : 'Nowa stawka' }}
          </h2>
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div class="sm:col-span-2">
              <label class="text-xs font-bold uppercase tracking-wider text-surface-500 mb-1 block" for="vat-name"
                >Nazwa</label
              >
              <input
                id="vat-name"
                type="text"
                maxlength="50"
                class="w-full rounded-xl border border-surface-200 bg-surface-0 px-3 py-2.5 text-surface-900 shadow-sm outline-none focus:border-primary dark:border-surface-200 dark:bg-surface-950"
                [ngModel]="formName()"
                (ngModelChange)="formName.set($event)"
                placeholder="np. VAT 23%"
              />
            </div>
            <div>
              <label class="text-xs font-bold uppercase tracking-wider text-surface-500 mb-1 block" for="vat-pct"
                >Stawka (%)</label
              >
              <input
                id="vat-pct"
                type="number"
                min="0"
                max="100"
                step="0.01"
                class="w-full rounded-xl border border-surface-200 bg-surface-0 px-3 py-2.5 text-surface-900 shadow-sm outline-none focus:border-primary dark:border-surface-200 dark:bg-surface-950"
                [ngModel]="formPercent()"
                (ngModelChange)="onPercentChange($event)"
              />
            </div>
            <div class="flex flex-col justify-end gap-2">
              <span class="text-xs font-bold uppercase tracking-wider text-surface-500">Domyślna dla usług</span>
              <p-toggleSwitch
                [ngModel]="formIsDefault()"
                (ngModelChange)="formIsDefault.set($event)"
              />
            </div>
          </div>
          <div class="mt-5 flex flex-wrap gap-3">
            @if (editingId(); as eid) {
              <p-button label="Zapisz zmiany" icon="pi pi-check" (onClick)="saveEdit(eid)" />
              <p-button label="Anuluj" severity="secondary" (onClick)="cancelEdit()" />
            } @else {
              <p-button label="Dodaj stawkę" icon="pi pi-plus" (onClick)="createRate()" />
            }
          </div>
        </section>

        @if (vatRates.isLoading()) {
          <div class="flex justify-center py-16">
            <i class="pi pi-spin pi-spinner text-3xl text-primary" aria-hidden="true"></i>
          </div>
        } @else if (vatRates.error()) {
          <div
            class="rounded-xl border border-red-200 dark:border-red-900/50 bg-red-50 dark:bg-red-950/20 p-6 text-red-800 dark:text-red-200 text-sm"
          >
            Nie udało się wczytać stawek VAT. Sprawdź połączenie i uprawnienia (właściciel lub manager).
          </div>
        } @else {
          <div class="rounded-2xl border border-surface-200 dark:border-surface-100 overflow-hidden shadow-sm">
            <table class="w-full text-left text-sm">
              <thead class="bg-surface-100 dark:bg-surface-100/80 text-xs uppercase tracking-wider text-surface-600 dark:text-surface-400">
                <tr>
                  <th class="px-4 py-3 font-bold">Nazwa</th>
                  <th class="px-4 py-3 font-bold w-28">Stawka</th>
                  <th class="px-4 py-3 font-bold w-32">Domyślna</th>
                  <th class="px-4 py-3 font-bold w-28 text-right">Akcje</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-surface-200 dark:divide-surface-800 bg-surface-0 dark:bg-surface-950">
                @for (row of sortedRates(); track row.id) {
                  <tr class="text-surface-800">
                    <td class="px-4 py-3 font-medium">{{ row.name }}</td>
                    <td class="px-4 py-3 font-mono">{{ formatPercent(row.value) }}%</td>
                    <td class="px-4 py-3">
                      @if (row.isDefault) {
                        <span
                          class="inline-flex rounded-full bg-primary/15 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-primary"
                          >Tak</span
                        >
                      } @else {
                        <span class="text-surface-400">—</span>
                      }
                    </td>
                    <td class="px-4 py-3 text-right whitespace-nowrap">
                      <button
                        type="button"
                        class="mr-2 text-xs font-bold uppercase tracking-wide text-primary hover:underline"
                        (click)="startEdit(row)"
                      >
                        Edytuj
                      </button>
                      <button
                        type="button"
                        class="text-xs font-bold uppercase tracking-wide text-red-600 hover:underline dark:text-red-400"
                        (click)="confirmDelete(row)"
                      >
                        Usuń
                      </button>
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="4" class="px-4 py-10 text-center text-surface-500">
                      Brak zdefiniowanych stawek — dodaj pierwszą powyżej.
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    </div>
  `,
})
export class VatRatesPageComponent {
  private readonly vatRatesClient = inject(VatRatesClient);
  private readonly confirm = inject(ConfirmationService);
  private readonly messages = inject(MessageService);

  readonly formName = signal('');
  readonly formPercent = signal<number | null>(23);
  readonly formIsDefault = signal(false);
  readonly editingId = signal<string | null>(null);

  onPercentChange(v: number | string | null): void {
    if (v === '' || v === null) {
      this.formPercent.set(null);
      return;
    }
    const n = typeof v === 'string' ? Number(v) : v;
    this.formPercent.set(Number.isNaN(n) ? null : n);
  }

  vatRates = rxResource({
    stream: () => this.vatRatesClient.getVatRates(),
    defaultValue: [] as VatRateDto[],
  });

  sortedRates = computed(() => {
    const rows = this.vatRates.value() ?? [];
    return [...rows].sort((a, b) => String(a.name ?? '').localeCompare(String(b.name ?? ''), 'pl'));
  });

  formatPercent(value: number | undefined): string {
    return decimalToPercent(value).toLocaleString('pl-PL', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2,
    });
  }

  startEdit(row: VatRateDto): void {
    const id = row.id;
    if (!id) {
      return;
    }
    this.editingId.set(id);
    this.formName.set(row.name ?? '');
    this.formPercent.set(decimalToPercent(row.value ?? 0));
    this.formIsDefault.set(!!row.isDefault);
  }

  cancelEdit(): void {
    this.editingId.set(null);
    this.resetForm();
  }

  private resetForm(): void {
    this.formName.set('');
    this.formPercent.set(23);
    this.formIsDefault.set(false);
  }

  private readForm(): { name: string; value: number } | null {
    const name = this.formName().trim();
    const pct = this.formPercent();
    if (!name) {
      this.messages.add({
        severity: 'warn',
        summary: 'Brak nazwy',
        detail: 'Podaj nazwę stawki (max 50 znaków).',
        life: 4_000,
      });
      return null;
    }
    if (pct == null || Number.isNaN(pct) || pct < 0 || pct > 100) {
      this.messages.add({
        severity: 'warn',
        summary: 'Nieprawidłowa stawka',
        detail: 'Wpisz wartość od 0 do 100 (%).',
        life: 4_000,
      });
      return null;
    }
    return { name, value: percentToDecimal(pct) };
  }

  async createRate(): Promise<void> {
    const v = this.readForm();
    if (!v) {
      return;
    }
    const cmd: CreateVatRateCommand = {
      name: v.name,
      value: v.value,
      isDefault: this.formIsDefault(),
    };
    try {
      await lastValueFrom(this.vatRatesClient.createVatRate(cmd));
      this.messages.add({
        severity: 'success',
        summary: 'Dodano',
        detail: 'Nowa stawka VAT została zapisana.',
        life: 3_500,
      });
      this.resetForm();
      this.vatRates.reload();
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Błąd',
        detail: 'Nie udało się dodać stawki (np. duplikat nazwy).',
        life: 5_000,
      });
    }
  }

  async saveEdit(id: string): Promise<void> {
    const v = this.readForm();
    if (!v) {
      return;
    }
    const cmd: UpdateVatRateCommand = {
      id,
      name: v.name,
      value: v.value,
      isDefault: this.formIsDefault(),
    };
    try {
      await lastValueFrom(this.vatRatesClient.updateVatRate(id, cmd));
      this.messages.add({
        severity: 'success',
        summary: 'Zapisano',
        detail: 'Stawka VAT została zaktualizowana.',
        life: 3_500,
      });
      this.cancelEdit();
      this.vatRates.reload();
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Błąd',
        detail: 'Nie udało się zapisać zmian.',
        life: 5_000,
      });
    }
  }

  confirmDelete(row: VatRateDto): void {
    const id = row.id;
    if (!id) {
      return;
    }
    this.confirm.confirm({
      message: `Usunąć stawkę „${row.name ?? ''}”?`,
      header: 'Potwierdzenie',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Usuń',
      rejectLabel: 'Anuluj',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => {
        this.vatRatesClient.deleteVatRate(id).subscribe({
          next: () => {
            this.messages.add({
              severity: 'success',
              summary: 'Usunięto',
              detail: 'Stawka została usunięta.',
              life: 3_000,
            });
            if (this.editingId() === id) {
              this.cancelEdit();
            }
            this.vatRates.reload();
          },
          error: () => {
            this.messages.add({
              severity: 'error',
              summary: 'Błąd',
              detail: 'Nie można usunąć stawki (możliwe powiązania z usługami).',
              life: 5_000,
            });
          },
        });
      },
    });
  }
}
