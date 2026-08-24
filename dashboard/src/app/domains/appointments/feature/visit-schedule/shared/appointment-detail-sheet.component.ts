import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  Renderer2,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Dialog } from 'primeng/dialog';
import { Drawer } from 'primeng/drawer';
import { InputNumber } from 'primeng/inputnumber';
import { Textarea } from 'primeng/textarea';
import { catchError, finalize, of } from 'rxjs';
import {
  AppointmentDto,
  AppointmentPreviewDto,
  AppointmentsClient,
  SetDurationRequest,
  CustomersClient,
  DepositsClient,
  GenerateDepositLinkResult,
  SetFinalPriceRequest,
} from '@core/api/api-client';
import { BodyScrollLockService } from '@core/services/body-scroll-lock.service';
import {
  APPOINTMENT_STATUS_TOKENS,
  AppointmentStatusVariant,
  statusVariantFromIdOrName,
} from '@core/theme/status-tokens';

/**
 * Bottom sheet (mobile) / right drawer (desktop) — JEDYNY ekran szczegółów wizyty. Pokazuje pełen
 * komplet danych (godzina, klient, telefon, Instagram, pracownik, cena, notatka) + szybkie akcje.
 * Podstawowe pola idą od razu z inputu `appointment()` (AppointmentPreviewDto z range-query),
 * a cena/notatka/Instagram doładowywane lazy po `id` (patrz `fullDetail`). Osobna strona
 * `/admin/appointment/:id` została usunięta — deep-linki otwierają ten drawer w kalendarzu.
 *
 * Reschedule jest disabled w F1.5 — wizard „Zaproponuj nowy termin" przychodzi w F3.1.
 */
