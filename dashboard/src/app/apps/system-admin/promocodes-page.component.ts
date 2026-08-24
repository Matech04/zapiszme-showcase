import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { FormsModule } from '@angular/forms';
import {
  AdminPromoCodesClient,
  PromoCodeAppliesTo,
  PromoCodeDto,
  PromoCodeKind,
  PromoCodeRedemptionDto,
  PromoDiscountType,
} from '@core/api/api-client';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';

/**
 * Panel admin: CRUD kodów promocyjnych. Tylko `systemAdmin` (route guard) ma dostęp.
 * API: /api/admin/promocodes (POST/GET), /api/admin/promocodes/{id}/(deactivate|validity|redemptions).
 */
@Component({
  selector: 'app-promocodes-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TableModule, ButtonModule, TagModule, DialogModule, ConfirmDialogModule,
    Select, InputTextModule, InputNumberModule, CheckboxModule,
    DatePickerModule, FormsModule,
  ],
  providers: [ConfirmationService],
  template: `
    <p-confirmDialog />

    <div class="p-6 lg:p-10 max-w-7xl mx-auto">
      <div class="mb-8 flex items-end justify-between gap-4">
        <div>
          <h1 class="text-3xl font-black tracking-tight text-surface-900">Kody promocyjne</h1>
          <p class="mt-1 text-sm text-surface-500 dark:text-surface-400">
            {{ codes.value()?.length ?? 0 }} kodów (globalne, dostępne dla wszystkich salonów)
          </p>
        </div>
        <div class="flex items-center gap-2">
          <p-select
            [options]="filterOptions"
            [(ngModel)]="activeFilter"
            (ngModelChange)="codes.reload()"
            optionLabel="label"
            optionValue="value"
            styleClass="w-44"
          />
          <p-button label="Nowy kod" icon="pi pi-plus" (onClick)="openCreate()" />
        </div>
      </div>

      <div class="rounded-3xl border border-surface-200 dark:border-surface-200 overflow-hidden bg-white dark:bg-surface-50 shadow-sm">
        <p-table
          [value]="codes.value() ?? []"
          [loading]="codes.isLoading()"
          styleClass="p-datatable-sm"
          [rowHover]="true"
        >
          <ng-template pTemplate="header">
            <tr>
              <th>Kod</th>
              <th>Typ</th>
              <th>Wartość</th>
              <th>Użycia</th>
              <th>Ważny do</th>
              <th>Status</th>
              <th></th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-c>
            <tr>
              <td class="font-mono font-bold">{{ c.code }}</td>
              <td>
                <p-tag [value]="discountTypeLabel(c.discountType)" severity="info" />
              </td>
              <td class="text-sm">
                {{ valueLabel(c) }}
                @if (c.durationMonths) {
                  <span class="block text-xs opacity-60">{{ c.durationMonths }} mies.</span>
                }
              </td>
              <td class="text-sm">
                <span class="font-bold">{{ c.currentUses }}</span>
                <span class="opacity-60">/{{ c.maxTotalUses ?? '∞' }}</span>
              </td>
              <td class="text-sm">{{ c.validUntil ? formatDate(c.validUntil) : 'bez końca' }}</td>
              <td>
                @if (c.isActive) {
                  <p-tag value="aktywny" severity="success" />
                } @else {
                  <p-tag value="wyłączony" severity="secondary" />
                }
              </td>
              <td class="text-right flex justify-end gap-1">
                <p-button icon="pi pi-list" size="small" [text]="true" pTooltip="Redempcje"
                  (onClick)="openRedemptions(c)" />
                <p-button icon="pi pi-calendar" size="small" [text]="true" pTooltip="Zmień ważność"
                  (onClick)="openExtend(c)" />
                @if (c.isActive) {
                  <p-button icon="pi pi-ban" size="small" [text]="true" severity="danger" pTooltip="Wyłącz"
                    (onClick)="confirmDeactivate(c)" />
                }
              </td>
            </tr>
          </ng-template>

          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="7" class="text-center py-8 text-surface-400">Brak kodów</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    </div>

    <!-- Create dialog -->
    <p-dialog header="Nowy kod promocyjny" [(visible)]="createOpen" [modal]="true" [style]="{ width: '520px' }" [closable]="!saving()">
      <div class="flex flex-col gap-4 py-2">
        <div class="flex flex-col gap-1">
          <label class="text-sm font-semibold">Kod</label>
          <input pInputText [(ngModel)]="newCode.code" placeholder="np. FOUNDING10" maxlength="64" />
          <p class="text-xs opacity-60">Zapisywany UPPERCASE. Min 3 znaki.</p>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">Typ rabatu</label>
            <p-select [options]="discountTypeOptions" [(ngModel)]="newCode.discountType"
              optionLabel="label" optionValue="value" styleClass="w-full" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">Rodzaj</label>
            <p-select [options]="kindOptions" [(ngModel)]="newCode.kind"
              optionLabel="label" optionValue="value" styleClass="w-full" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">{{ valueFieldLabel() }}</label>
            <p-inputNumber [(ngModel)]="newCode.discountValue" [minFractionDigits]="0" [maxFractionDigits]="2"
              styleClass="w-full" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">Czas trwania (mies.)</label>
            <p-inputNumber [(ngModel)]="newCode.durationMonths" [min]="1" [showButtons]="true" styleClass="w-full"
              placeholder="puste = na zawsze" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">Limit globalny</label>
            <p-inputNumber [(ngModel)]="newCode.maxTotalUses" [min]="1" [showButtons]="true" styleClass="w-full"
              placeholder="puste = nieograniczony" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">Limit / salon</label>
            <p-inputNumber [(ngModel)]="newCode.maxUsesPerTenant" [min]="1" [showButtons]="true" styleClass="w-full" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">Ważny od</label>
            <p-datePicker [(ngModel)]="newCode.validFrom" dateFormat="dd.mm.yy" [showIcon]="true" styleClass="w-full" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-semibold">Ważny do</label>
            <p-datePicker [(ngModel)]="newCode.validUntil" dateFormat="dd.mm.yy" [showIcon]="true" styleClass="w-full"
              placeholder="puste = bezterminowo" />
          </div>
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-semibold">Dla kogo</label>
          <p-select [options]="appliesToOptions" [(ngModel)]="newCode.appliesTo"
            optionLabel="label" optionValue="value" styleClass="w-full" />
        </div>

        <div class="flex justify-end gap-2 pt-2">
          <p-button label="Anuluj" [text]="true" [disabled]="saving()" (onClick)="createOpen = false" />
          <p-button label="Utwórz" icon="pi pi-check" [loading]="saving()" (onClick)="submitCreate()" />
        </div>
      </div>
    </p-dialog>

    <!-- Extend validity dialog -->
    <p-dialog [header]="'Zmień ważność — ' + (editingCode()?.code ?? '')"
      [(visible)]="extendOpen" [modal]="true" [style]="{ width: '380px' }" [closable]="!saving()">
      <div class="flex flex-col gap-3 py-2">
        <div class="flex flex-col gap-1">
          <label class="text-sm font-semibold">Nowa data ważności</label>
          <p-datePicker [(ngModel)]="newValidUntil" dateFormat="dd.mm.yy" [showIcon]="true"
            styleClass="w-full" placeholder="puste = bezterminowo" />
        </div>
        <div class="flex justify-end gap-2 pt-2">
          <p-button label="Anuluj" [text]="true" (onClick)="extendOpen = false" />
          <p-button label="Zapisz" icon="pi pi-check" [loading]="saving()" (onClick)="submitExtend()" />
        </div>
      </div>
    </p-dialog>

    <!-- Redemptions dialog -->
    <p-dialog [header]="'Redempcje — ' + (editingCode()?.code ?? '')"
      [(visible)]="redemptionsOpen" [modal]="true" [style]="{ width: '720px' }">
      <p-table [value]="redemptions.value() ?? []" [loading]="redemptions.isLoading()" styleClass="p-datatable-sm">
        <ng-template pTemplate="header">
          <tr>
            <th>Salon (TenantId)</th>
            <th>Data</th>
            <th>Wygasa</th>
            <th>Aktywny</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-r>
          <tr>
            <td class="font-mono text-xs">{{ r.tenantId }}</td>
            <td>{{ formatDate(r.redeemedAt) }}</td>
            <td>{{ r.expiresAt ? formatDate(r.expiresAt) : 'bezterminowo' }}</td>
            <td>
              @if (r.isActive) {
                <p-tag value="aktywna" severity="success" />
              } @else {
                <p-tag value="nieaktywna" severity="secondary" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td colspan="4" class="text-center py-6 text-surface-400">Brak redempcji</td></tr>
        </ng-template>
      </p-table>
    </p-dialog>
  `,
})
export class PromoCodesPageComponent {
  private readonly client = inject(AdminPromoCodesClient);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  readonly PromoCodeKind = PromoCodeKind;
  readonly PromoDiscountType = PromoDiscountType;

