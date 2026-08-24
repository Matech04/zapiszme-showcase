import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { form, FormField, required, validate } from '@angular/forms/signals';
import { Select } from 'primeng/select';
import { MultiSelect } from 'primeng/multiselect';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DestroyRef } from '@angular/core';
import { Observable, lastValueFrom } from 'rxjs';
import { MessageService } from 'primeng/api';
import { submit as formSubmit } from '@angular/forms/signals';

import {
  ServiceCategoriesClient,
  ServiceCategoryDto,
  ServicesClient,
  ServiceDto,
  VatRateDto,
  VatRatesClient,
  EmployeesClient,
  EmployeeDto,
  MediaClient,
  FileParameter,
} from '@core/api/api-client';
import { HasUnsavedChanges } from '@core/guards/has-unsaved-changes';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';

interface ServiceInterface {
  /** `null` = usługa bez kategorii (Opcja A — domyślne dla SOLO). */
  categoryId: string | null;
  vatRateId: string;
  name: string;
  amount: number;
  currency: string;
  durationInMinutes: number;
  /** Górna granica widełek cenowych; `null` = cena stała. */
  maxAmount: number | null;
  /** Dolna/górna granica przedziału czasu (do wyświetlania); `null` = czas stały. */
  durationMinMinutes: number | null;
  durationMaxMinutes: number | null;
  /** Grupa wariantów: usługi z tą samą etykietą nie mogą być razem w jednej wizycie-combo. Pusta = brak grupy. */
  comboGroup: string;
  /** Lista ID pracowników; pusta = nikt nie świadczy (solo-aware UI zaznacza auto). */
  employeeIds: string[];
  /** Ukrywa cenę w publicznej rezerwacji (np. „cena na zapytanie"). */
  hidePrice: boolean;
  /** Usługa dodatkowa (combo): dobierana do usługi głównej, zwykle 0 min. */
  isAddon: boolean;
  /** ID dodatków, które można dobrać do TEJ usługi głównej (puste, gdy isAddon=true). */
  addonServiceIds: string[];
  /** Opis usługi pokazywany klientce w publicznej rezerwacji. Pusty = brak. */
  description: string;
}

/** Zdjęcie galerii w formularzu — URL-e + klucz storage (po uploadzie przez MediaClient). */
interface ServiceImageItem {
  url: string;
  thumbnailUrl: string;
  key: string;
}

const PRICE_CHIPS = [50, 80, 100, 120, 150, 200];
const DURATION_CHIPS = [30, 45, 60, 90, 120];
const MAX_IMAGES = 5;
const MAX_DESCRIPTION = 2000;
// Limit i formaty zgodne z backendem (ImageProcessing.MaxInputBytes = 5 MB, magic-bytes JPG/PNG).
// Walidujemy po stronie klienta, by dać natychmiastowy, czytelny komunikat zamiast błędu z serwera.
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const ACCEPTED_IMAGE_TYPES = ['image/jpeg', 'image/png'];

/**
 * Content komponent formularza usługi — bez nagłówka/footera. Renderowany w drawerze
 * przez {@link ServiceFormDrawerComponent}. Formularz emituje (saved)/(cancelled);
 * zapis i powiadomienia w toaście robi sam, nawigacją zarządza wywołujący.
 *
 * Mobile-first, progresywne ujawnianie — esencja (Nazwa + Cena + Czas) zawsze widoczna,
 * reszta w zwijanych sekcjach:
 *  - domyślnie zamknięte przy tworzeniu (szybka ścieżka SOLO),
 *  - auto-otwierane przy edycji, gdy zawierają dane,
 *  - auto-otwierane przy błędzie walidacji dotyczącym danej sekcji.
 *
 * Stałe doświadczenie dla SOLO:
 *  - VAT z domyślnym pierwszym dostępnym („zw." preferowane),
 *  - jeśli jest dokładnie 1 pracownik → auto-przypisany i picker ukryty,
 *  - chipy szybkiego wyboru ceny i czasu.
 */
