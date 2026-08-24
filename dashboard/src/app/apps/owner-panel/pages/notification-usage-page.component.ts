import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { DatePicker } from 'primeng/datepicker';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import {
  NotificationsClient,
  NotificationUsageByTypeDto,
  NotificationUsageDto,
} from '@core/api/api-client';

/** Etykiety typów powiadomień (mapuje NotificationType z backendu, wartości 1–14). */
const TYPE_LABELS: Record<number, string> = {
  1: 'Nowa rezerwacja → salon',
  2: 'Potwierdzenie → klient',
  3: 'Anulowanie → salon',
  4: 'Anulowanie → klient',
  5: 'Przełożenie → salon',
  6: 'Przełożenie → klient',
  7: 'Przypomnienie 24h → klient',
  8: 'Czeka na potwierdzenie → salon',
  9: 'Salon odwołał → klient',
  10: 'Salon przełożył → klient',
  11: 'Przypomnienie 2h → klient',
  12: 'Weryfikacja klienta (OTP)',
  13: 'Wizyta wystawiona przez salon → klient',
  14: 'Link do zadatku → klient',
};

/** Wiersz tabeli rozbicia po typie, z gotowymi etykietami do widoku. */
interface UsageRow {
  channelLabel: string;
  channelIcon: string;
  typeLabel: string;
  sentCount: number;
  failedCount: number;
  points: number;
}