@Component({
  selector: 'app-appointment-detail-sheet',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, Dialog, Drawer, InputNumber, Textarea],
  template: `
    <!-- Shared content body — używany zarówno w mobile drawer jak i desktop inline panel -->
    <ng-template #content let-a>
      <header class="px-5 pt-5 pb-3 border-b border-surface-200/70 dark:border-surface-200/70">
        @if (!embedded() && !isDesktop()) {
          <div class="flex justify-center pb-2 -mt-2" aria-hidden="true">
            <span class="block w-12 h-1.5 rounded-full bg-surface-300 dark:bg-surface-600"></span>
          </div>
        }
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0 flex flex-col">
            <p class="admin-section-label text-primary">Wizyta</p>
            <h2 class="text-xl font-black text-surface-900 truncate">
              {{ serviceName() }}
            </h2>
            <p class="text-xs text-surface-500 dark:text-surface-400 mt-0.5">
              {{ dateLabel(a) }}
            </p>
            <!-- Zawór bezpieczeństwa dla powiadomień, które NIE ruszają kalendarza („do
                 potwierdzenia"): kontekst grafiku jest o jedno kliknięcie, ale to użytkownik
                 decyduje, więc przeskok go nie zaskakuje. Parent pokazuje przycisk tylko gdy
                 kalendarz stoi na innym dniu/pracowniku — po kliknięciu kafelka byłby szumem.
                 Świadomie w nagłówku, nie w stopce: stopka ma pozostać niezmienna. -->
            @if (canRevealInCalendar()) {
              <button
                type="button"
                (click)="revealRequested.emit()"
                data-testid="reveal-in-calendar"
                class="self-start mt-1 -mx-1 px-1 py-0.5 rounded text-xs font-bold text-primary hover:underline transition-colors"
              >
                <i class="pi pi-calendar mr-1 text-[10px]" aria-hidden="true"></i>
                Pokaż w kalendarzu
              </button>
            }
            <!-- Status na mobile: w kolumnie pod datą (na desktopie ukryty — pokazany w prawej grupie) -->
            <span
              class="sm:hidden self-start mt-2 inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase tracking-wider"
              [class]="statusChipClass()"
            >
              <i [class]="statusIcon()" class="text-[10px]"></i>
              {{ statusLabel() }}
            </span>
          </div>
          <div class="flex items-start gap-2 shrink-0">
            <span
              class="hidden sm:inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase tracking-wider"
              [class]="statusChipClass()"
            >
              <i [class]="statusIcon()" class="text-[10px]"></i>
              {{ statusLabel() }}
            </span>
            @if (!embedded()) {
              <button
                type="button"
                (click)="closeRequested.emit()"
                aria-label="Zamknij"
                class="w-8 h-8 rounded-full grid place-items-center text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
              >
                <i class="pi pi-times text-sm" aria-hidden="true"></i>
              </button>
            } @else {
              <button
                type="button"
                (click)="closeRequested.emit()"
                aria-label="Wyczyść wybór"
                title="Wyczyść wybór"
                class="w-8 h-8 rounded-full grid place-items-center text-surface-400 hover:text-surface-700 dark:hover:text-surface-300 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
              >
                <i class="pi pi-times text-sm" aria-hidden="true"></i>
              </button>
            }
          </div>
        </div>
      </header>

      <section class="px-5 py-4 flex-1 flex flex-col gap-4 overflow-y-auto">
        <!-- Kiedy: godzina + długość wizyty w jednym, mocno wyeksponowanym wierszu. -->
        <div class="flex items-center gap-3">
          <i class="pi pi-clock text-surface-500 w-5 text-center" aria-hidden="true"></i>
          <span class="text-base font-black text-surface-900 font-mono tabular-nums">
            {{ timeRange(a) }}
          </span>
          @if (durationLabel(a); as d) {
            <span class="text-xs font-semibold text-surface-500 dark:text-surface-400">· {{ d }}</span>
          }
          @if (isCustomDuration()) {
            <span class="text-[11px] font-bold text-primary" data-testid="detail-duration-custom-badge">czas własny</span>
          }
        </div>

        <!-- Kto -->
        <div class="flex items-center gap-3">
          <i class="pi pi-user text-surface-500 w-5 text-center" aria-hidden="true"></i>
          <span class="text-sm font-bold text-surface-900">{{ customerLine(a) }}</span>
        </div>

        <!-- Kontakt: telefon / Instagram — ten sam układ co reszta danych (ikona + wartość),
             ale klikalne (tel: / link do profilu) z subtelnym hoverem. -->
        @if (phone(a); as ph) {
          <a
            [href]="'tel:' + ph"
            class="flex items-center gap-3 -mx-2 px-2 py-1 rounded-lg hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
          >
            <i class="pi pi-phone text-surface-500 w-5 text-center" aria-hidden="true"></i>
            <span class="text-sm font-bold text-surface-900 tabular-nums">{{ ph }}</span>
          </a>
        }

        @if (instagramNick(); as ig) {
          <a
            [href]="'https://instagram.com/' + ig"
            target="_blank"
            rel="noopener"
            class="flex items-center gap-3 -mx-2 px-2 py-1 rounded-lg hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
          >
            <i class="pi pi-instagram text-surface-500 w-5 text-center" aria-hidden="true"></i>
            <span class="text-sm font-bold text-surface-900">{{ '@' + ig }}</span>
          </a>
        }

        @if (!hideEmployeeLine() && employeeLine(a); as emp) {
          <div class="flex items-center gap-3" data-testid="employee-line">
            <i class="pi pi-id-card text-surface-500 w-5 text-center" aria-hidden="true"></i>
            <span class="text-sm text-surface-700">{{ emp }}</span>
          </div>
        }

        @if (comboServices().length > 1) {
          <div class="flex items-start gap-3" data-testid="appointment-combo-services">
            <i class="pi pi-list text-surface-500 w-5 text-center mt-0.5" aria-hidden="true"></i>
            <ul class="text-sm text-surface-700 space-y-0.5 flex-1">
              @for (it of comboServices(); track it.serviceId) {
                <li class="flex items-baseline justify-between gap-3">
                  <span>{{ it.serviceName }}</span>
                  <span class="tabular-nums text-surface-500 dark:text-surface-400">{{ it.durationMinutes }} min</span>
                </li>
              }
            </ul>
          </div>
        }

        <!-- Cena orientacyjna (suma usług) — tylko dla wizyt przyszłych. Po rozpoczęciu wizyty
             rozliczenie przejmuje karta „Cena końcowa" niżej, więc tu byłaby duplikacją. -->
        @if (!visitStartedOrDone() && price(); as p) {
          <div class="flex items-center gap-3">
            <i class="pi pi-wallet text-surface-500 w-5 text-center" aria-hidden="true"></i>
            <span class="text-sm font-bold text-surface-900 tabular-nums">
              {{ p.amount }} {{ p.currency }}
            </span>
          </div>
        }

        @if (depositPaid()) {
          <div class="flex items-center gap-3">
            <i class="pi pi-check-circle text-emerald-600 w-5 text-center" aria-hidden="true"></i>
            <span class="text-sm font-bold text-emerald-700 dark:text-emerald-300">
              Zadatek opłacony{{ depositAmount()?.amount != null ? ': ' + depositAmount()?.amount + ' ' + (depositAmount()?.currency ?? '') : '' }}
            </span>
          </div>
        }

        <!-- Inspiracje — zdjęcia dodane przez klientkę przy rezerwacji online. Klik = podgląd. -->
        @if (inspirationImages().length > 0) {
          <div class="flex flex-col gap-2 pt-1" data-testid="inspiration-images">
            <span class="admin-section-label text-surface-500 dark:text-surface-400">Inspiracje</span>
            <div class="flex flex-wrap gap-2">
              @for (photo of inspirationImages(); track photo.url) {
                <button
                  type="button"
                  class="h-16 w-16 overflow-hidden rounded-lg border border-surface-200 dark:border-surface-600 cursor-pointer hover:opacity-80 transition-opacity"
                  (click)="previewPhoto.set(photo.url ?? null)"
                  data-testid="inspiration-thumb"
                >
                  <img
                    [src]="photo.thumbnailUrl || photo.url"
                    alt="Inspiracja"
                    class="h-full w-full object-cover"
                  />
                </button>
              }
            </div>
          </div>
        }

        <!-- Notatki — jedna zwijana sekcja; po rozwinięciu od razu notatka wizyty + notatka klienta. -->
        <div class="flex flex-col gap-2 pt-1">
          <button
            type="button"
            (click)="notesExpanded.set(!notesExpanded())"
            [attr.aria-expanded]="notesExpanded()"
            data-testid="notes-toggle"
            class="flex items-center gap-3 -mx-2 px-2 py-1.5 rounded-lg hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
          >
            <i class="pi pi-comment text-surface-500 w-5 text-center" aria-hidden="true"></i>
            <span class="text-sm font-bold text-surface-700 flex-1 text-left">Notatki</span>
            @if (notes() || customerNotes()) {
              <span class="w-2 h-2 rounded-full bg-primary" aria-label="Notatka uzupełniona"></span>
            }
            <i
              class="pi text-surface-400 text-xs"
              [class.pi-chevron-down]="!notesExpanded()"
              [class.pi-chevron-up]="notesExpanded()"
              aria-hidden="true"
            ></i>
          </button>

          @if (notesExpanded()) {
            <div class="flex flex-col gap-4">
              <!-- Notatka wizyty -->
              <div class="flex flex-col gap-2">
                <span class="admin-section-label text-surface-500 dark:text-surface-400">Notatka wizyty</span>
                @if (canEditAppointmentNotes()) {
                  <textarea
                    pTextarea
                    [(ngModel)]="appointmentNotesValue"
                    [ngModelOptions]="{ standalone: true }"
                    rows="5"
                    maxlength="1000"
                    placeholder="Notatka widoczna tylko dla personelu…"
                    class="w-full text-sm"
                    data-testid="appointment-notes-input"
                  ></textarea>
                  <button
                    type="button"
                    [disabled]="savingAppointmentNotes()"
                    (click)="saveAppointmentNotes()"
                    data-testid="appointment-notes-save"
                    class="self-end rounded-xl bg-primary hover:bg-primary-600 text-white font-bold py-2 px-4 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Zapisz notatkę
                  </button>
                } @else if (notes(); as n) {
                  <p class="text-sm text-surface-700 whitespace-pre-wrap leading-relaxed">{{ n }}</p>
                } @else {
                  <p class="text-sm text-surface-400 italic">Brak notatki</p>
                }
              </div>

              <!-- Notatka o kliencie — tylko gdy wizyta ma przypisanego klienta (nie-gość) -->
              @if (customerDetail.value()) {
                <div class="flex flex-col gap-2" data-testid="customer-notes-section">
                  <span class="admin-section-label text-surface-500 dark:text-surface-400">Notatka o kliencie</span>
                  @if (canEditCustomerNotes()) {
                    <textarea
                      pTextarea
                      [(ngModel)]="customerNotesValue"
                      [ngModelOptions]="{ standalone: true }"
                      rows="5"
                      maxlength="1000"
                      placeholder="Stała notatka o kliencie (np. preferencje, alergie)…"
                      class="w-full text-sm"
                      data-testid="customer-notes-input"
                    ></textarea>
                    <button
                      type="button"
                      [disabled]="savingCustomerNotes()"
                      (click)="saveCustomerNotes()"
                      data-testid="customer-notes-save"
                      class="self-end rounded-xl bg-primary hover:bg-primary-600 text-white font-bold py-2 px-4 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      Zapisz notatkę
                    </button>
                  } @else if (customerNotes(); as cn) {
                    <p class="text-sm text-surface-700 whitespace-pre-wrap leading-relaxed">{{ cn }}</p>
                  } @else {
                    <p class="text-sm text-surface-400 italic">Brak notatki</p>
                  }
                </div>
              }
            </div>
          }
        </div>

        <!-- „Szacunek" + „Cena końcowa" — tylko gdy wizyta się rozpoczęła/zakończyła. Dla wizyt
             przyszłych rozliczenie jest bezużyteczne (klient jeszcze nie był), więc kartę chowamy. -->
        @if (visitStartedOrDone() && detail(); as full) {
          <div class="mt-1 rounded-2xl border border-surface-200/70 dark:border-surface-700/70 p-3 flex flex-col gap-2"
               data-testid="appointment-final-price">
            <div class="flex items-center justify-between gap-3">
              <span class="flex items-center gap-2 text-sm text-surface-600 dark:text-surface-300">
                <i class="pi pi-wallet text-surface-500" aria-hidden="true"></i>
                Szacunek
              </span>
              <span class="text-sm font-bold text-surface-900 tabular-nums">
                {{ full.totalPrice?.amount | currency: full.totalPrice?.currency || 'PLN' : 'symbol' : '1.2-2' }}
              </span>
            </div>

            @if (canEditFinalPrice()) {
              <label class="text-xs font-bold uppercase tracking-wider text-surface-500 dark:text-surface-400">
                Cena końcowa
              </label>
              <div class="flex items-center gap-2">
                <p-inputnumber
                  [(ngModel)]="finalPriceValue"
                  [ngModelOptions]="{ standalone: true }"
                  mode="currency"
                  [currency]="finalPriceCurrency()"
                  locale="pl-PL"
                  [min]="0"
                  inputStyleClass="w-full"
                  styleClass="flex-1"
                  placeholder="Kwota pobrana"
                />
                <button
                  type="button"
                  [disabled]="savingFinalPrice() || finalPriceValue() == null"
                  (click)="saveFinalPrice()"
                  class="rounded-xl bg-primary hover:bg-primary-600 text-white font-bold py-2 px-4 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Zapisz
                </button>
              </div>
            } @else if (full.finalPrice) {
              <div class="flex items-center justify-between gap-3">
                <span class="text-sm text-surface-600 dark:text-surface-300">Cena końcowa</span>
                <span class="text-sm font-bold text-surface-900 tabular-nums">
                  {{ full.finalPrice.amount | currency: full.finalPrice.currency || 'PLN' : 'symbol' : '1.2-2' }}
                </span>
              </div>
            }
          </div>
        }
      </section>

      @if (hasAnyAction()) {
      <footer
        class="px-5 py-4 border-t border-surface-200/70 dark:border-surface-200/70 flex flex-col gap-2 pb-[calc(env(safe-area-inset-bottom)+1rem)] sm:pb-4"
      >
        @if (canQuickConfirm()) {
          <button
            type="button"
            [disabled]="isUpdating()"
            (click)="onConfirm(a)"
            class="w-full rounded-xl bg-emerald-600 hover:bg-emerald-700 text-white font-bold py-2.5 px-4 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <i class="pi pi-check mr-2" aria-hidden="true"></i>
            Zatwierdź
          </button>
        }

        @if (canRebook()) {
          <button
            type="button"
            [disabled]="isUpdating()"
            (click)="onRebook(a)"
            class="w-full rounded-xl bg-amber-500 hover:bg-amber-600 text-amber-950 font-bold py-2.5 px-4 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <i class="pi pi-refresh mr-2" aria-hidden="true"></i>
            Umów ponownie
          </button>
        }

        <!-- Podstawowe akcje: Zmień termin / Anuluj (Zatwierdź i Umów ponownie wyżej). -->
        <div class="flex flex-col sm:flex-row gap-2">
          @if (canReschedule()) {
            <button
              type="button"
              [disabled]="isUpdating()"
              (click)="onReschedule(a)"
              class="flex-1 rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <i class="pi pi-calendar mr-2" aria-hidden="true"></i>
              Zmień termin
            </button>
          }

          @if (canQuickCancel()) {
            <button
              type="button"
              [disabled]="isUpdating()"
              (click)="onCancel(a)"
              class="flex-1 rounded-xl border border-red-500/55 text-red-700 dark:text-red-300 hover:bg-red-50 dark:hover:bg-red-950/30 font-bold py-2.5 px-4 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <i class="pi pi-times mr-2" aria-hidden="true"></i>
              Anuluj wizytę
            </button>
          }
        </div>

        <!-- Akcje drugorzędne — chowane pod „Więcej opcji", żeby stopka na mobile była krótka. -->
        @if (hasSecondaryActions()) {
          <button
            type="button"
            (click)="moreActionsExpanded.set(!moreActionsExpanded())"
            [attr.aria-expanded]="moreActionsExpanded()"
            data-testid="more-actions-toggle"
            class="w-full rounded-xl border border-surface-200 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-600 dark:text-surface-300 transition-colors flex items-center justify-center gap-2"
          >
            <i
              class="pi text-xs"
              [class.pi-chevron-down]="!moreActionsExpanded()"
              [class.pi-chevron-up]="moreActionsExpanded()"
              aria-hidden="true"
            ></i>
            {{ moreActionsExpanded() ? 'Mniej opcji' : 'Więcej opcji' }}
          </button>

          @if (moreActionsExpanded()) {
            <div class="flex flex-col gap-2" data-testid="secondary-actions">
              <!-- Regulacja czasu wizyty (per wizyta) — pełnoszerokościowy panel, czytelny na telefonie. -->
              @if (canEditDuration()) {
                @if (!editingDuration()) {
                  <button
                    type="button"
                    (click)="startEditDuration()"
                    data-testid="detail-duration-edit"
                    class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors flex items-center justify-center gap-2"
                  >
                    <i class="pi pi-clock" aria-hidden="true"></i>
                    Zmień czas trwania
                    @if (isCustomDuration()) {
                      <span class="text-xs font-semibold text-primary">· czas własny</span>
                    }
                  </button>
                } @else {
                  <div
                    class="w-full rounded-xl border border-surface-300 dark:border-surface-600 p-3 flex flex-col gap-3"
                    data-testid="detail-duration-editor"
                  >
                    <label class="admin-section-label">Czas trwania wizyty (min)</label>
                    <p-inputnumber
                      [ngModel]="durationValue()"
                      (ngModelChange)="durationValue.set($event)"
                      [disabled]="savingDuration()"
                      [min]="1"
                      [max]="1440"
                      [step]="5"
                      [showButtons]="true"
                      [useGrouping]="false"
                      styleClass="w-full"
                      inputStyleClass="w-full text-center"
                      data-testid="detail-duration-input"
                    />
                    <div class="flex flex-wrap gap-2">
                      <button
                        type="button"
                        (click)="saveDuration()"
                        [disabled]="savingDuration()"
                        data-testid="detail-duration-save"
                        class="flex-1 min-w-24 rounded-xl bg-primary text-primary-contrast font-bold py-2.5 px-4 transition-colors disabled:opacity-50"
                      >
                        Zapisz
                      </button>
                      @if (isCustomDuration()) {
                        <button
                          type="button"
                          (click)="saveDuration(true)"
                          [disabled]="savingDuration()"
                          data-testid="detail-duration-reset"
                          class="rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-semibold py-2.5 px-3 text-surface-600 dark:text-surface-300 transition-colors disabled:opacity-50"
                        >
                          Standard ({{ standardDurationMinutes() }} min)
                        </button>
                      }
                      <button
                        type="button"
                        (click)="cancelEditDuration()"
                        [disabled]="savingDuration()"
                        class="rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-semibold py-2.5 px-3 text-surface-600 dark:text-surface-300 transition-colors disabled:opacity-50"
                      >
                        Anuluj
                      </button>
                    </div>
                  </div>
                }
              }

              @if (canChangeService()) {
                <button
                  type="button"
                  [disabled]="isUpdating()"
                  (click)="onChangeService(a)"
                  class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <i class="pi pi-tag mr-2" aria-hidden="true"></i>
                  Zmień usługę
                </button>
              }

              @if (canSwap()) {
                <button
                  type="button"
                  [disabled]="isUpdating()"
                  (click)="onSwap(a)"
                  data-testid="swap-from-drawer"
                  class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <i class="pi pi-arrow-right-arrow-left mr-2" aria-hidden="true"></i>
                  Zamień z inną wizytą
                </button>
              }

              @if (canAddBreakAfter()) {
                <button
                  type="button"
                  [disabled]="isUpdating()"
                  (click)="onAddBreakAfter(a)"
                  data-testid="add-break-after"
                  class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <i class="pi pi-pause mr-2" aria-hidden="true"></i>
                  Dodaj przerwę po wizycie
                </button>
              }

              <!--
                Zadatek (manualny model: generuj link → kopiuj / wyślij klientce). Karta niesie cały
                stan linku: kwotę, numer próby, czy dotarł do klienta, ile jeszcze jest ważny oraz
                ile wcześniejszych linków wygasło bez opłaty.
              -->
              @if (depositLinkUrl(); as url) {
                <div
                  class="rounded-xl border overflow-hidden"
                  [class]="depositLinkExpired() ? 'border-rose-300 dark:border-rose-900/60' : 'border-surface-300 dark:border-surface-600'"
                  data-testid="deposit-link-card"
                >
                  <!--
                    Nagłówek stackowany: tytuł + chip w pierwszej linii, kwota i numer próby niżej.
                    Wszystko w jednym wierszu rozjeżdżało się w wąskim drawerze (chip nachodził na
                    ucięty numer próby).
                  -->
                  <div class="px-3 py-2.5 bg-surface-100 dark:bg-surface-100">
                    <div class="flex items-center justify-between gap-2">
                      <span class="text-xs font-bold uppercase tracking-wider text-surface-500">Zadatek</span>
                      @if (depositStatusChip(); as chip) {
                        <span
                          class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold whitespace-nowrap shrink-0"
                          [class]="chip.cls"
                        >
                          <i [class]="chip.icon" aria-hidden="true"></i>{{ chip.label }}
                        </span>
                      }
                    </div>
                    @if (depositAmountLabel(); as amount) {
                      <div class="mt-1 flex items-baseline gap-2 min-w-0">
                        <span class="text-lg font-bold text-surface-800 dark:text-surface-800 tabular-nums">{{ amount }}</span>
                        @if (depositAttemptLabel(); as attempt) {
                          <span class="text-xs text-surface-500 truncate">{{ attempt }}</span>
                        }
                      </div>
                    }
                  </div>

                  <div class="px-3 py-3 flex flex-col gap-3">
                    <p
                      class="text-xs font-mono break-all text-surface-600 dark:text-surface-300 rounded-lg border border-surface-200 dark:border-surface-600 px-2.5 py-2"
                    >
                      {{ url }}
                    </p>

                    <div class="flex flex-col gap-1.5 text-xs">
                      <!-- Czy klient w ogóle dostał link. Backend stempluje dopiero po udanej wysyłce. -->
                      @if (depositLinkSentLabel(); as sentLabel) {
                        <p class="flex items-center gap-2 text-emerald-700 dark:text-emerald-400">
                          <i class="pi pi-check-circle" aria-hidden="true"></i>
                          <span class="font-bold">{{ sentLabel }}</span>
                        </p>
                      } @else {
                        <p class="flex items-center gap-2 text-surface-500">
                          <i class="pi pi-inbox" aria-hidden="true"></i>
                          <span>Jeszcze nie wysłano klientowi</span>
                        </p>
                      }

                      <!-- Ważność: odliczanie do wygaśnięcia, po terminie — kiedy wygasł. -->
                      @if (depositValidityLabel(); as validity) {
                        <p
                          class="flex items-center gap-2"
                          [class]="depositLinkExpired() ? 'text-rose-700 dark:text-rose-300 font-bold' : 'text-surface-500'"
                        >
                          <i [class]="depositLinkExpired() ? 'pi pi-times-circle' : 'pi pi-clock'" aria-hidden="true"></i>
                          <span>{{ validity }}</span>
                        </p>
                      }
                    </div>

                    <!-- Nieudane próby sprzed aktualnego linku — sygnał, że klient zwleka z opłatą. -->
                    @if (expiredAttemptsLabel(); as failed) {
                      <p
                        class="flex items-start gap-2 text-xs rounded-lg bg-amber-50 dark:bg-amber-950/20 text-amber-800 dark:text-amber-200 px-2.5 py-2"
                        data-testid="deposit-expired-attempts"
                      >
                        <i class="pi pi-exclamation-triangle mt-0.5" aria-hidden="true"></i>
                        <span>{{ failed }}</span>
                      </p>
                    }

                    <div class="flex flex-wrap gap-2">
                      <button
                        type="button"
                        (click)="copyDepositLink()"
                        class="inline-flex items-center gap-2 px-3 py-2 rounded-lg border border-surface-300 dark:border-surface-600 hover:border-primary/45 text-surface-700 text-sm font-bold transition-colors"
                      >
                        <i class="pi pi-copy" aria-hidden="true"></i> Skopiuj
                      </button>
                      <button
                        type="button"
                        [disabled]="sendingDeposit() || depositLinkExpired()"
                        (click)="sendDepositLink()"
                        class="inline-flex items-center gap-2 px-3 py-2 rounded-lg border border-surface-300 dark:border-surface-600 hover:border-primary/45 text-surface-700 text-sm font-bold transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        <i class="pi pi-send" aria-hidden="true"></i>
                        {{ depositLinkSentAt() ? 'Wyślij ponownie' : 'Wyślij klientowi' }}
                      </button>
                    </div>
                  </div>
                </div>
              }

              @if (canGenerateDeposit()) {
                <button
                  type="button"
                  [disabled]="generatingDeposit()"
                  (click)="generateDepositLink()"
                  class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <i class="pi pi-wallet mr-2" aria-hidden="true"></i>
                  {{ depositLinkUrl() ? 'Generuj nowy link' : 'Generuj zadatek' }}
                </button>
              }

              @if (!depositLinkUrl() && appointmentAwaitingPayment() && canMutate()) {
                <button
                  type="button"
                  [disabled]="sendingDeposit()"
                  (click)="sendDepositLink()"
                  class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <i class="pi pi-send mr-2" aria-hidden="true"></i>
                  Wyślij link klientowi
                </button>
              }
            </div>
          }
        }
      </footer>
      }
    </ng-template>

    @if (embedded()) {
      <!-- Desktop inline panel — stała kolumna obok kalendarza, bez Drawer/click-outside/body-lock -->
      <!-- Stała wysokość (h-), nie max-h: przy max-h panel rósł w dół w miarę doładowywania sekcji
           (cena, Instagram, inspiracje), spychając stopkę — „Zatwierdź" uciekał spod kursora.
           Ze stałą wysokością jest ona znana od pierwszej klatki, treść scrolluje się w środku,
           a stopka stoi na dole niezależnie od tego, ile danych dojedzie. -->
      <aside class="admin-glass-card rounded-3xl shadow-sm overflow-hidden sticky top-4 flex flex-col h-[calc(100dvh-2rem)]">
        @if (appointment(); as a) {
          <ng-container *ngTemplateOutlet="content; context: { $implicit: a }" />
        } @else {
          <div class="flex-1 flex flex-col items-center justify-center text-center px-6 py-12 text-surface-500 dark:text-surface-400">
            <div class="w-14 h-14 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mb-3">
              <i class="pi pi-calendar text-2xl text-surface-400" aria-hidden="true"></i>
            </div>
            <p class="text-sm font-bold text-surface-700 mb-1">Wybierz wizytę</p>
            <p class="text-xs leading-relaxed max-w-[22ch]">
              Kliknij na kafelek w kalendarzu, aby zobaczyć szczegóły i szybkie akcje.
            </p>
          </div>
        }
      </aside>
    } @else {
      <p-drawer
        [visible]="isVisible()"
        (visibleChange)="onVisibleChange($event)"
        [position]="position()"
        [modal]="false"
        [showCloseIcon]="false"
        [dismissible]="false"
        [styleClass]="drawerClass()"
        [ariaCloseLabel]="'Zamknij podgląd wizyty'"
      >
        @if (appointment(); as a) {
          <ng-container *ngTemplateOutlet="content; context: { $implicit: a }" />
        }
      </p-drawer>
    }

    <!-- Lightbox inspiracji — podgląd pełnego zdjęcia po kliknięciu miniatury. -->
    <p-dialog
      [visible]="previewPhoto() != null"
      (visibleChange)="$event ? null : previewPhoto.set(null)"
      [modal]="true"
      [dismissableMask]="true"
      header="Inspiracja"
      [style]="{ width: '90vw', maxWidth: '720px' }"
      data-testid="inspiration-lightbox"
    >
      @if (previewPhoto(); as url) {
        <img [src]="url" alt="Inspiracja" class="w-full h-auto rounded-lg" />
      }
    </p-dialog>

  `,
})
export class AppointmentDetailSheetComponent {
  readonly appointment = input<AppointmentPreviewDto | null>(null);
  readonly isDesktop = input<boolean>(false);
  readonly isUpdating = input<boolean>(false);
  /**
   * Czy zalogowany user może mutować wizytę (Zatwierdź / Anuluj). Wynika z roli + polityki
   * `StaffCalendarVisibilityPolicy` w monolicie — tu tylko ukrywamy przyciski gdy false
   * (sheet jest read-only). Default `true` — backward-compat dla wywołań bez tej props.
   */
  readonly canMutate = input<boolean>(true);
  /**
   * `embedded=true` — komponent renderuje się jako sticky panel (desktop split-view) bez Drawera,
   * body-lock'a i click-outside listenera. Empty state pokazywany gdy brak wybranej wizyty.
   * `embedded=false` (default) — klasyczny bottom sheet (mobile) / side drawer (desktop).
   */
  readonly embedded = input<boolean>(false);
  /**
   * Czy pokazać akcję „Dodaj przerwę po wizycie". Parent liczy warunek (grafik dynamiczny,
   * pojedynczy pracownik, wolny czas zaraz za wizytą). Sheet tylko renderuje przycisk.
   */
  readonly canAddBreakAfter = input<boolean>(false);
  /**
   * Salon jednoosobowy — chowamy linię „pracownik" (zawsze ta sama osoba, więc to szum). Parent
   * liczy warunek z pełnej listy pracowników salonu.
   */
  readonly hideEmployeeLine = input<boolean>(false);
  /**
   * Pełne dane wizyty pobrane już przez parenta (deep-link `?appointment=<id>` fetchuje
   * `AppointmentDto`, żeby w ogóle wiedzieć, którą wizytę otworzyć). Bez tego sheet strzelałby
   * po raz drugi po ten sam zasób — dodatkowy round-trip i drugi przeskok layoutu tuż po otwarciu.
   * Używane tylko gdy `id` zgadza się z `appointment()`; po każdym `reload()` ustępuje danym z serwera.
   */
  readonly preloadedDetail = input<AppointmentDto | null>(null);
  /**
   * Czy pokazać „Pokaż w kalendarzu". Parent liczy warunek (kalendarz stoi na innym dniu lub
   * pracowniku niż wizyta) — dla wizyty otwartej kliknięciem w kafelek przycisk się nie pokazuje.
   */
  readonly canRevealInCalendar = input<boolean>(false);