  // Filtrowanie listy (server-side query param isActive).
  activeFilter: 'all' | 'active' | 'inactive' = 'active';
  readonly filterOptions = [
    { label: 'Tylko aktywne', value: 'active' },
    { label: 'Tylko wyłączone', value: 'inactive' },
    { label: 'Wszystkie', value: 'all' },
  ];

  codes = rxResource({
    stream: () => {
      const flag = this.activeFilter === 'all' ? undefined : this.activeFilter === 'active';
      return this.client.list(flag);
    },
  });

  // Create dialog state
  createOpen = false;
  saving = signal(false);
  newCode: {
    code: string;
    kind: PromoCodeKind;
    discountType: PromoDiscountType;
    discountValue: number;
    durationMonths: number | undefined;
    maxTotalUses: number | undefined;
    maxUsesPerTenant: number;
    validFrom: Date;
    validUntil: Date | undefined;
    appliesTo: PromoCodeAppliesTo;
  } = this.makeBlankNewCode();

  readonly discountTypeOptions = [
    { label: 'PriceOverride (nowa cena bazy)', value: PromoDiscountType.PriceOverride },
    { label: 'PercentOff (% zniżki)', value: PromoDiscountType.PercentOff },
    { label: 'FreeMonths (mies. gratis)', value: PromoDiscountType.FreeMonths },
    { label: 'TrialExtension (dni trialu)', value: PromoDiscountType.TrialExtension },
  ];
  readonly kindOptions = [
    { label: 'AdminIssued', value: PromoCodeKind.AdminIssued },
    { label: 'Influencer', value: PromoCodeKind.Influencer },
    { label: 'Referral', value: PromoCodeKind.Referral },
  ];
  readonly appliesToOptions = [
    { label: 'Tylko nowi (rejestracja)', value: PromoCodeAppliesTo.NewTenantsOnly },
    { label: 'Tylko istniejący', value: PromoCodeAppliesTo.ExistingTenantsOnly },
    { label: 'Wszyscy', value: PromoCodeAppliesTo.Both },
  ];