@Component({
  selector: 'app-notification-usage-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePicker, ProgressSpinnerModule, DecimalPipe],
  template: `
    <div class="min-h-screen px-4 pb-12 pt-4 sm:px-8 sm:pt-8">
      <div class="max-w-3xl mx-auto space-y-6">
        <header class="admin-glass-card rounded-4xl px-6 py-8 sm:px-8 sm:py-9">
          <span class="admin-section-label text-primary mb-3 block">
            Panel właściciela
          </span>
          <h1
            class="text-3xl sm:text-4xl font-black tracking-tight text-surface-900 leading-tight mb-2"
          >
            Zużycie SMS i e-mail
          </h1>
          <p class="text-surface-700 dark:text-surface-300 text-sm sm:text-base leading-relaxed">
            Ile wiadomości wysłaliśmy w wybranym miesiącu i ile SMS-ów zostało Ci w ramach planu.
            Powiadomienia in-app są darmowe i nie liczą się do limitu.
          </p>

          <div class="mt-5 flex items-center gap-3">
            <span class="text-xs font-bold uppercase tracking-wider text-surface-500">Miesiąc</span>
            <p-date-picker
              [ngModel]="selectedMonth()"
              (ngModelChange)="onMonthPicked($event)"
              view="month"
              dateFormat="mm/yy"
              [showIcon]="true"
              [readonlyInput]="true"
              [maxDate]="today"
              appendTo="body"
              styleClass="w-40"
              inputStyleClass="!text-sm !font-bold"
            />
          </div>
        </header>

        @if (usage.isLoading()) {
          <div class="flex flex-col items-center justify-center py-20 gap-4">
            <p-progressSpinner styleClass="w-12 h-12" />
            <span class="text-surface-500 text-sm">Ładowanie zużycia…</span>
          </div>
        } @else if (usage.error()) {
          <div
            class="rounded-xl border border-red-200 dark:border-red-900/50 bg-red-50 dark:bg-red-950/20 p-6 text-red-800 dark:text-red-200 text-sm"
          >
            Nie udało się wczytać zużycia. Sprawdź połączenie z API albo uprawnienia (wymagana rola
            właściciela lub managera).
          </div>
        } @else if (usage.value(); as u) {
          <!-- Karty podsumowania -->
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <!-- SMS -->
            <div
              data-testid="usage-card-sms"
              data-tour="usage-sms"
              class="admin-glass-card rounded-3xl p-6 space-y-4"
            >
              <div class="flex items-center gap-3">
                <span
                  class="inline-flex h-10 w-10 items-center justify-center rounded-2xl bg-amber-100 dark:bg-amber-900/40"
                >
                  <i class="pi pi-comment text-lg text-amber-600 dark:text-amber-300"></i>
                </span>
                <div>
                  <h2 class="text-sm font-black text-surface-900">SMS</h2>
                  <p class="text-xs text-surface-500">Wysłane w tym miesiącu</p>
                </div>
              </div>

              <div class="flex items-end gap-2">
                <span class="text-4xl font-black text-surface-900">{{
                  u.smsCreditsUsed ?? 0
                }}</span>
                <span class="text-sm text-surface-500 mb-1.5"
                  >/ {{ u.smsAllowance ?? 0 }} SMS w planie</span
                >
              </div>

              <!-- Pasek limitu -->
              <div class="space-y-1.5">
                <div class="h-2.5 w-full rounded-full bg-surface-200 dark:bg-surface-200 overflow-hidden">
                  <div
                    class="h-full rounded-full transition-all"
                    [class.bg-amber-500]="!isOverLimit(u)"
                    [class.bg-red-500]="isOverLimit(u)"
                    [style.width.%]="usagePercent(u)"
                  ></div>
                </div>
                @if (isOverLimit(u)) {
                  <p class="text-xs font-bold text-red-600 dark:text-red-400">
                    Przekroczono limit o {{ u.smsOverageCount ?? 0 }} SMS — dopłata
                    {{ formatGrosze(u.overageCostGrosze ?? 0) }}
                  </p>
                } @else {
                  <p class="text-xs text-surface-500">
                    Pozostało {{ u.smsRemaining ?? 0 }} SMS w ramach planu
                  </p>
                }
              </div>

              <div class="flex flex-wrap gap-x-6 gap-y-1 text-xs text-surface-500 pt-1">
                <span>Wiadomości: <strong class="text-surface-700 dark:text-surface-300">{{ u.smsSent ?? 0 }}</strong></span>
                <span>Punkty smsapi.pl: <strong class="text-surface-700 dark:text-surface-300">{{ (u.smsPoints ?? 0) | number: '1.0-2' }}</strong></span>
                @if ((u.smsFailed ?? 0) > 0) {
                  <span class="text-red-500">Nieudane: {{ u.smsFailed }}</span>
                }
              </div>
            </div>

            <!-- E-mail -->
            <div
              data-testid="usage-card-email"
              data-tour="usage-email"
              class="admin-glass-card rounded-3xl p-6 space-y-4"
            >
              <div class="flex items-center gap-3">
                <span
                  class="inline-flex h-10 w-10 items-center justify-center rounded-2xl bg-sky-100 dark:bg-sky-900/40"
                >
                  <i class="pi pi-envelope text-lg text-sky-600 dark:text-sky-300"></i>
                </span>
                <div>
                  <h2 class="text-sm font-black text-surface-900">E-mail</h2>
                  <p class="text-xs text-surface-500">Wysłane w tym miesiącu</p>
                </div>
              </div>

              <div class="flex items-end gap-2">
                <span class="text-4xl font-black text-surface-900">{{
                  u.emailSent ?? 0
                }}</span>
                <span class="text-sm text-surface-500 mb-1.5">wiadomości</span>
              </div>

              <p class="text-xs text-surface-500">
                E-mail jest bez limitu i nie wpływa na koszt planu.
                @if ((u.emailFailed ?? 0) > 0) {
                  <span class="text-red-500 block mt-1">Nieudane: {{ u.emailFailed }}</span>
                }
              </p>
            </div>
          </div>

          <!-- Rozbicie po typach -->
          <div class="admin-glass-card rounded-3xl p-6">
            <h2 class="text-sm font-black text-surface-900 mb-4">
              Rozbicie po typach powiadomień
            </h2>

            @if (rows().length === 0) {
              <p class="text-sm text-surface-500 py-6 text-center">
                Brak wysłanych wiadomości w tym miesiącu.
              </p>
            } @else {
              <div class="overflow-x-auto">
                <table class="w-full text-sm">
                  <thead>
                    <tr class="text-left text-xs uppercase tracking-wider text-surface-500 border-b border-surface-200 dark:border-surface-200">
                      <th class="py-2 pr-3 font-bold">Kanał</th>
                      <th class="py-2 pr-3 font-bold">Typ</th>
                      <th class="py-2 px-3 font-bold text-right">Wysłane</th>
                      <th class="py-2 px-3 font-bold text-right">Punkty</th>
                      <th class="py-2 pl-3 font-bold text-right">Błędy</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of rows(); track row.typeLabel + row.channelLabel) {
                      <tr class="border-b border-surface-100 dark:border-surface-100 last:border-0">
                        <td class="py-2.5 pr-3">
                          <span class="inline-flex items-center gap-1.5 font-bold text-surface-700 dark:text-surface-300">
                            <i [class]="row.channelIcon" class="text-xs"></i>
                            {{ row.channelLabel }}
                          </span>
                        </td>
                        <td class="py-2.5 pr-3 text-surface-700 dark:text-surface-300">{{ row.typeLabel }}</td>
                        <td class="py-2.5 px-3 text-right font-black text-surface-900">{{ row.sentCount }}</td>
                        <td class="py-2.5 px-3 text-right text-surface-500">{{ row.points | number: '1.0-2' }}</td>
                        <td class="py-2.5 pl-3 text-right" [class.text-red-500]="row.failedCount > 0">{{ row.failedCount || '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
})
export class NotificationUsagePageComponent {
  private readonly notifications = inject(NotificationsClient);

  protected readonly today = new Date();

  /** Wybrany miesiąc jako Date (zakotwiczony na 1. dniu). Domyślnie bieżący miesiąc. */
  protected readonly selectedMonth = signal<Date>(
    new Date(new Date().getFullYear(), new Date().getMonth(), 1),
  );

  readonly usage = rxResource<NotificationUsageDto | undefined, string>({
    // Klucz stringowy „rok-miesiąc" — obiektowy literał miał nową tożsamość przy każdym
    // przeliczeniu, więc zużycie SMS-ów przeładowywało się bez zmiany miesiąca.
    params: () => `${this.selectedMonth().getFullYear()}-${this.selectedMonth().getMonth() + 1}`,
    stream: ({ params }) => {
      const [year, month] = params.split('-').map(Number);
      return this.notifications.getUsage(year, month);
    },
    defaultValue: undefined,
  });

  /** Wiersze tabeli — typy z największą liczbą wysyłek na górze. */
  readonly rows = computed<UsageRow[]>(() => {
    const byType = this.usage.value()?.byType ?? [];
    return [...byType]
      .map((t: NotificationUsageByTypeDto) => ({
        channelLabel: t.channel === 2 ? 'SMS' : 'E-mail',
        channelIcon: t.channel === 2 ? 'pi pi-comment' : 'pi pi-envelope',
        typeLabel: TYPE_LABELS[t.type ?? 0] ?? `Typ ${t.type}`,
        sentCount: t.sentCount ?? 0,
        failedCount: t.failedCount ?? 0,
        points: t.points ?? 0,
      }))
      .sort((a, b) => b.sentCount - a.sentCount);
  });

  onMonthPicked(date: Date | null): void {
    if (!date) {
      return;
    }
    this.selectedMonth.set(new Date(date.getFullYear(), date.getMonth(), 1));
  }

  isOverLimit(u: NotificationUsageDto): boolean {
    return (u.smsOverageCount ?? 0) > 0;
  }

  usagePercent(u: NotificationUsageDto): number {
    const allowance = u.smsAllowance ?? 0;
    const used = u.smsCreditsUsed ?? 0;
    if (allowance <= 0) {
      return used > 0 ? 100 : 0;
    }
    return Math.min(100, Math.round((used / allowance) * 100));
  }

  formatGrosze(grosze: number): string {
    return (grosze / 100).toLocaleString('pl-PL', {
      style: 'currency',
      currency: 'PLN',
    });
  }
}