  readonly closeRequested = output<void>();
  /** Użytkownik świadomie prosi o kontekst grafiku — parent przestawia dzień i pracownika. */
  readonly revealRequested = output<void>();
  readonly confirm = output<string>();
  readonly cancel = output<string>();
  /**
   * Emit z pełnym `AppointmentPreviewDto` — parent uruchamia
   * `RescheduleAppointmentDialogComponent` (F3.1). Bez canMutate
   * przycisk się nie pokazuje, więc emit tylko z autoryzowanej ścieżki.
   */
  /** Emitowane po zmianie czasu wizyty (endTime się zmienia → parent przeładowuje kalendarz). */
  readonly durationChanged = output<void>();
  readonly rescheduleRequested = output<AppointmentPreviewDto>();
  /**
   * Emit dla „Zmień usługę" — parent otwiera `ChangeServiceDialogComponent`. Podmiana zabiegu
   * w obrębie tego samego terminu (data/godzina bez zmian). Widoczne na tych samych warunkach
   * co reschedule (status nie terminalny + uprawnienie do mutacji).
   */
  readonly changeServiceRequested = output<AppointmentPreviewDto>();
  /**
   * Emit dla flow „Umów ponownie" — parent otwiera CreateAppointmentDrawer z pre-fillem
   * (klient + usługa + pracownik z tej wizyty). Widoczne tylko dla inProgress / completed,
   * gdzie ma to sens biznesowy (klient już skorzystał z usługi, chcemy go zaprosić na kolejną).
   */
  readonly rebookRequested = output<AppointmentPreviewDto>();
  /**
   * Emit dla „Dodaj przerwę po wizycie" — parent otwiera edytor przerwy z prefillem startu
   * = koniec tej wizyty. Widoczność steruje `canAddBreakAfter` (parent liczy: grafik dynamiczny,
   * pojedynczy pracownik, wolny czas zaraz po wizycie).
   */
  readonly addBreakAfterRequested = output<AppointmentPreviewDto>();
  /** „Zamień z inną wizytą" — parent włącza tryb zamiany z tą wizytą jako pierwszą. */
  readonly swapRequested = output<AppointmentPreviewDto>();