@Component({
  selector: 'app-service-form',
  standalone: true,
  imports: [CommonModule, FormsModule, FormFieldComponent, FormField, Select, MultiSelect, ToggleSwitch],
  providers: [VatRatesClient, EmployeesClient, ServiceCategoriesClient, MediaClient],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form
      (ngSubmit)="onSubmit()"
      class="flex flex-col gap-5"
      data-testid="service-form"
    >
      <!-- ── ESENCJA: nazwa + cena + czas (zawsze widoczne) ─────────────────── -->
      <!-- Wrapper niesie kotwicę przewodnika „Dodajmy usługę"; klasy powielają układ
           formularza, żeby odstępy między polami zostały bez zmian. -->
      <div class="flex flex-col gap-5" data-tour="service-essentials">
      <app-form-field
        label="Nazwa usługi"
        id="name"
        placeholder="np. Manicure hybrydowy"
        [formField]="serviceForm.name"
      />

      <div class="flex flex-col gap-2">
        <app-form-field
          label="Cena (PLN)"
          id="price"
          type="number"
          placeholder="0.00"
          [formField]="serviceForm.amount"
        />
        <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
          Wpisz <strong>0</strong>, jeśli usługa jest bezpłatna.
        </p>
        <div class="flex flex-wrap gap-2" data-testid="service-form-price-chips">
          @for (chip of priceChips; track chip) {
            <button
              type="button"
              (click)="setPrice(chip)"
              [class.ring-2]="serviceModel().amount === chip"
              [class.ring-primary]="serviceModel().amount === chip"
              [class.bg-primary-50]="serviceModel().amount === chip"
              [class.dark:bg-primary-900/20]="serviceModel().amount === chip"
              [class.text-primary]="serviceModel().amount === chip"
              [class.border-primary]="serviceModel().amount === chip"
              class="px-3 py-1.5 rounded-full border border-surface-200 dark:border-surface-200 text-sm font-bold hover:border-primary transition-colors bg-surface-0 dark:bg-surface-50 text-surface-700 dark:text-surface-300"
            >
              {{ chip }} zł
            </button>
          }
        </div>
      </div>

      <div class="flex flex-col gap-2">
        <app-form-field
          label="Czas rezerwacji (min)"
          id="duration"
          type="number"
          placeholder="60"
          [formField]="serviceForm.durationInMinutes"
        />
        <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
          Tyle czasu rezerwacja zablokuje w kalendarzu.
        </p>
        <div class="flex flex-wrap gap-2" data-testid="service-form-duration-chips">
          @for (chip of durationChips; track chip) {
            <button
              type="button"
              (click)="setDuration(chip)"
              [class.ring-2]="serviceModel().durationInMinutes === chip"
              [class.ring-primary]="serviceModel().durationInMinutes === chip"
              [class.bg-primary-50]="serviceModel().durationInMinutes === chip"
              [class.dark:bg-primary-900/20]="serviceModel().durationInMinutes === chip"
              [class.text-primary]="serviceModel().durationInMinutes === chip"
              [class.border-primary]="serviceModel().durationInMinutes === chip"
              class="px-3 py-1.5 rounded-full border border-surface-200 dark:border-surface-200 text-sm font-bold hover:border-primary transition-colors bg-surface-0 dark:bg-surface-50 text-surface-700 dark:text-surface-300"
            >
              {{ chip }} min
            </button>
          }
        </div>
      </div>
      </div>
      <!-- ── koniec esencji ─────────────────────────────────────────────────── -->

      <!-- ── Pracownicy (kontekstowo, poza akordeonami) ─────────────────────── -->
      @if (employeePickerMode() === 'multi') {
        <div class="flex flex-col gap-2" data-testid="service-form-employees-multi">
          <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
            Kto świadczy tę usługę?
          </label>
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
            @for (employee of employees.value(); track employee.id) {
              <button
                type="button"
                [attr.aria-pressed]="isEmployeeSelected(employee.id!)"
                (click)="toggleEmployee(employee.id!)"
                [class.ring-2]="isEmployeeSelected(employee.id!)"
                [class.ring-primary]="isEmployeeSelected(employee.id!)"
                [class.bg-primary-50]="isEmployeeSelected(employee.id!)"
                [class.dark:bg-primary-900/20]="isEmployeeSelected(employee.id!)"
                [class.border-primary]="isEmployeeSelected(employee.id!)"
                class="flex items-center gap-3 p-3 rounded-xl border border-surface-200 dark:border-surface-200 hover:border-primary transition-all bg-surface-0 dark:bg-surface-50 text-left"
              >
                <span
                  class="flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2"
                  [class.border-primary]="isEmployeeSelected(employee.id!)"
                  [class.bg-primary]="isEmployeeSelected(employee.id!)"
                  [class.border-surface-300]="!isEmployeeSelected(employee.id!)"
                >
                  @if (isEmployeeSelected(employee.id!)) {
                    <svg viewBox="0 0 20 20" fill="white" class="h-3.5 w-3.5">
                      <path fill-rule="evenodd" d="M16.7 5.3a1 1 0 010 1.4l-8 8a1 1 0 01-1.4 0l-4-4a1 1 0 011.4-1.4L8 12.6l7.3-7.3a1 1 0 011.4 0z" clip-rule="evenodd" />
                    </svg>
                  }
                </span>
                <span class="text-sm font-semibold text-surface-900">
                  {{ employee.firstName }} {{ employee.lastName }}
                </span>
              </button>
            }
          </div>
        </div>
      } @else if (employeePickerMode() === 'solo') {
        <div class="flex items-center gap-2 px-1 text-xs text-surface-500 dark:text-surface-400"
             data-testid="service-form-employees-solo">
          <i class="pi pi-check-circle text-primary text-sm"></i>
          <span>Świadczy: <strong class="font-semibold text-surface-700 dark:text-surface-300">{{ soloEmployeeName() }}</strong> — przypisane automatycznie.</span>
        </div>
      } @else if (employeePickerMode() === 'none') {
        <div class="flex items-center gap-3 p-3 rounded-xl bg-amber-50 dark:bg-amber-900/20 border border-amber-300/40"
             data-testid="service-form-employees-empty">
          <i class="pi pi-exclamation-triangle text-amber-600 dark:text-amber-400"></i>
          <span class="text-sm text-surface-700 dark:text-surface-900">
            Brak pracowników. Dodaj siebie w sekcji „Pracownicy", aby usługa pojawiła się w rezerwacjach.
          </span>
        </div>
      }

      <!-- ── SEKCJE OPCJONALNE (progresywne ujawnianie) ─────────────────────── -->
      <div class="flex flex-col gap-3 pt-1">

        <!-- Zdjęcia i opis -->
        <details
          class="group rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 overflow-hidden"
          data-testid="service-form-appearance"
          [open]="appearanceOpen()"
        >
          <summary class="flex items-center justify-between gap-3 cursor-pointer px-4 py-3 select-none list-none [&::-webkit-details-marker]:hidden">
            <span class="flex items-center gap-2.5 text-sm font-bold text-surface-800 dark:text-surface-300">
              <i class="pi pi-image text-surface-400"></i>
              Zdjęcia i opis
            </span>
            <i class="pi pi-chevron-down text-surface-400 text-xs transition-transform duration-300 group-open:rotate-180"></i>
          </summary>
          <div class="px-4 pb-4 pt-1 flex flex-col gap-4 border-t border-surface-100 dark:border-surface-100">

            <div class="flex flex-col gap-2" data-testid="service-form-images">
              <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                Zdjęcia ({{ images().length }}/{{ maxImages }})
              </label>
              <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
                Pierwsze zdjęcie będzie okładką. Maks. {{ maxImages }} zdjęć, do 5&nbsp;MB każde (JPG/PNG).
              </p>

              @if (images().length > 0) {
                <div class="grid grid-cols-3 sm:grid-cols-4 gap-2" data-testid="service-images-grid">
                  @for (img of images(); track img.key; let i = $index) {
                    <div
                      class="group/img relative aspect-square overflow-hidden rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-100 dark:bg-surface-100"
                      [attr.data-testid]="'service-image-' + i"
                    >
                      <img [src]="img.thumbnailUrl" alt="" class="h-full w-full object-cover" />
                      @if (i === 0) {
                        <span class="absolute left-1 top-1 rounded-md bg-primary px-1.5 py-0.5 text-[10px] font-black uppercase text-white">
                          Okładka
                        </span>
                      }
                      <div class="absolute inset-x-0 bottom-0 flex items-center justify-between gap-1 bg-black/45 px-1 py-1 opacity-0 transition group-hover/img:opacity-100">
                        <button
                          type="button"
                          [disabled]="i === 0"
                          (click)="moveImage(i, -1)"
                          [attr.data-testid]="'service-image-left-' + i"
                          class="grid size-6 place-items-center rounded text-white disabled:opacity-30 hover:bg-white/20"
                          aria-label="Przesuń w lewo"
                        >
                          <i class="pi pi-chevron-left text-xs"></i>
                        </button>
                        <button
                          type="button"
                          (click)="removeImage(i)"
                          [attr.data-testid]="'service-image-remove-' + i"
                          class="grid size-6 place-items-center rounded text-white hover:bg-red-500/80"
                          aria-label="Usuń zdjęcie"
                        >
                          <i class="pi pi-trash text-xs"></i>
                        </button>
                        <button
                          type="button"
                          [disabled]="i === images().length - 1"
                          (click)="moveImage(i, 1)"
                          [attr.data-testid]="'service-image-right-' + i"
                          class="grid size-6 place-items-center rounded text-white disabled:opacity-30 hover:bg-white/20"
                          aria-label="Przesuń w prawo"
                        >
                          <i class="pi pi-chevron-right text-xs"></i>
                        </button>
                      </div>
                    </div>
                  }
                </div>
              }

              @if (images().length < maxImages) {
                <label
                  class="flex cursor-pointer items-center justify-center gap-2 rounded-xl border border-dashed border-surface-300 dark:border-surface-300 bg-surface-0 dark:bg-surface-50 px-4 py-3 text-sm font-bold text-surface-600 dark:text-surface-700 hover:border-primary transition-colors"
                  [class.opacity-50]="uploading()"
                  [class.pointer-events-none]="uploading()"
                  data-testid="service-image-upload"
                >
                  @if (uploading()) {
                    <i class="pi pi-spin pi-spinner"></i>
                    <span>Wgrywanie…</span>
                  } @else {
                    <i class="pi pi-upload"></i>
                    <span>Dodaj zdjęcie</span>
                  }
                  <input
                    type="file"
                    accept="image/jpeg,image/png"
                    class="hidden"
                    [disabled]="uploading()"
                    (change)="onImageSelected($event)"
                  />
                </label>
              }
            </div>

            <div class="flex flex-col gap-2" data-testid="service-form-description">
              <app-form-field
                label="Opis usługi"
                id="description"
                placeholder="np. Manicure hybrydowy z pełną stylizacją…"
                [multiline]="true"
                [rows]="4"
                [formField]="serviceForm.description"
              />
              <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
                Widoczny dla klientki w rezerwacji online. {{ descriptionLength() }}/{{ maxDescription }} znaków.
              </p>
            </div>
          </div>
        </details>

        <!-- Więcej opcji ceny i czasu -->
        <details
          class="group rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 overflow-hidden"
          data-testid="service-form-pricing"
          [open]="pricingOpen()"
        >
          <summary class="flex items-center justify-between gap-3 cursor-pointer px-4 py-3 select-none list-none [&::-webkit-details-marker]:hidden">
            <span class="flex items-center gap-2.5 text-sm font-bold text-surface-800 dark:text-surface-300">
              <i class="pi pi-sliders-h text-surface-400"></i>
              Więcej opcji ceny i czasu
            </span>
            <i class="pi pi-chevron-down text-surface-400 text-xs transition-transform duration-300 group-open:rotate-180"></i>
          </summary>
          <div class="px-4 pb-4 pt-1 flex flex-col gap-4 border-t border-surface-100 dark:border-surface-100">

            <div class="flex flex-col gap-2">
              <div class="grid grid-cols-2 gap-2 p-1 rounded-2xl bg-surface-100 dark:bg-surface-800" data-testid="service-form-price-mode">
                <button
                  type="button"
                  data-testid="price-mode-fixed"
                  (click)="setPriceRange(false)"
                  [class.bg-surface-0]="!isPriceRange()"
                  [class.dark:bg-surface-200]="!isPriceRange()"
                  [class.shadow-sm]="!isPriceRange()"
                  [class.text-surface-900]="!isPriceRange()"
                  [class.dark:text-surface-900]="!isPriceRange()"
                  class="px-4 py-2.5 rounded-xl text-sm font-bold transition-all text-surface-500 dark:text-surface-400"
                >
                  Cena stała
                </button>
                <button
                  type="button"
                  data-testid="price-mode-range"
                  (click)="setPriceRange(true)"
                  [class.bg-surface-0]="isPriceRange()"
                  [class.dark:bg-surface-200]="isPriceRange()"
                  [class.shadow-sm]="isPriceRange()"
                  [class.text-surface-900]="isPriceRange()"
                  [class.dark:text-surface-900]="isPriceRange()"
                  class="px-4 py-2.5 rounded-xl text-sm font-bold transition-all text-surface-500 dark:text-surface-400"
                >
                  Widełki (od–do)
                </button>
              </div>

              @if (isPriceRange()) {
                <app-form-field
                  label="Cena do (PLN)"
                  id="maxAmount"
                  type="number"
                  placeholder="np. 150"
                  [formField]="serviceForm.maxAmount"
                />
                <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
                  Klientka zobaczy zakres „od–do". Ostateczną kwotę wpiszesz na wizycie.
                </p>
              }

              <div
                class="flex items-center justify-between gap-4 mt-1 p-3 rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50"
                data-testid="service-form-hide-price"
              >
                <div class="flex flex-col min-w-0">
                  <span class="text-sm font-bold text-surface-700 dark:text-surface-300">Nie pokazuj ceny</span>
                  <span class="text-xs text-surface-500 dark:text-surface-400">
                    W rezerwacji online klientka zobaczy „cena ukryta" zamiast kwoty.
                  </span>
                </div>
                <p-toggleswitch
                  data-testid="hide-price-toggle"
                  styleClass="shrink-0"
                  [ngModel]="serviceModel().hidePrice"
                  (ngModelChange)="setHidePrice($event)"
                  [ngModelOptions]="{ standalone: true }"
                />
              </div>
            </div>

            <div class="flex flex-col gap-2">
              <div class="grid grid-cols-2 gap-2 p-1 rounded-2xl bg-surface-100 dark:bg-surface-800" data-testid="service-form-duration-mode">
                <button
                  type="button"
                  data-testid="duration-mode-fixed"
                  (click)="setDurationRange(false)"
                  [class.bg-surface-0]="!isDurationRange()"
                  [class.dark:bg-surface-200]="!isDurationRange()"
                  [class.shadow-sm]="!isDurationRange()"
                  [class.text-surface-900]="!isDurationRange()"
                  [class.dark:text-surface-900]="!isDurationRange()"
                  class="px-4 py-2.5 rounded-xl text-sm font-bold transition-all text-surface-500 dark:text-surface-400"
                >
                  Czas stały
                </button>
                <button
                  type="button"
                  data-testid="duration-mode-range"
                  (click)="setDurationRange(true)"
                  [class.bg-surface-0]="isDurationRange()"
                  [class.dark:bg-surface-200]="isDurationRange()"
                  [class.shadow-sm]="isDurationRange()"
                  [class.text-surface-900]="isDurationRange()"
                  [class.dark:text-surface-900]="isDurationRange()"
                  class="px-4 py-2.5 rounded-xl text-sm font-bold transition-all text-surface-500 dark:text-surface-400"
                >
                  Przedział (min–max)
                </button>
              </div>

              @if (isDurationRange()) {
                <div class="grid grid-cols-2 gap-3">
                  <app-form-field
                    label="Czas od (min)"
                    id="durationMin"
                    type="number"
                    placeholder="60"
                    [formField]="serviceForm.durationMinMinutes"
                  />
                  <app-form-field
                    label="Czas do (min)"
                    id="durationMax"
                    type="number"
                    placeholder="90"
                    [formField]="serviceForm.durationMaxMinutes"
                  />
                </div>
                <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
                  Pokazywane klientce jako orientacyjny zakres. Kalendarz blokuje „czas rezerwacji" powyżej.
                </p>
              }
            </div>
          </div>
        </details>

        <!-- Kategoria i dodatki -->
        <details
          class="group rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 overflow-hidden"
          data-testid="service-form-categorization"
          [open]="categoryAddonsOpen()"
        >
          <summary class="flex items-center justify-between gap-3 cursor-pointer px-4 py-3 select-none list-none [&::-webkit-details-marker]:hidden">
            <span class="flex items-center gap-2.5 text-sm font-bold text-surface-800 dark:text-surface-300">
              <i class="pi pi-tags text-surface-400"></i>
              Kategoria i dodatki
            </span>
            <i class="pi pi-chevron-down text-surface-400 text-xs transition-transform duration-300 group-open:rotate-180"></i>
          </summary>
          <div class="px-4 pb-4 pt-1 flex flex-col gap-4 border-t border-surface-100 dark:border-surface-100">

            <div class="flex flex-col gap-2">
              <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                Kategoria
              </label>
              <div class="grid grid-cols-2 gap-2 p-1 rounded-2xl bg-surface-100 dark:bg-surface-100">
                <button
                  type="button"
                  data-testid="categorization-none"
                  (click)="setCategorized(false)"
                  [class.bg-surface-0]="!isCategorized()"
                  [class.dark:bg-surface-50]="!isCategorized()"
                  [class.shadow-sm]="!isCategorized()"
                  [class.text-surface-900]="!isCategorized()"
                  [class.dark:text-surface-900]="!isCategorized()"
                  class="px-4 py-2.5 rounded-xl text-sm font-bold transition-all text-surface-500 dark:text-surface-400"
                >
                  Bez kategorii
                </button>
                <button
                  type="button"
                  data-testid="categorization-in"
                  (click)="setCategorized(true)"
                  [class.bg-surface-0]="isCategorized()"
                  [class.dark:bg-surface-50]="isCategorized()"
                  [class.shadow-sm]="isCategorized()"
                  [class.text-surface-900]="isCategorized()"
                  [class.dark:text-surface-900]="isCategorized()"
                  class="px-4 py-2.5 rounded-xl text-sm font-bold transition-all text-surface-500 dark:text-surface-400"
                >
                  W kategorii
                </button>
              </div>

              @if (categoryResetNotice()) {
                <p
                  class="text-xs text-amber-600 dark:text-amber-400 px-1"
                  data-testid="category-reset-notice"
                >
                  Poprzednia kategoria tej usługi została usunięta — ustawiono „Bez kategorii".
                  Wybierz nową kategorię lub po prostu zapisz.
                </p>
              }

              @if (isCategorized()) {
                @if (categories.isLoading()) {
                  <div class="h-10 bg-surface-100 dark:bg-surface-100 animate-pulse rounded-xl"></div>
                } @else if (categories.value().length === 0) {
                  <p class="text-sm text-surface-500 dark:text-surface-400 italic px-1">
                    Brak zdefiniowanych kategorii. Możesz najpierw utworzyć kategorię lub zostawić usługę bez.
                  </p>
                } @else {
                  <p-select
                    data-testid="category-select"
                    [options]="categoryOptions()"
                    [ngModel]="serviceModel().categoryId"
                    (ngModelChange)="onCategoryChange($event)"
                    [ngModelOptions]="{ standalone: true }"
                    placeholder="Wybierz kategorię…"
                    [fluid]="true"
                    appendTo="body"
                    styleClass="w-full"
                  />
                }
              }
            </div>

            <div class="flex flex-col gap-2" data-testid="service-form-addons">
              <div
                class="flex items-center justify-between gap-4 p-3 rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50"
                data-testid="service-form-is-addon"
              >
                <div class="flex flex-col min-w-0">
                  <span class="text-sm font-bold text-surface-700 dark:text-surface-300">To jest usługa dodatkowa</span>
                  <span class="text-xs text-surface-500 dark:text-surface-400">
                    Dobierana do usługi głównej, nie rezerwowana samodzielnie.
                  </span>
                </div>
                <p-toggleswitch
                  data-testid="is-addon-toggle"
                  styleClass="shrink-0"
                  [ngModel]="serviceModel().isAddon"
                  (ngModelChange)="setIsAddon($event)"
                  [ngModelOptions]="{ standalone: true }"
                />
              </div>

              @if (!serviceModel().isAddon) {
                <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                  Dostępne dodatki
                </label>
                @if (addonOptions().length === 0) {
                  <p class="text-sm text-surface-500 dark:text-surface-400 italic px-1">
                    Brak usług oznaczonych jako dodatek. Utwórz usługę i zaznacz „To jest usługa dodatkowa".
                  </p>
                } @else {
                  <p-multiselect
                    data-testid="addons-multiselect"
                    [options]="addonOptions()"
                    [ngModel]="serviceModel().addonServiceIds"
                    (ngModelChange)="setAddonServiceIds($event)"
                    [ngModelOptions]="{ standalone: true }"
                    placeholder="Wybierz dodatki…"
                    [fluid]="true"
                    appendTo="body"
                    styleClass="w-full"
                  />
                  <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
                    Te dodatki klient będzie mógł dobrać do tej usługi w rezerwacji.
                  </p>
                }
              }
            </div>
          </div>
        </details>

        <!-- Zaawansowane: VAT + grupa -->
        <details
          class="group rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 overflow-hidden"
          data-testid="service-form-advanced"
          [open]="advancedOpen()"
        >
          <summary class="flex items-center justify-between gap-3 cursor-pointer px-4 py-3 select-none list-none [&::-webkit-details-marker]:hidden">
            <span class="flex items-center gap-2.5 text-sm font-bold text-surface-800 dark:text-surface-300">
              <i class="pi pi-cog text-surface-400"></i>
              Zaawansowane (VAT, grupa)
            </span>
            <i class="pi pi-chevron-down text-surface-400 text-xs transition-transform duration-300 group-open:rotate-180"></i>
          </summary>
          <div class="px-4 pb-4 pt-1 flex flex-col gap-2 border-t border-surface-100 dark:border-surface-100">
            <label class="mt-2 text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
              Stawka VAT
            </label>
            @if (vatRates.isLoading()) {
              <div class="grid grid-cols-3 gap-2">
                @for (i of [1,2,3]; track i) {
                  <div class="h-10 bg-surface-100 dark:bg-surface-100 animate-pulse rounded-xl"></div>
                }
              </div>
            } @else {
              <div class="grid grid-cols-3 gap-2" data-testid="vat-rate-options">
                @for (vat of vatRates.value(); track vat.id) {
                  <button
                    type="button"
                    (click)="setVatRate(vat.id!)"
                    [class.ring-2]="serviceModel().vatRateId === vat.id"
                    [class.ring-primary]="serviceModel().vatRateId === vat.id"
                    [class.bg-primary-50]="serviceModel().vatRateId === vat.id"
                    [class.dark:bg-primary-900/20]="serviceModel().vatRateId === vat.id"
                    [class.border-primary]="serviceModel().vatRateId === vat.id"
                    class="px-3 py-2 rounded-xl border border-surface-200 dark:border-surface-200 hover:border-primary transition-all bg-surface-0 dark:bg-surface-50 text-sm"
                  >
                    {{ vat.name }}
                  </button>
                }
              </div>
            }

            <label class="mt-2 text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
              Grupa
            </label>
            <app-form-field
              label=""
              id="comboGroup"
              placeholder="np. Przedłużanie (dla rozmiarów S/M/L/XL)"
              [formField]="serviceForm.comboGroup"
            />
            <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
              Usługi w tej samej grupie nie mogą być razem wybrane przy jednej rezerwacji.
            </p>
          </div>
        </details>
      </div>
    </form>
  `,
})
export class ServiceForm implements HasUnsavedChanges {
  /** ID usługi (edit). Brak = tworzenie nowej. */
  readonly id = input<string>();
  /** Sugerowane categoryId przy tworzeniu (np. kliknięcie "Dodaj usługę" w karcie kategorii). */
  readonly categoryHint = input<string | null | undefined>(undefined);
  /**
   * Czy formularz jest aktualnie otwarty (drawer). Instancja komponentu jest reużywana między
   * otwarciami, więc używamy tego jako sygnału do odświeżenia listy usług (opcje dodatków) —
   * inaczej świeżo utworzony dodatek nie pojawiłby się bez przeładowania strony.
   */
  readonly active = input<boolean>(true);

  /** Emit po udanym zapisie (PUT/POST) — wywołujący zamyka drawer i odświeża listę. */
  readonly saved = output<{ id: string | undefined; isUpdate: boolean }>();
  /** Emit przy anulowaniu lub zewnętrznym żądaniu zamknięcia. */
  readonly cancelled = output<void>();

  protected readonly priceChips = PRICE_CHIPS;
  protected readonly durationChips = DURATION_CHIPS;

  private servicesClient = inject(ServicesClient);
  private vatRatesClient = inject(VatRatesClient);
  private employeesClient = inject(EmployeesClient);
  private categoriesClient = inject(ServiceCategoriesClient);
  private mediaClient = inject(MediaClient);
  private message = inject(MessageService);
  private destroyRef = inject(DestroyRef);

  protected readonly maxImages = MAX_IMAGES;
  protected readonly maxDescription = MAX_DESCRIPTION;

  protected isEdit = computed(() => !!this.id());

  /** Galeria zdjęć (poza signal-form: lista trzymana osobno, kolejność = pozycja w galerii). */
  protected images = signal<ServiceImageItem[]>([]);
  /** Trwa upload pliku przez MediaClient — blokuje wybór kolejnego. */
  protected uploading = signal(false);

  protected descriptionLength = computed(() => (this.serviceModel().description ?? '').length);

  /**
   * Stan otwarcia sekcji opcjonalnych (progresywne ujawnianie). Domyślnie zamknięte przy
   * tworzeniu; auto-otwierane przy edycji (gdy zawierają dane) i przy błędzie walidacji.
   * Kontrolowane sygnałem — użytkownik może swobodnie toggle'ować natywnym <details>,
   * a Angular nie nadpisuje stanu, dopóki sygnał się nie zmieni.
   */
  protected appearanceOpen = signal(false);
  protected pricingOpen = signal(false);
  protected categoryAddonsOpen = signal(false);
  protected advancedOpen = signal(false);

  protected serviceModel = signal<ServiceInterface>({
    categoryId: null,
    vatRateId: '',
    name: '',
    amount: 0,
    currency: 'PLN',
    durationInMinutes: 60,
    maxAmount: null,
    durationMinMinutes: null,
    durationMaxMinutes: null,
    comboGroup: '',
    employeeIds: [],
    hidePrice: false,
    isAddon: false,
    addonServiceIds: [],
    description: '',
  });

  protected serviceForm = form(this.serviceModel, (schemaPath) => {
    required(schemaPath.name, { message: 'Nazwa jest wymagana' });
    validate(schemaPath.description, (ctx) => {
      const v = ctx.value();
      if (v != null && v.length > MAX_DESCRIPTION) {
        return { kind: 'descriptionTooLong', message: `Opis może mieć maksymalnie ${MAX_DESCRIPTION} znaków` };
      }
      return null;
    });
    // NIE używamy required() dla ceny — required traktuje 0 jako wartość pustą,
    // co blokowało zapis usług bezpłatnych (0 zł). Wymagamy obecnej, nieujemnej liczby.
    validate(schemaPath.amount, (ctx) => {
      const v = ctx.value();
      if (v == null || Number.isNaN(v) || v < 0) {
        return { kind: 'invalidPrice', message: 'Podaj cenę (0 lub więcej)' };
      }
      return null;
    });
    // Tak samo jak cena: 0 jest dozwolone (usługi-dodatki). required() blokowałoby 0.
    validate(schemaPath.durationInMinutes, (ctx) => {
      const v = ctx.value();
      if (v == null || Number.isNaN(v) || v < 0) {
        return { kind: 'invalidDuration', message: 'Podaj czas (0 lub więcej)' };
      }
      return null;
    });
  });

  /** Tryb widełek cen / przedziału czasu (UI; niezależny od wartości, jak categorizedSig). */
  private priceRangeSig = signal(false);
  protected isPriceRange = computed(() => this.priceRangeSig());
  private durationRangeSig = signal(false);
  protected isDurationRange = computed(() => this.durationRangeSig());

  protected vatRates = rxResource<VatRateDto[], void>({
    stream: () => this.vatRatesClient.getVatRates(),
    defaultValue: [] as VatRateDto[],
  });

  protected employees = rxResource<EmployeeDto[], void>({
    stream: () => this.employeesClient.getEmployees(),
    defaultValue: [] as EmployeeDto[],
  });

  protected categories = rxResource<ServiceCategoryDto[], void>({
    stream: () => this.categoriesClient.getServiceCategories(),
    defaultValue: [] as ServiceCategoryDto[],
  });

  /** Wszystkie usługi — źródło opcji multi-selecta dodatków (filtrowane do isAddon). */
  protected allServices = rxResource<ServiceDto[], void>({
    stream: () => this.servicesClient.getServices(undefined),
    defaultValue: [] as ServiceDto[],
  });

  /** Opcje „Dostępne dodatki": usługi oznaczone jako dodatek (bez samej edytowanej usługi). */
  protected addonOptions = computed(() =>
    this.allServices.value()
      .filter((s) => s.isAddon && s.id !== this.id())
      .map((s) => ({ label: s.name ?? 'Dodatek', value: s.id ?? '' })),
  );

  /** Pary `{ label, value }` pod `<p-select>` — PrimeNG operuje na value, nie obiekcie. */
  protected categoryOptions = computed(() =>
    this.categories.value().map((c) => ({ label: c.name ?? 'Kategoria', value: c.id ?? '' })),
  );

  /** „solo" gdy 1 pracownik (auto-assign + ukryj picker); „none" gdy 0; „multi" gdy ≥2. */
  protected employeePickerMode = computed<'multi' | 'solo' | 'none'>(() => {
    const list = this.employees.value();
    if (list.length === 0) return 'none';
    if (list.length === 1) return 'solo';
    return 'multi';
  });

  protected soloEmployeeName = computed(() => {
    const list = this.employees.value();
    if (list.length !== 1) return '';
    const e = list[0];
    return [e.firstName, e.lastName].filter(Boolean).join(' ').trim();
  });

  /**
   * Czy formularz jest w trybie „W kategorii". Niezależne od `categoryId`, bo user
   * może najpierw kliknąć segmented control, a dopiero potem wybrać konkretną
   * kategorię z dropdownu — przez ten okres categoryId pozostaje null.
   */
  private categorizedSig = signal(false);
  protected isCategorized = computed(() => this.categorizedSig());

  /**
   * Notka: edytowana usługa wskazywała kategorię, której już nie ma (kategoria
   * usunięta — „ghost service"). Formularz ustawił wtedy „Bez kategorii", żeby dało
   * się zapisać (UpdateService odrzuca martwe categoryId).
   */
  protected categoryResetNotice = signal(false);

  private submittedSuccessfully = signal(false);
  /** Pamiętany categoryId żeby toggle przywrócił wartość gdy user się rozmyśli. */
  private lastCategoryId: string | null = null;

  constructor() {
    // 0) Drawer otwarty → odśwież listę usług (opcje dodatków). Instancja formularza jest
    //    reużywana między otwarciami, więc bez tego świeżo utworzony dodatek nie pojawiłby się
    //    w multi-selekcie aż do przeładowania strony.
    effect(() => {
      if (this.active()) {
        this.allServices.reload();
      }
    });

    // 1) Pre-fill przy create wg hinta (np. „Dodaj usługę" w karcie kategorii).
    effect(() => {
      const hint = this.categoryHint();
      if (this.isEdit()) return;
      if (hint) {
        this.serviceModel.update((m) => ({ ...m, categoryId: hint }));
        this.lastCategoryId = hint;
        this.categorizedSig.set(true);
        // Kategoria wskazana z zewnątrz → rozwiń sekcję, by user widział wybór.
        this.categoryAddonsOpen.set(true);
      }
    });

    // 2) Edit — fetch serwisu i wypełnij model.
    effect(() => {
      const id = this.id();
      if (!id) return;
      this.servicesClient
        .getService(id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((data: any) => {
          this.lastCategoryId = data.categoryId ?? null;
          this.categorizedSig.set(data.categoryId != null);
          this.priceRangeSig.set(data.maxAmount != null);
          this.durationRangeSig.set(data.durationMinMinutes != null && data.durationMaxMinutes != null);
          this.serviceModel.set({
            name: data.name,
            amount: data.price?.amount ?? 0,
            durationInMinutes: data.durationInMinutes ?? 60,
            categoryId: data.categoryId ?? null,
            vatRateId: data.vatRateId ?? '',
            currency: data.price?.currency ?? 'PLN',
            maxAmount: data.maxAmount ?? null,
            durationMinMinutes: data.durationMinMinutes ?? null,
            durationMaxMinutes: data.durationMaxMinutes ?? null,
            comboGroup: data.comboGroup ?? '',
            employeeIds: data.employeeIds ?? [],
            hidePrice: data.hidePrice ?? false,
            isAddon: data.isAddon ?? false,
            addonServiceIds: data.addonServiceIds ?? [],
            description: data.description ?? '',
          });
          // Galeria z DTO (ServiceImageDto: url/thumbnailUrl/storageKey/orderIndex) → model formularza.
          this.images.set(
            (data.images ?? [])
              .slice()
              .sort((a: any, b: any) => (a.orderIndex ?? 0) - (b.orderIndex ?? 0))
              .map((img: any) => ({
                url: img.url ?? '',
                thumbnailUrl: img.thumbnailUrl ?? '',
                key: img.storageKey ?? '',
              })),
          );
          // Auto-otwórz sekcje, które zawierają dane — edycja bogatej usługi pokazuje
          // od razu wszystko istotne; świeży create pozostaje minimalny.
          this.appearanceOpen.set((data.images?.length ?? 0) > 0 || !!(data.description ?? '').trim());
          this.pricingOpen.set(
            data.maxAmount != null ||
              (data.durationMinMinutes != null && data.durationMaxMinutes != null) ||
              !!data.hidePrice,
          );
          this.categoryAddonsOpen.set(
            data.categoryId != null || !!data.isAddon || (data.addonServiceIds?.length ?? 0) > 0,
          );
          this.advancedOpen.set(!!(data.comboGroup ?? '').trim());
        });
    });

    // 3) Domyślny VAT przy CREATE — preferowanie „zw." (zwolnienie podmiotowe,
    //    najczęstsze u solo-tenantów w branży beauty). Kolejność wyboru:
    //    a) stawka o nazwie zaczynającej się od „zw" (case-insensitive)
    //    b) stawka z flagą isDefault (backend katalog seeduje „zw." z IsDefault=true)
    //    c) pierwsza z listy — last resort, gdy tenant skasował domyślne stawki
    effect(() => {
      const rates = this.vatRates.value();
      if (this.isEdit()) return;
      if (!rates.length) return;
      const current = this.serviceModel().vatRateId;
      if (current) return;
      const byName = rates.find((r) => (r.name ?? '').trim().toLowerCase().startsWith('zw'));
      const byDefault = rates.find((r) => r.isDefault);
      const picked = (byName ?? byDefault ?? rates[0]).id;
      if (picked) this.serviceModel.update((m) => ({ ...m, vatRateId: picked }));
    });

    // 4) Solo: auto-assign jedynego pracownika przy CREATE.
    effect(() => {
      const list = this.employees.value();
      if (this.isEdit()) return;
      if (list.length !== 1) return;
      const onlyId = list[0].id;
      if (!onlyId) return;
      const current = this.serviceModel().employeeIds;
      if (current.length === 0) {
        this.serviceModel.update((m) => ({ ...m, employeeIds: [onlyId] }));
      }
    });

    // 5) Reconcyliacja „martwej" kategorii (ghost-service po usunięciu kategorii):
    //    jeśli usługa wskazuje categoryId spoza aktywnych kategorii, ustaw „Bez
    //    kategorii" — inaczej UpdateService odrzuciłby zapis martwym categoryId
    //    (NotFound), blokując edycję nawet samej nazwy. User może wybrać nową.
    effect(() => {
      if (this.categories.isLoading()) return;
      const catId = this.serviceModel().categoryId;
      if (!catId) return;
      if (this.categories.value().some((c) => c.id === catId)) return;
      // categoryId nie istnieje wśród aktywnych → odepnij do „Bez kategorii".
      this.lastCategoryId = null;
      this.categorizedSig.set(false);
      this.categoryResetNotice.set(true);
      // Rozwiń sekcję kategorii, by user zobaczył notkę i mógł wybrać nową.
      this.categoryAddonsOpen.set(true);
      this.serviceModel.update((m) => ({ ...m, categoryId: null }));
    });
  }

  protected setPrice(v: number) {
    this.serviceModel.update((m) => ({ ...m, amount: v }));
  }

  protected setDuration(v: number) {
    this.serviceModel.update((m) => ({ ...m, durationInMinutes: v }));
  }

  protected setVatRate(id: string) {
    this.serviceModel.update((m) => ({ ...m, vatRateId: id }));
  }

  protected setHidePrice(on: boolean) {
    this.serviceModel.update((m) => ({ ...m, hidePrice: on }));
  }

  protected setIsAddon(on: boolean) {
    // Dodatek nie ma własnych dodatków — czyścimy listę przy włączeniu.
    this.serviceModel.update((m) => ({ ...m, isAddon: on, addonServiceIds: on ? [] : m.addonServiceIds }));
  }

  protected setAddonServiceIds(ids: string[]) {
    this.serviceModel.update((m) => ({ ...m, addonServiceIds: ids ?? [] }));
  }

  /** Wybór pliku → upload przez MediaClient (POST /api/media/images) → dopisz URL-e do galerii. */
  protected async onImageSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Reset inputu, by ten sam plik dało się wybrać ponownie po usunięciu.
    input.value = '';
    if (!file) return;
    if (this.images().length >= MAX_IMAGES) {
      this.message.add({ severity: 'warn', summary: 'Limit zdjęć', detail: `Maksymalnie ${MAX_IMAGES} zdjęć.` });
      return;
    }

    // Walidacja PRZED uploadem — natychmiastowy, jasny komunikat zamiast błędu z serwera
    // (zwłaszcza dla dużych zdjęć z telefonu, które inaczej wracały generycznym błędem).
    if (!ACCEPTED_IMAGE_TYPES.includes(file.type)) {
      this.message.add({
        severity: 'warn',
        summary: 'Nieobsługiwany format',
        detail: 'Dozwolone formaty zdjęć to JPG i PNG.',
      });
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      const mb = (file.size / 1024 / 1024).toFixed(1);
      this.message.add({
        severity: 'warn',
        summary: 'Zdjęcie za duże',
        detail: `To zdjęcie ma ${mb} MB. Maksymalny rozmiar to 5 MB — zmniejsz je i spróbuj ponownie.`,
      });
      return;
    }

    this.uploading.set(true);
    const param: FileParameter = { data: file, fileName: file.name };
    try {
      const res = await lastValueFrom(this.mediaClient.uploadImage(param));
      this.images.update((list) => [
        ...list,
        { url: res.url ?? '', thumbnailUrl: res.thumbnailUrl ?? '', key: res.key ?? '' },
      ]);
    } catch {
      // Toast z czytelnym komunikatem (m.in. image.too_large / image.unsupported_format)
      // pokazuje errorInterceptor na podstawie errorCode/detail z odpowiedzi — nie dublujemy go tutaj.
    } finally {
      this.uploading.set(false);
    }
  }

  protected removeImage(index: number) {
    this.images.update((list) => list.filter((_, i) => i !== index));
  }

  /** Przesuwa zdjęcie o `delta` (-1 w lewo / +1 w prawo); pierwsze = okładka. */
  protected moveImage(index: number, delta: number) {
    this.images.update((list) => {
      const next = list.slice();
      const target = index + delta;
      if (target < 0 || target >= next.length) return list;
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  protected setPriceRange(on: boolean) {
    this.priceRangeSig.set(on);
    // Wyłączenie widełek czyści górną granicę (cena stała).
    if (!on) this.serviceModel.update((m) => ({ ...m, maxAmount: null }));
  }

  protected setDurationRange(on: boolean) {
    this.durationRangeSig.set(on);
    // Wyłączenie przedziału czyści obie granice (czas stały).
    if (!on) this.serviceModel.update((m) => ({ ...m, durationMinMinutes: null, durationMaxMinutes: null }));
  }

  protected setCategorized(on: boolean) {
    this.categoryResetNotice.set(false);
    this.categorizedSig.set(on);
    this.serviceModel.update((m) => {
      if (on) {
        // Włącz „W kategorii": przywróć ostatnio znaną lub zostaw null do wyboru z dropdownu.
        return { ...m, categoryId: this.lastCategoryId ?? null };
      }
      // Wyłącz: zapamiętaj poprzedni wybór, wyzeruj.
      if (m.categoryId) this.lastCategoryId = m.categoryId;
      return { ...m, categoryId: null };
    });
  }

  protected onCategoryChange(value: string | null) {
    this.categoryResetNotice.set(false);
    const next = value || null;
    this.lastCategoryId = next;
    this.serviceModel.update((m) => ({ ...m, categoryId: next }));
  }

  protected isEmployeeSelected(id: string): boolean {
    return this.serviceModel().employeeIds.includes(id);
  }

  protected toggleEmployee(id: string) {
    this.serviceModel.update((m) => ({
      ...m,
      employeeIds: m.employeeIds.includes(id)
        ? m.employeeIds.filter((x) => x !== id)
        : [...m.employeeIds, id],
    }));
  }

  /** Wywoływane z drawera (przycisk „Zapisz" w footerze). */
  async onSubmit() {
    const tree = this.serviceForm;
    await formSubmit(tree, async () => {
      const t = tree();
      if (t.invalid() || t.pending()) return;
      const id = this.id();
      const value = t.value();
      if (!value.vatRateId) {
        // VAT żyje w sekcji „Zaawansowane" — rozwiń, by user zobaczył pole.
        this.advancedOpen.set(true);
        this.message.add({
          severity: 'warn',
          summary: 'Stawka VAT wymagana',
          detail: 'Sprawdź sekcję „Zaawansowane (VAT, grupa)".',
        });
        return;
      }
      if (this.isCategorized() && !value.categoryId) {
        this.categoryAddonsOpen.set(true);
        this.message.add({
          severity: 'warn',
          summary: 'Wybierz kategorię',
          detail: 'Wybierz kategorię z listy lub przełącz na „Bez kategorii".',
        });
        return;
      }

      // Widełki cen: górna granica wymagana i większa od ceny bazowej.
      if (this.isPriceRange() && (value.maxAmount == null || value.maxAmount <= value.amount)) {
        this.pricingOpen.set(true);
        this.message.add({
          severity: 'warn',
          summary: 'Sprawdź widełki ceny',
          detail: 'Podaj cenę „do" większą niż cena bazowa.',
        });
        return;
      }

      // Przedział czasu: oba końce wymagane, dodatnie, min ≤ max.
      if (this.isDurationRange()) {
        const min = value.durationMinMinutes;
        const max = value.durationMaxMinutes;
        if (min == null || max == null || min <= 0 || max <= 0 || min > max) {
          this.pricingOpen.set(true);
          this.message.add({
            severity: 'warn',
            summary: 'Sprawdź przedział czasu',
            detail: 'Podaj czas „od" i „do" (od ≤ do).',
          });
          return;
        }
      }

      // Wyłączone tryby → wysyłamy null (cena/czas stały), nawet gdyby został stary wpis.
      const payload = {
        ...value,
        id,
        maxAmount: this.isPriceRange() ? value.maxAmount : null,
        durationMinMinutes: this.isDurationRange() ? value.durationMinMinutes : null,
        durationMaxMinutes: this.isDurationRange() ? value.durationMaxMinutes : null,
        comboGroup: value.comboGroup?.trim() ? value.comboGroup.trim() : null,
        // Dodatek nie ma własnych dodatków — wymuś pustą listę.
        addonServiceIds: value.isAddon ? [] : value.addonServiceIds,
        description: value.description?.trim() ? value.description.trim() : null,
        // Galeria: kolejność listy = OrderIndex w backendzie (pierwsze = okładka).
        images: this.images().map((img) => ({ url: img.url, thumbnailUrl: img.thumbnailUrl, key: img.key })),
      } as any;
      // updateService → Observable<FileResponse>, createService → Observable<string>;
      // dla naszego flow wynik nieistotny, więc rzutujemy na unknown żeby ujednolicić typ.
      const action$ = (
        id ? this.servicesClient.updateService(id, payload) : this.servicesClient.createService(payload)
      ) as Observable<unknown>;
      try {
        await lastValueFrom(action$);
        this.submittedSuccessfully.set(true);
        this.message.add({
          severity: 'success',
          summary: 'Sukces',
          detail: id ? 'Usługa zaktualizowana' : 'Usługa utworzona',
        });
        this.saved.emit({ id, isUpdate: !!id });
      } catch (err: any) {
        this.message.add({
          severity: 'error',
          summary: 'Błąd',
          detail: err?.message || 'Nie udało się zapisać usługi',
        });
      }
    });
  }

  /** Wywoływane z drawera (Anuluj / klik poza panelem). */
  onCancel() {
    this.cancelled.emit();
  }

  hasUnsavedChanges(): boolean {
    if (this.submittedSuccessfully()) return false;
    try {
      return !!this.serviceForm().dirty?.();
    } catch {
      return false;
    }
  }
}