  // Extend validity dialog state
  extendOpen = false;
  editingCode = signal<PromoCodeDto | null>(null);
  newValidUntil: Date | undefined;

  // Redemptions dialog state
  redemptionsOpen = false;
  private redemptionsCodeId = signal<string | null>(null);
  redemptions = rxResource<PromoCodeRedemptionDto[], string | null>({
    params: () => this.redemptionsCodeId(),
    stream: ({ params: id }) => id ? this.client.redemptions(id) : of([] as PromoCodeRedemptionDto[]),
  });

  // ── Actions ──────────────────────────────────────────────────────────────────────────

  openCreate(): void {
    this.newCode = this.makeBlankNewCode();
    this.createOpen = true;
  }

  submitCreate(): void {
    if (!this.newCode.code?.trim() || this.newCode.code.trim().length < 3) {
      this.toast.add({ severity: 'warn', summary: 'Walidacja', detail: 'Kod musi mieć ≥ 3 znaki.', life: 3000 });
      return;
    }
    this.saving.set(true);
    this.client.create(this.newCode).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Utworzono', detail: `Kod ${this.newCode.code.toUpperCase()} aktywny.`, life: 3000 });
        this.createOpen = false;
        this.codes.reload();
      },
      error: (err) => {
        this.toast.add({ severity: 'error', summary: 'Błąd', detail: this.extractError(err), life: 5000 });
      },
      complete: () => this.saving.set(false),
    });
  }

  openExtend(code: PromoCodeDto): void {
    this.editingCode.set(code);
    this.newValidUntil = code.validUntil ? new Date(code.validUntil) : undefined;
    this.extendOpen = true;
  }

  submitExtend(): void {
    const id = this.editingCode()?.id;
    if (!id) return;
    this.saving.set(true);
    this.client.updateValidity(id, { validUntil: this.newValidUntil }).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Zapisano', detail: 'Ważność zaktualizowana.', life: 3000 });
        this.extendOpen = false;
        this.codes.reload();
      },
      error: (err) => {
        this.toast.add({ severity: 'error', summary: 'Błąd', detail: this.extractError(err), life: 5000 });
      },
      complete: () => this.saving.set(false),
    });
  }

  openRedemptions(code: PromoCodeDto): void {
    this.editingCode.set(code);
    this.redemptionsCodeId.set(code.id ?? null);
    this.redemptionsOpen = true;
  }

  confirmDeactivate(code: PromoCodeDto): void {
    this.confirm.confirm({
      message: `Wyłączyć kod ${code.code}? Istniejące redempcje pozostają aktywne.`,
      header: 'Potwierdzenie',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Wyłącz',
      rejectLabel: 'Anuluj',
      acceptButtonProps: { severity: 'danger' },
      accept: () => this.deactivate(code),
    });
  }

  private deactivate(code: PromoCodeDto): void {
    if (!code.id) return;
    this.client.deactivate(code.id).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Wyłączono', detail: `Kod ${code.code} wyłączony.`, life: 3000 });
        this.codes.reload();
      },
      error: (err) => {
        this.toast.add({ severity: 'error', summary: 'Błąd', detail: this.extractError(err), life: 5000 });
      },
    });
  }

  // ── Labels / helpers ─────────────────────────────────────────────────────────────────

  discountTypeLabel(type: PromoDiscountType | undefined): string {
    switch (type) {
      case PromoDiscountType.PriceOverride: return 'PriceOverride';
      case PromoDiscountType.PercentOff: return 'PercentOff';
      case PromoDiscountType.FreeMonths: return 'FreeMonths';
      case PromoDiscountType.TrialExtension: return 'TrialExtension';
      default: return '—';
    }
  }

  valueLabel(c: PromoCodeDto): string {
    const v = c.discountValue ?? 0;
    switch (c.discountType) {
      case PromoDiscountType.PriceOverride: return `${v.toFixed(0)} zł`;
      case PromoDiscountType.PercentOff: return `${v.toFixed(0)}%`;
      case PromoDiscountType.FreeMonths: return `${v.toFixed(0)} mies.`;
      case PromoDiscountType.TrialExtension: return `${v.toFixed(0)} dni`;
      default: return String(v);
    }
  }

  valueFieldLabel(): string {
    switch (this.newCode.discountType) {
      case PromoDiscountType.PriceOverride: return 'Nowa cena bazy (zł)';
      case PromoDiscountType.PercentOff: return 'Procent (0–100)';
      case PromoDiscountType.FreeMonths: return 'Liczba miesięcy';
      case PromoDiscountType.TrialExtension: return 'Liczba dni';
      default: return 'Wartość';
    }
  }

  formatDate(d: Date | string | undefined): string {
    if (!d) return '—';
    return new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(d));
  }

  private makeBlankNewCode() {
    return {
      code: '',
      kind: PromoCodeKind.AdminIssued,
      discountType: PromoDiscountType.PriceOverride,
      discountValue: 49,
      durationMonths: undefined,
      maxTotalUses: 10,
      maxUsesPerTenant: 1,
      validFrom: new Date(),
      validUntil: undefined,
      appliesTo: PromoCodeAppliesTo.NewTenantsOnly,
    };
  }

  private extractError(err: unknown): string {
    if (err && typeof err === 'object') {
      const anyErr = err as { error?: { detail?: string; title?: string }; message?: string };
      return anyErr.error?.detail ?? anyErr.error?.title ?? anyErr.message ?? 'Nieoczekiwany błąd.';
    }
    return 'Nieoczekiwany błąd.';
  }
}