  private readonly renderer = inject(Renderer2);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly scrollLock = inject(BodyScrollLockService);
  private readonly appointmentsClient = inject(AppointmentsClient);
  private readonly customersClient = inject(CustomersClient);
  private readonly depositsClient = inject(DepositsClient);
  private readonly messages = inject(MessageService);

  /** Stan akcji zadatku (manualny model: personel generuje link i wysyła klientce). */
  protected readonly generatingDeposit = signal(false);
  protected readonly sendingDeposit = signal(false);
  protected readonly generatedLink = signal<GenerateDepositLinkResult | null>(null);

  /**
   * Tykający zegar dla odliczania ważności linku zadatku. Bez niego „Ważny jeszcze 3 min" zastygłby
   * na wartości z chwili otwarcia drawera. 30 s wystarcza — pokazujemy minuty, nie sekundy.
   */
  private readonly clockTick = signal(Date.now());

  /**
   * Pełne dane wizyty (cena, notatka, Instagram klienta) doładowywane po `id`. `AppointmentPreviewDto`
   * z range-query ich nie zawiera, a nie chcemy pogrubiać payloadu całego kalendarza — drawer dotyczy
   * jednej wizyty, więc fetch po id jest tani. Sekcje pojawiają się gdy fetch wróci; podstawowe pola
   * (godzina, klient, telefon) są od razu z inputu `appointment()`.
   */
  protected readonly fullDetail = rxResource({
    params: () => (this.usablePreload() ? undefined : (this.appointment()?.id ?? undefined)),
    stream: ({ params: id }) =>
      id ? this.appointmentsClient.getAppointmentById(id).pipe(catchError(() => of(null))) : of(null),
  });

