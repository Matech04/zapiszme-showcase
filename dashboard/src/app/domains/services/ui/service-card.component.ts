import { CommonModule } from "@angular/common";
import { Component, computed, input, output } from "@angular/core";
import { ServiceDto } from "@core/api/api-client";
import { MenuItem } from "primeng/api";
import { ButtonModule } from "primeng/button";
import { TieredMenuModule } from "primeng/tieredmenu";

/** Formatuje kwotę w polskim formacie waluty ("100,00 zł"); fallback przy egzotycznej walucie. */
function formatMoney(amount: number, currency: string | undefined): string {
  try {
    return new Intl.NumberFormat('pl-PL', {
      style: 'currency',
      currency: currency || 'PLN',
    }).format(amount);
  } catch {
    return `${amount} ${currency ?? ''}`.trim();
  }
}

@Component({
  selector: 'app-service-card',
  standalone: true,
  imports: [CommonModule, ButtonModule, TieredMenuModule],
  template: `
    <!-- Płaski wiersz cennika: nazwa, pod nią czas (lewo) + cena (prawo). -->
    <div class="group relative py-3 pl-2 pr-9 rounded-xl transition-colors
                hover:bg-surface-50/80 dark:hover:bg-surface-100/40">

      <span class="block text-base font-bold text-surface-900 group-hover:text-primary transition-colors break-words leading-snug">
        {{ service().name }}
      </span>

      <div class="mt-1 flex items-center justify-between gap-3">
        <!-- Czas trwania -->
        <span class="inline-block px-2 py-0.5 rounded-md bg-surface-100 dark:bg-surface-100
                    text-surface-600 dark:text-surface-400 text-[10px] font-bold tracking-wider uppercase shrink-0">
          @if (hasDurationRange()) {
            {{ service().durationMinMinutes }}–{{ service().durationMaxMinutes }} min
          } @else {
            {{ service().durationInMinutes }} min
          }
        </span>

        <!-- Cena (do prawej) -->
        <div class="text-right min-w-0">
          @if (service().hidePrice) {
            <span
              class="inline-flex items-center gap-1.5 text-sm font-bold text-surface-500 dark:text-surface-400"
              data-testid="service-card-price-hidden"
            >
              <i class="pi pi-eye-slash text-xs"></i>
              Cena ukryta
            </span>
          } @else if (isFree()) {
            <span
              class="text-base font-bold text-emerald-600 dark:text-emerald-400 tracking-tight"
              data-testid="service-card-price-free"
            >
              Bezpłatnie
            </span>
          } @else {
            <span
              class="text-base font-bold text-primary tracking-tight break-words"
              data-testid="service-card-price"
            >
              {{ priceLabel() }}
            </span>
          }
        </div>
      </div>

      <!-- Menu akcji: prawy górny róg (ukryte w trybie zmiany kolejności) -->
      @if (!reorderMode()) {
        <div class="absolute top-2 right-1 flex items-center">
          <p-button
            icon="pi pi-ellipsis-h"
            [text]="true"
            [plain]="true"
            [rounded]="true"
            (onClick)="menu.toggle($event)"
            styleClass="w-8 h-8 p-0 text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 hover:bg-surface-200 dark:hover:bg-surface-200 transition-colors" />

          <p-tieredmenu #menu [popup]="true" [model]="menuItems()" appendTo="body" />
        </div>
      }
    </div>
  `
})
export class ServiceCardComponent {
  service = input.required<ServiceDto>();
  /** Tryb zmiany kolejności — chowa kebab (uchwyt renderuje rodzic obok wiersza). */
  reorderMode = input<boolean>(false);

  editService = output<string | undefined>();
  deleteService = output<string | undefined>();

  // Bezpłatnie: cena bazowa = 0 i brak widełek (górna granica też 0/niezdefiniowana).
  protected isFree = computed(() => {
    const s = this.service();
    return s.price?.amount === 0 && !this.hasPriceRange();
  });

  // Widełki cenowe: maxAmount ustawione i większe od ceny bazowej.
  protected hasPriceRange = computed(() => {
    const s = this.service();
    return s.maxAmount != null && s.maxAmount > (s.price?.amount ?? 0);
  });

  // Przedział czasu: oba końce ustawione (backend gwarantuje oba-lub-żaden).
  protected hasDurationRange = computed(() => {
    const s = this.service();
    return s.durationMinMinutes != null && s.durationMaxMinutes != null;
  });

  /** Cena w polskim formacie; z widełkami "100,00 zł – 150,00 zł". */
  protected priceLabel = computed(() => {
    const s = this.service();
    const currency = s.price?.currency;
    const base = formatMoney(s.price?.amount ?? 0, currency);
    if (this.hasPriceRange()) {
      return `${base} – ${formatMoney(s.maxAmount ?? 0, currency)}`;
    }
    return base;
  });

  menuItems = computed<MenuItem[]>(() => [
    {
      label: 'Edytuj',
      icon: 'pi pi-pencil',
      command: () => this.editService.emit(this.service().id)
    },
    {
      label: 'Usuń',
      icon: 'pi pi-trash',
      styleClass: 'text-red-500',
      command: () => this.deleteService.emit(this.service().id)
    }
  ]);
}
