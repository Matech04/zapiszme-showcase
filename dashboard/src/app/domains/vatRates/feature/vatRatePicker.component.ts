import { Component, inject, model, input } from "@angular/core";
import { rxResource } from "@angular/core/rxjs-interop";
import { VatRatesClient } from "@core/api/api-client";
import { CommonModule } from "@angular/common";
import { FormValueControl } from "@angular/forms/signals";

@Component({
  selector: 'app-vat-rate-picker',
  standalone: true,
  imports: [CommonModule],
  providers: [VatRatesClient],
  template: `
  <div class="flex flex-col gap-2 w-full">
    <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
      Stawka VAT
    </label>
    
    <div class="grid grid-cols-2 md:grid-cols-3 gap-2">
      @if (vatRates.isLoading()) {
        @for (i of [1,2,3]; track i) {
          <div class="h-12 bg-surface-100 dark:bg-surface-100 animate-pulse rounded-xl"></div>
        }
      } @else {
        @for (vatRate of vatRates.value(); track vatRate.id) {
          <button
            type="button"
            (click)="value.set(vatRate.id || null)"
            [class.ring-2]="value() === vatRate.id"
            [class.ring-primary]="value() === vatRate.id"
            [class.bg-primary-50]="value() === vatRate.id"
            [class.dark:bg-primary-900/20]="value() === vatRate.id"
            [class.border-primary]="value() === vatRate.id"
            class="flex flex-col items-center justify-center p-3 rounded-xl border border-surface-200 dark:border-surface-200 hover:border-primary transition-all duration-200 bg-surface-0 dark:bg-surface-50"
          >
            <span class="text-xs text-surface-500 dark:text-surface-400">{{ vatRate.name }}</span>
          </button>
        }
      }
    </div>

    @if (vatRates.error()) {
      <small class="text-red-500 px-1">Nie udało się pobrać stawek VAT</small>
    }
  </div>
`
})
export class VatRatePickerComponent implements FormValueControl<string | null> {
  private vatRatesService = inject(VatRatesClient);

  readonly value = model<string | null>(null);

  vatRates = rxResource({
    stream: () => this.vatRatesService.getVatRates(),
    defaultValue: []
  });
}