  /**
   * Preload z parenta zdezaktualizowany przez własną mutację (`reload()`). Od tego momentu
   * o wizycie decyduje serwer — inaczej po zapisie czasu/notatki wracałby stary snapshot.
   */
  private readonly preloadStale = signal(false);

  /** Preload nadający się do użycia: dotyczy tej samej wizyty i nie został unieważniony mutacją. */
  private readonly usablePreload = computed<AppointmentDto | null>(() => {
    if (this.preloadStale()) return null;
    const pre = this.preloadedDetail();
    const id = this.appointment()?.id;
    return pre && id && pre.id === id ? pre : null;
  });

  /**
   * Szczegóły wizyty — preload z parenta albo własny fetch. JEDYNE źródło dla sekcji lazy;
   * nie czytaj `fullDetail.value()` bezpośrednio, bo przy preloadzie resource jest pusty.
   */
  protected readonly detail = computed<AppointmentDto | null>(() => {
    const own = this.fullDetail;
    return this.usablePreload() ?? own.value() ?? null;
  });

  /** Unieważnia preload i dociąga świeże dane po mutacji. */
  private reloadDetail(): void {
    this.preloadStale.set(true);
    this.fullDetail.reload();
  }

  /** Pozycje combo (wszystkie usługi wizyty) — lista pokazywana, gdy >1 usługa. */
  protected readonly comboServices = computed(() => this.detail()?.services ?? []);

  /** Zdjęcia inspiracji dodane przez klientkę przy rezerwacji online (≤3). */
  protected readonly inspirationImages = computed(
    () => this.detail()?.inspirationImages ?? [],
  );

  /** URL zdjęcia otwartego w lightboxie (null = zamknięty). */
  protected readonly previewPhoto = signal<string | null>(null);

  protected readonly price = computed(() => {
    const p = this.detail()?.totalPrice;
    return p?.amount != null ? p : null;
  });

  protected readonly notes = computed(() => {
    const n = this.detail()?.appointmentNotes?.trim();
    return n ? n : null;
  });

  // --- Czas trwania wizyty (edycja inline, override personelu) ---
  protected readonly editingDuration = signal(false);
  protected readonly durationValue = signal<number | null>(null);
  protected readonly savingDuration = signal(false);

  // --- Notatka wizyty (edycja inline) ---
  protected readonly appointmentNotesValue = signal<string>('');
  protected readonly savingAppointmentNotes = signal(false);
  /** Wspólna sekcja notatek — domyślnie zwinięta; po rozwinięciu widać obie notatki. Reset przy zmianie wizyty. */
  protected readonly notesExpanded = signal(false);

  /** Czy rozwinięto sekcję akcji drugorzędnych („Więcej opcji"). Reset przy zmianie wizyty. */
  protected readonly moreActionsExpanded = signal(false);

  /** Notatkę wizyty można edytować dla każdej wizyty poza anulowaną (przydatna też po wizycie). */
  protected readonly canEditAppointmentNotes = computed(
    () => this.canMutate() && this.variant() !== 'canceled' && this.detail() != null,
  );

  // --- Notatka o kliencie (osobny fetch — AppointmentDto jej nie niesie; ukryta dla gości) ---
  /**
   * Pełne dane klienta (z `generalNotes`) doładowywane po `customerId` z wizyty. Tylko dla wizyt
   * z przypisanym klientem (nie-gość) — dla gościa `customerId` jest null i sekcja się nie pokazuje.
   */
  protected readonly customerDetail = rxResource({
    params: () => {
      const full = this.detail();
      if (!full || full.isGuest) return undefined;
      return full.customerId ?? undefined;
    },
    stream: ({ params: id }) =>
      id ? this.customersClient.getCustomer(id).pipe(catchError(() => of(null))) : of(null),
  });

  protected readonly customerNotes = computed(() => {
    const n = this.customerDetail.value()?.generalNotes?.trim();
    return n ? n : null;
  });

  protected readonly customerNotesValue = signal<string>('');
  protected readonly savingCustomerNotes = signal(false);

  protected readonly canEditCustomerNotes = computed(
    () => this.canMutate() && this.customerDetail.value() != null,
  );

  protected readonly instagramNick = computed(() => {
    const ig = this.detail()?.customerInstagramNick?.trim();
    return ig ? ig : null;
  });

  /** Wartość pola „cena końcowa" (prefill z finalPrice lub szacunku). */
  protected readonly finalPriceValue = signal<number | null>(null);
  protected readonly savingFinalPrice = signal(false);

  protected readonly finalPriceCurrency = computed(
    () =>
      this.detail()?.finalPrice?.currency ??
      this.detail()?.totalPrice?.currency ??
      'PLN',
  );

  /** Cenę końcową można ustawić dla każdej wizyty poza anulowaną (zwykle po wizycie = Completed). */
  protected readonly canEditFinalPrice = computed(
    () => this.canMutate() && this.variant() !== 'canceled' && this.detail() != null,
  );

  /**
   * Widoczność wyprowadzona wprost z inputu — czysty signal-flow. Bez plain field + `effect()`
   * panel pod OnPush nie re-renderował się po zmianie inputu (mask się pokazywał, drawer nie).
   * Domknięcie inicjowane przez użytkownika idzie przez `(visibleChange)` → `closeRequested`,
   * a parent ustawia `appointment` na `null` — `isVisible()` wraca do `false` w tym samym CD.
   */
  protected readonly isVisible = computed(() => this.appointment() != null);

  private clickOutsideUnsubscribe?: () => void;
  private bodyLocked = false;

  /** Id wizyty, dla której ostatnio resetowaliśmy stan zadatku — strażnik przed resetem na każdym CD. */
  private lastDepositAppointmentId: string | null = null;

  constructor() {
    const clockInterval = setInterval(() => this.clockTick.set(Date.now()), 30_000);
    this.destroyRef.onDestroy(() => clearInterval(clockInterval));

    // Reset stanu zadatku przy przełączeniu wizyty. Bez tego link wygenerowany dla wizyty A
    // zostawał widoczny po otwarciu wizyty B (signal `generatedLink` nigdy nie czyszczony).
    effect(() => {
      const id = this.appointment()?.id ?? null;
      if (id === this.lastDepositAppointmentId) return;
      this.lastDepositAppointmentId = id;
      this.generatedLink.set(null);
      this.generatingDeposit.set(false);
      this.sendingDeposit.set(false);
      // Notatki i „Więcej opcji" wracają do stanu zwiniętego przy każdej nowej wizycie.
      this.notesExpanded.set(false);
      this.moreActionsExpanded.set(false);
      // Edycja czasu wraca do stanu zamkniętego przy każdej nowej wizycie.
      this.editingDuration.set(false);
      // Nowa wizyta = nowy preload z parenta; unieważnienie po mutacji dotyczyło poprzedniej.
      this.preloadStale.set(false);
    });

    // Blokada scrolla body + click-outside listener — TYLKO w trybie drawer (mobile).
    // W trybie `embedded` panel jest stałą częścią layoutu, nie potrzebuje takich efektów ubocznych.
    effect(() => {
      if (this.embedded()) {
        this.unbindClickOutside();
        this.unlockBodyScroll();
        return;
      }
      if (this.isVisible()) {
        this.lockBodyScroll();
        // setTimeout(0) — bez tego klik otwierający drawer (jeszcze propaguje do document)
        // od razu odpalał handler outside i zamykał panel w tym samym evencie.
        const t = setTimeout(() => this.bindClickOutside(), 0);
        this.destroyRef.onDestroy(() => clearTimeout(t));
      } else {
        this.unbindClickOutside();
        this.unlockBodyScroll();
      }
    });

    this.destroyRef.onDestroy(() => {
      this.unbindClickOutside();
      this.unlockBodyScroll();
    });

    // Prefill pola „cena końcowa" gdy doładuje się pełne DTO: istniejąca cena końcowa,
    // a w jej braku — szacunek (totalPrice), żeby zwykle wystarczyło kliknąć „Zapisz".
    effect(() => {
      const full = this.detail();
      const prefill = full?.finalPrice?.amount ?? full?.totalPrice?.amount ?? null;
      this.finalPriceValue.set(prefill);
    });

    // Prefill notatki wizyty po doładowaniu pełnego DTO (oraz po przełączeniu wizyty).
    effect(() => {
      this.appointmentNotesValue.set(this.detail()?.appointmentNotes ?? '');
    });

    // Prefill notatki klienta po doładowaniu danych klienta (null = gość / brak → puste pole).
    effect(() => {
      this.customerNotesValue.set(this.customerDetail.value()?.generalNotes ?? '');
    });
  }

  protected startEditDuration(): void {
    this.durationValue.set(this.effectiveDurationMinutes());
    this.editingDuration.set(true);
  }

  protected cancelEditDuration(): void {
    this.editingDuration.set(false);
  }

  /**
   * Zapisuje niestandardowy czas wizyty. <paramref name="reset"/> = true → powrót do standardu (null).
   * Wartość równa standardowej sumie też normalizowana do null. 409 (nie mieści się w slocie) obsłuży
   * errorInterceptor. Po sukcesie przeładowuje szczegóły i informuje parenta (endTime → kalendarz).
   */
  protected saveDuration(reset = false): void {
    const id = this.appointment()?.id;
    if (!id || this.savingDuration()) return;

    const raw = reset ? null : this.durationValue();
    const normalized = raw == null || raw === this.standardDurationMinutes() ? undefined : raw;
    const body: SetDurationRequest = { durationMinutes: normalized };

    this.savingDuration.set(true);
    this.appointmentsClient
      .setAppointmentDuration(id, body)
      .pipe(finalize(() => this.savingDuration.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({ severity: 'success', summary: 'Zapisano', detail: 'Czas wizyty zaktualizowany.' });
          this.editingDuration.set(false);
          this.reloadDetail();
          this.durationChanged.emit();
        },
        error: () => {
          /* komunikat pokazuje errorInterceptor (409 → „termin zajęty") */
        },
      });
  }

  protected saveAppointmentNotes(): void {
    const id = this.appointment()?.id;
    if (!id || this.savingAppointmentNotes()) return;

    this.savingAppointmentNotes.set(true);
    this.appointmentsClient
      .updateAppointmentNote(id, this.appointmentNotesValue().trim())
      .pipe(finalize(() => this.savingAppointmentNotes.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({ severity: 'success', summary: 'Zapisano', detail: 'Notatka wizyty zaktualizowana.' });
          this.reloadDetail();
        },
        error: () => {
          /* komunikat pokazuje errorInterceptor (mapowanie kodów na PL) */
        },
      });
  }

  protected saveCustomerNotes(): void {
    const customerId = this.customerDetail.value()?.id;
    if (!customerId || this.savingCustomerNotes()) return;

    this.savingCustomerNotes.set(true);
    this.customersClient
      .updateCustomerNote(customerId, this.customerNotesValue().trim())
      .pipe(finalize(() => this.savingCustomerNotes.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({ severity: 'success', summary: 'Zapisano', detail: 'Notatka o kliencie zaktualizowana.' });
          this.customerDetail.reload();
        },
        error: () => {
          /* errorInterceptor */
        },
      });
  }

  protected saveFinalPrice(): void {
    const id = this.appointment()?.id;
    const amount = this.finalPriceValue();
    if (!id || amount == null) return;

    this.savingFinalPrice.set(true);
    const body = { amount, currency: this.finalPriceCurrency() } as SetFinalPriceRequest;
    this.appointmentsClient.setFinalPrice(id, body).subscribe({
      next: () => {
        this.savingFinalPrice.set(false);
        this.messages.add({ severity: 'success', summary: 'Zapisano', detail: 'Cena końcowa zaktualizowana.' });
        this.reloadDetail();
      },
      error: () => {
        this.savingFinalPrice.set(false);
        this.messages.add({ severity: 'error', summary: 'Błąd', detail: 'Nie udało się zapisać ceny końcowej.' });
      },
    });
  }

  private lockBodyScroll(): void {
    if (this.bodyLocked) return;
    this.bodyLocked = true;
    this.scrollLock.lock();
  }

  private unlockBodyScroll(): void {
    if (!this.bodyLocked) return;
    this.bodyLocked = false;
    this.scrollLock.unlock();
  }

  private bindClickOutside(): void {
    if (this.clickOutsideUnsubscribe) return;
    this.clickOutsideUnsubscribe = this.renderer.listen(
      'document',
      'pointerdown',
      (ev: Event) => {
        if (!this.isVisible()) return;
        const target = ev.target as HTMLElement | null;
        if (!target) return;
        // Klik w jakimkolwiek PrimeNG overlay (drawer, confirm dialog, toast) NIE zamyka panelu —
        // bez tego confirm zamknięcia wizyty zaraz po otwarciu kasowałby też drawer pod spodem.
        if (this.isInsideOverlay(target)) return;
        this.closeRequested.emit();
      },
    );
  }

  private unbindClickOutside(): void {
    this.clickOutsideUnsubscribe?.();
    this.clickOutsideUnsubscribe = undefined;
  }

  private isInsideOverlay(target: HTMLElement): boolean {
    return !!(
      target.closest('.p-drawer') ||
      target.closest('.p-dialog') ||
      target.closest('.p-confirm-dialog') ||
      target.closest('.p-overlay-mask') ||
      target.closest('.p-toast') ||
      target.closest('.p-tooltip')
    );
  }

  protected readonly position = computed<'bottom' | 'right'>(() =>
    this.isDesktop() ? 'right' : 'bottom',
  );

  protected readonly drawerClass = computed(() => {
    // Solidne tło — nie `admin-glass-card`, który ma 72% przezroczystość. Drawer ma być
    // wyraźnie odcięty od treści pod spodem, żeby animacja wjazdu/zjazdu była dobrze widoczna.
    // `admin-drawer-shell` — klasa-marker (patrz styles.css). PrimeNG v21 NIE MA
    // `contentStyleClass`, więc `.p-drawer-content` zostawał `display:block`: nasz `flex-1`
    // na sekcji nic nie robił, a stopka płynęła zaraz za treścią zamiast stać na dole.
    // To dlatego „Zatwierdź" uciekał w dół, gdy doładowały się cena/Instagram/inspiracje.
    // Marker robi z contentu flex-kolumnę — stopka trzyma się dołu, rośnie tylko scroll.
    const base = 'admin-drawer-shell !bg-surface-0 dark:!bg-surface-50 !border-l-0 !border-t-0';
    return this.isDesktop()
      ? `${base} !w-[420px] !max-w-[92vw] !border-l !border-surface-200 dark:!border-surface-200`
      : // Bottom sheet trzyma się dołu ekranu, więc przy `h-auto` rośnie GÓRNA krawędź, a stopka
        // stoi. Wysokość z treści jest tu lepsza niż stały detent — krótka wizyta nie rozciąga
        // panelu na pół ekranu. Zgodnie z `admin-drawer.component.ts`.
        `${base} !h-auto !max-h-[90dvh] !rounded-t-3xl shadow-2xl`;
  });

  private readonly variant = computed<AppointmentStatusVariant>(() => {
    const a = this.appointment();
    if (!a) return 'default';
    return statusVariantFromIdOrName(a.status?.id, a.status?.name);
  });

  protected readonly statusChipClass = computed(() => {
    const tokens = APPOINTMENT_STATUS_TOKENS[this.variant()];
    return `${tokens.surfaceClasses} ${tokens.textClasses}`;
  });

  protected readonly statusIcon = computed(
    () => APPOINTMENT_STATUS_TOKENS[this.variant()].icon,
  );

  protected readonly statusLabel = computed(
    () => APPOINTMENT_STATUS_TOKENS[this.variant()].label,
  );

  protected readonly serviceName = computed(() => {
    const a = this.appointment();
    const x = a as unknown as Record<string, unknown> | null;
    const n = a?.serviceName ?? (x?.['ServiceName'] as string | undefined);
    return typeof n === 'string' && n.trim() !== '' ? n.trim() : 'Wizyta';
  });

  protected readonly canQuickConfirm = computed(
    () => this.canMutate() && this.variant() === 'pending',
  );

  protected readonly canQuickCancel = computed(() => {
    if (!this.canMutate()) return false;
    const v = this.variant();
    return v !== 'completed' && v !== 'canceled';
  });

  /**
   * Reschedule (F3.1) — analogicznie jak cancel: tylko gdy user może mutować wizytę
   * i status nie jest finalny. Po reschedule status zostaje (booked/pending), zmienia
   * się tylko data/godzina, więc walidacja jest taka sama jak dla cancel.
   */
  protected readonly canReschedule = computed(() => {
    if (!this.canMutate()) return false;
    const v = this.variant();
    return v !== 'completed' && v !== 'canceled';
  });

  /**
   * „Zmień usługę" — te same warunki co reschedule: tylko gdy user może mutować wizytę i status
   * nie jest terminalny (Completed/Canceled). Podmiana składu zachowuje termin; backend blokuje,
   * gdy dłuższy zabieg nie mieści się w slocie.
   */
  protected readonly canChangeService = computed(() => {
    if (!this.canMutate()) return false;
    const v = this.variant();
    return v !== 'completed' && v !== 'canceled';
  });

  /**
   * „Zmień czas" — personel reguluje długość bloku per wizyta (jedne klientki krócej, inne dłużej).
   * Warunki jak zmiana usługi (mutacja + status nieterminalny). Wymaga doładowanego `fullDetail`
   * (skład usług → standardowa suma czasów do prefillu i normalizacji).
   */
  protected readonly canEditDuration = computed(() => {
    if (!this.canMutate() || this.detail() == null) return false;
    const v = this.variant();
    return v !== 'completed' && v !== 'canceled';
  });

  /** Suma standardowych czasów usług wizyty (minuty) — z pozycji combo w fullDetail. */
  protected readonly standardDurationMinutes = computed(() =>
    (this.detail()?.services ?? []).reduce((s, i) => s + (i.durationMinutes ?? 0), 0),
  );

  /** Czy wizyta ma niestandardowy czas (override personelu). */
  protected readonly isCustomDuration = computed(
    () => this.detail()?.customDurationMinutes != null,
  );

  /** Efektywny czas bloku (minuty) = EndTime − StartTime wizyty. */
  protected readonly effectiveDurationMinutes = computed(() => {
    const a = this.appointment();
    const s = a ? this.timeToMinutes(a.startTime) : null;
    const e = a ? this.timeToMinutes(a.endTime) : null;
    return s != null && e != null && e > s ? e - s : null;
  });

  /**
   * „Zamień z inną wizytą" — start trybu zamiany z TĄ wizytą jako pierwszą. Warunki jak reschedule
   * (mutacja + status nieterminalny). Parent zamyka drawer, ustawia tryb zamiany i czeka na wskazanie
   * drugiej wizyty.
   */
  protected readonly canSwap = computed(() => {
    if (!this.canMutate()) return false;
    const v = this.variant();
    return v !== 'completed' && v !== 'canceled';
  });

  /**
   * „Umów ponownie": opcja widoczna dla wizyt w trakcie i zakończonych — naturalny moment
   * w salonie kosmetycznym (klient kończy zabieg, od razu umawia kolejny). Nie pokazujemy
   * dla pending/booked (jeszcze nie przyszedł) ani canceled (nie zaszło).
   */
  protected readonly canRebook = computed(() => {
    if (!this.canMutate()) return false;
    const v = this.variant();
    return v === 'inProgress' || v === 'completed';
  });

  /**
   * Czy wizyta już się rozpoczęła / zakończyła. Dla wizyt przyszłych (pending/booked) NIE pokazujemy
   * ceny końcowej ani „Szacunku" — kwota do rozliczenia ma sens dopiero, gdy klient jest na miejscu.
   * Upraszcza drawer nadchodzącej wizyty (mobile).
   */
  protected readonly visitStartedOrDone = computed(() => {
    const v = this.variant();
    return v === 'inProgress' || v === 'completed';
  });

  // --- Zadatek (pola płatności doładowywane w fullDetail) ---
  protected readonly depositPaid = computed(
    () => String(this.detail()?.paymentStatus ?? '').toLowerCase() === 'paid',
  );
  protected readonly depositAmount = computed(() => this.detail()?.depositAmount ?? null);
  protected readonly appointmentAwaitingPayment = computed(
    () => String(this.detail()?.paymentStatus ?? '').toLowerCase() === 'awaitingpayment',
  );

  /**
   * URL linku do zadatku do pokazania: świeżo wygenerowany (lokalny sygnał) lub utrwalony z backendu
   * (`paymentLinkUrl` z fullDetail). Dzięki temu raz wygenerowany link jest widoczny po odświeżeniu
   * strony i po powrocie do wizyty, a nie tylko w pamięci komponentu. Po opłaceniu link znika —
   * nie ma sensu go już kopiować/wysyłać.
   */
  protected readonly depositLinkUrl = computed(() => {
    if (this.depositPaid()) return null;
    return this.generatedLink()?.url ?? this.detail()?.paymentLinkUrl ?? null;
  });

  /**
   * Czy aktualny link wygasł. Autorytetem jest backend (`paymentLinkExpired` — ma prawdziwy zegar),
   * ale gdy drawer jest otwarty dłużej niż ważność linku, dokładamy porównanie z lokalnym czasem,
   * żeby stan przełączył się na oczach użytkownika zamiast czekać na refetch.
   * Link świeżo wygenerowany w tej sesji nigdy nie jest wygasły.
   */
  protected readonly depositLinkExpired = computed(() => {
    if (this.generatedLink() != null) return false;
    if (this.detail()?.paymentLinkExpired === true) return true;

    const expiresAt = this.depositLinkExpiresAt();
    return expiresAt != null && expiresAt.getTime() <= this.clockTick();
  });

  /**
   * UTC potwierdzonej wysyłki linku. Backend stempluje to dopiero, gdy kanał faktycznie dostarczył
   * wiadomość — nieudany SMS (np. blokada linków u operatora) zostawia null. Świeżo wygenerowany link
   * z tej sesji nigdy nie jest wysłany, nawet jeśli fullDetail nie zdążył się przeładować.
   */
  protected readonly depositLinkSentAt = computed(() =>
    this.generatedLink() == null ? (this.detail()?.depositLinkSentAtUtc ?? null) : null,
  );

  protected readonly depositLinkSentLabel = computed(() => {
    const sentAt = this.depositLinkSentAt();
    if (!sentAt) return null;

    const channel = this.detail()?.depositLinkSentChannel;
    const via = channel === 'Sms' ? 'SMS-em' : channel === 'Email' ? 'e-mailem' : null;
    const when = new Date(sentAt).toLocaleString('pl-PL', {
      day: '2-digit',
      month: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });

    return via ? `Wysłano ${via} — ${when}` : `Wysłano — ${when}`;
  });

  /** Kwota zadatku: świeżo wygenerowana (lokalny sygnał) albo utrwalona z backendu. */
  protected readonly depositAmountLabel = computed(() => {
    const fresh = this.generatedLink();
    const amount = fresh?.amount ?? this.depositAmount()?.amount ?? null;
    const currency = fresh?.currency ?? this.depositAmount()?.currency ?? null;
    if (amount == null || !currency) return null;

    try {
      return new Intl.NumberFormat('pl-PL', { style: 'currency', currency }).format(amount);
    } catch {
      // Nieznany kod waluty — lepiej pokazać surową wartość niż wywalić widok.
      return `${amount} ${currency}`;
    }
  });

  /** Numer aktualnej próby. Przy pierwszym linku milczymy — „1. link" to szum. */
  protected readonly depositAttemptLabel = computed(() => {
    const attempts = this.detail()?.depositLinkAttempts ?? 0;
    return attempts > 1 ? `${attempts}. link` : null;
  });

  /**
   * Ile POPRZEDNICH linków wygasło bez opłaty (aktualny opisuje chip statusu). Zero = nic nie
   * pokazujemy; brak ostrzeżenia to dobra wiadomość.
   */
  protected readonly expiredAttemptsLabel = computed(() => {
    const failed = this.detail()?.expiredDepositLinkCount ?? 0;
    if (failed < 1) return null;
    if (failed === 1) return 'Poprzedni link wygasł bez opłaty.';

    // Polska liczba mnoga: 2–4 „linki wygasły", 5+ „linków wygasło" (z wyjątkiem nastek).
    const lastTwo = failed % 100;
    const last = failed % 10;
    const fewForm = last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14);
    return fewForm
      ? `${failed} poprzednie linki wygasły bez opłaty.`
      : `${failed} poprzednich linków wygasło bez opłaty.`;
  });

  protected readonly depositStatusChip = computed(() =>
    this.depositLinkExpired()
      ? {
          label: 'Wygasł',
          icon: 'pi pi-times-circle',
          cls: 'bg-rose-100 text-rose-700 dark:bg-rose-900/40 dark:text-rose-300',
        }
      : {
          label: 'Czeka na opłatę',
          icon: 'pi pi-clock',
          cls: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300',
        },
  );

  /** Termin ważności aktualnego linku — świeżego z tej sesji albo utrwalonego. */
  private readonly depositLinkExpiresAt = computed(() => {
    const raw = this.generatedLink()?.expiresAtUtc ?? this.detail()?.paymentLinkExpiresAtUtc ?? null;
    return raw ? new Date(raw) : null;
  });

  /** „Ważny jeszcze 21 godz. 14 min" / „Wygasł 3 godz. temu". Odświeża się razem z `clockTick`. */
  protected readonly depositValidityLabel = computed(() => {
    const expiresAt = this.depositLinkExpiresAt();
    if (!expiresAt) return null;

    const deltaMs = expiresAt.getTime() - this.clockTick();
    if (this.depositLinkExpired() || deltaMs <= 0) {
      const ago = formatDuration(Math.abs(deltaMs));
      return ago ? `Wygasł ${ago} temu` : 'Wygasł przed chwilą';
    }

    const left = formatDuration(deltaMs);
    return left ? `Ważny jeszcze ${left}` : 'Wygasa lada moment';
  });

  /**
   * „Generuj zadatek": salon ma zadatki włączone + konto Stripe gotowe (depositsAvailable),
   * user może mutować, wizyta nieterminalna i jeszcze nieopłacona.
   */
  protected readonly canGenerateDeposit = computed(() => {
    if (!this.canMutate()) return false;
    if (this.detail()?.depositsAvailable !== true) return false;
    const v = this.variant();
    return v !== 'canceled' && v !== 'completed' && !this.depositPaid();
  });

  /** Czy footer ma cokolwiek do pokazania — bez tego pusty pasek z borderem dla wizyt read-only. */
  protected readonly hasAnyAction = computed(
    () =>
      this.canQuickConfirm() ||
      this.canRebook() ||
      this.canReschedule() ||
      this.canChangeService() ||
      this.canSwap() ||
      this.canQuickCancel() ||
      this.canGenerateDeposit() ||
      this.depositLinkUrl() != null ||
      (this.canMutate() && this.appointmentAwaitingPayment()),
  );

  /**
   * Akcje drugorzędne chowane pod „Więcej opcji": zmiana usługi, zamiana wizyt, przerwa po wizycie
   * oraz cała obsługa zadatku. Podstawowe (Zatwierdź / Umów ponownie / Zmień termin / Anuluj) są
   * zawsze widoczne. Dzięki temu stopka na mobile jest krótka.
   */
  protected readonly hasSecondaryActions = computed(
    () =>
      this.canEditDuration() ||
      this.canChangeService() ||
      this.canSwap() ||
      this.canAddBreakAfter() ||
      this.canGenerateDeposit() ||
      this.depositLinkUrl() != null ||
      (this.canMutate() && this.appointmentAwaitingPayment()),
  );

  protected onConfirm(a: AppointmentPreviewDto): void {
    if (a.id) this.confirm.emit(a.id);
  }

  protected onCancel(a: AppointmentPreviewDto): void {
    if (!a.id) return;
    const id = a.id;
    this.confirmationService.confirm({
      header: 'Anulować wizytę?',
      message: 'Klient zostanie powiadomiony o anulowaniu (jeśli ma włączone powiadomienia). Operacji nie można cofnąć.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Tak, anuluj',
      rejectLabel: 'Wstecz',
      acceptButtonStyleClass: 'p-button-danger',
      rejectButtonStyleClass: 'p-button-outlined p-button-secondary',
      accept: () => this.cancel.emit(id),
    });
  }

  protected onReschedule(a: AppointmentPreviewDto): void {
    if (!a.id) return;
    this.rescheduleRequested.emit(a);
  }

  protected onChangeService(a: AppointmentPreviewDto): void {
    if (!a.id) return;
    this.changeServiceRequested.emit(a);
  }

  protected onSwap(a: AppointmentPreviewDto): void {
    if (!a.id) return;
    this.swapRequested.emit(a);
  }

  protected onRebook(a: AppointmentPreviewDto): void {
    this.rebookRequested.emit(a);
  }

  protected onAddBreakAfter(a: AppointmentPreviewDto): void {
    this.addBreakAfterRequested.emit(a);
  }

  // --- Zadatek: generowanie / wysyłka / kopiowanie linku ---

  protected generateDepositLink(): void {
    const id = this.appointment()?.id;
    if (!id || this.generatingDeposit()) return;
    this.generatingDeposit.set(true);
    this.depositsClient
      .generateLink(id, { amountOverride: undefined })
      .pipe(finalize(() => this.generatingDeposit.set(false)))
      .subscribe({
        next: (result) => {
          // Strażnik wyścigu: jeśli w trakcie żądania przełączono wizytę, nie pokazuj
          // linku dla poprzedniej wizyty.
          if (this.appointment()?.id !== id) return;
          this.generatedLink.set(result);
          // Odśwież pełne dane wizyty, by utrwalony link/status zadatku był spójny po powrocie
          // do wizyty (fullDetail to źródło prawdy dla linku po przeładowaniu komponentu).
          this.reloadDetail();
          void this.copyToClipboard(result.url ?? '');
        },
        error: () => {
          /* komunikat pokazuje errorInterceptor (mapowanie kodów na PL) */
        },
      });
  }

  protected sendDepositLink(): void {
    const id = this.appointment()?.id;
    if (!id || this.sendingDeposit()) return;

    const resend = this.depositLinkSentAt() != null;
    this.confirmationService.confirm({
      header: resend ? 'Wysłać link ponownie?' : 'Wysłać link do zadatku?',
      message: resend
        ? 'Klient dostał już ten link. Wyślemy go jeszcze raz — na numer lub e-mail zapisany w ustawieniach salonu.'
        : 'Klient dostanie link do zapłaty zadatku — na numer lub e-mail zapisany w ustawieniach salonu.',
      icon: 'pi pi-send',
      acceptLabel: resend ? 'Wyślij ponownie' : 'Wyślij',
      rejectLabel: 'Anuluj',
      rejectButtonStyleClass: 'p-button-outlined p-button-secondary',
      accept: () => this.performSendDepositLink(id),
    });
  }

  private performSendDepositLink(id: string): void {
    this.sendingDeposit.set(true);
    this.depositsClient
      .send(id, { channel: undefined })
      .pipe(finalize(() => this.sendingDeposit.set(false)))
      .subscribe({
        next: (result) => {
          // Odświeżamy szczegóły, żeby wciągnąć znacznik wysyłki (depositLinkSentAtUtc) —
          // dopiero on odróżnia „wysłano" od „kliknięto wyślij".
          this.reloadDetail();
          this.messages.add({
            severity: 'success',
            summary: 'Wysłano',
            detail:
              result.channel === 'Phone'
                ? 'Link do zadatku został wysłany SMS-em.'
                : 'Link do zadatku został wysłany e-mailem.',
            life: 4000,
          });
        },
        error: () => {
          /* errorInterceptor — mapuje deposit.send_failed na toast „Nie wysłano" */
        },
      });
  }

  protected copyDepositLink(): void {
    void this.copyToClipboard(this.depositLinkUrl() ?? '');
  }

  private async copyToClipboard(url: string): Promise<void> {
    if (!url) return;
    const ok = await this.writeToClipboard(url);
    this.messages.add(
      ok
        ? {
            severity: 'success',
            summary: 'Skopiowano',
            detail: 'Link do zadatku skopiowany do schowka.',
            life: 3000,
          }
        : {
            severity: 'warn',
            summary: 'Nie udało się skopiować',
            detail: 'Zaznacz link powyżej i skopiuj ręcznie.',
            life: 4000,
          },
    );
  }

  /**
   * Kopiowanie odporne na brak secure context. `navigator.clipboard` istnieje tylko po https/localhost
   * — po http w LAN (demo na telefonie) trzeba fallbacku na ukryty `<textarea>` + `execCommand('copy')`
   * (z selekcją zgodną z iOS Safari). Zwraca true gdy się udało.
   */
  private async writeToClipboard(text: string): Promise<boolean> {
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return true;
      }
    } catch {
      /* przejdź do fallbacku poniżej */
    }
    try {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.contentEditable = 'true';
      ta.style.position = 'fixed';
      ta.style.top = '-1000px';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.focus();
      // iOS Safari ignoruje samo .select() — wymaga jawnego zakresu zaznaczenia.
      const range = document.createRange();
      range.selectNodeContents(ta);
      const sel = window.getSelection();
      sel?.removeAllRanges();
      sel?.addRange(range);
      ta.setSelectionRange(0, text.length);
      const ok = document.execCommand('copy');
      sel?.removeAllRanges();
      document.body.removeChild(ta);
      return ok;
    } catch {
      return false;
    }
  }

  protected onVisibleChange(next: boolean): void {
    if (!next && this.appointment() != null) {
      this.closeRequested.emit();
    }
  }

  protected dateLabel(a: AppointmentPreviewDto): string {
    if (!a.date) return '';
    const d = new Date(a.date as unknown as string);
    if (Number.isNaN(d.getTime())) return '';
    return d.toLocaleDateString('pl-PL', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
  }

  protected timeRange(a: AppointmentPreviewDto): string {
    const start = (a.startTime ?? '').substring(0, 5);
    const end = (a.endTime ?? '').substring(0, 5);
    return start && end ? `${start} – ${end}` : start || end || '—';
  }

  /** Długość wizyty jako „X godz Y min" / „X godz" / „Y min" — kontekst przy godzinie. */
  protected durationLabel(a: AppointmentPreviewDto): string | null {
    const s = this.timeToMinutes(a.startTime);
    const e = this.timeToMinutes(a.endTime);
    if (s == null || e == null || e <= s) return null;
    const mins = e - s;
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    if (h && m) return `${h} godz ${m} min`;
    return h ? `${h} godz` : `${m} min`;
  }

  private timeToMinutes(t: string | undefined): number | null {
    if (!t) return null;
    const [hh, mm] = t.split(':');
    const h = Number(hh);
    const m = Number(mm);
    return Number.isFinite(h) && Number.isFinite(m) ? h * 60 + m : null;
  }

  protected customerLine(a: AppointmentPreviewDto): string {
    if (a.isGuest === true) return 'Gość';
    const name = [a.customerFirstName, a.customerLastName]
      .filter((s): s is string => !!s && s.trim() !== '')
      .join(' ');
    return name || a.customerPhoneNumber || 'Klient';
  }

  protected phone(a: AppointmentPreviewDto): string | null {
    const ph = a.customerPhoneNumber?.trim();
    return ph ? ph : null;
  }

  protected employeeLine(a: AppointmentPreviewDto): string | null {
    const name = [a.employeeFirstName, a.employeeLastName]
      .filter((s): s is string => !!s && s.trim() !== '')
      .join(' ');
    return name || null;
  }
}

/**
 * Czas trwania po polsku, z dokładnością do minuty: „21 godz. 14 min", „43 min", „2 dni 3 godz.".
 * Zwraca null poniżej minuty — wołający decyduje, jak nazwać „już zaraz".
 */
function formatDuration(ms: number): string | null {
  const totalMinutes = Math.floor(ms / 60_000);
  if (totalMinutes < 1) return null;

  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  const minutes = totalMinutes % 60;

  if (days > 0) {
    const dayLabel = days === 1 ? '1 dzień' : `${days} dni`;
    return hours > 0 ? `${dayLabel} ${hours} godz.` : dayLabel;
  }
  if (hours > 0) {
    return minutes > 0 ? `${hours} godz. ${minutes} min` : `${hours} godz.`;
  }
  return `${minutes} min`;
}
