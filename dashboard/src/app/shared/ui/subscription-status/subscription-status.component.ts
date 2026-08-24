import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { EMPTY } from 'rxjs';
import { SubscriptionClient, SubscriptionInfoDto } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';

/**
 * Karta statusu subskrypcji w sidebarze. Pokazuje:
 *  - Trial → liczbę dni do końca + datę
 *  - Active → liczbę stanowisk + cenę miesięczną (z założeniem Founding Member, jeśli aktywne)
 *  - PastDue → ostrzeżenie z CTA "opłać dalszy okres"
 *  - Canceled → komunikat informacyjny
 *
 * Cena formatowana z `monthlyPriceInGrosze` (groszowa precyzja → display zł).
 */
@Component({
  selector: 'app-subscription-status',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (info.value(); as sub) {
      <div class="mx-4 mb-4 rounded-2xl border px-4 py-3 text-xs leading-snug"
        [class]="containerClass(sub.effectiveStatus)">

        <div class="flex items-center justify-between gap-2 mb-1">
          <span class="font-black uppercase tracking-widest text-[10px]">
            {{ statusLabel(sub.effectiveStatus) }}
          </span>
          @if (sub.isTrialActive) {
            <span class="font-bold opacity-70">{{ sub.daysRemainingInTrial }} dni</span>
          } @else if (sub.effectiveStatus === 'Active') {
            <span class="font-bold opacity-70">{{ sub.seats }} {{ seatLabel(sub.seats ?? 1) }}</span>
          }
        </div>

        @if (sub.isTrialActive) {
          <p class="opacity-80">Wersja próbna — pełny dostęp do {{ formatDate(sub.trialEndsAt) }}</p>
        } @else if (sub.effectiveStatus === 'Active') {
          <p class="opacity-80">
            @if (sub.baseMonthlyPriceInGrosze && sub.monthlyPriceInGrosze !== sub.baseMonthlyPriceInGrosze) {
              <span class="line-through opacity-50 mr-1">{{ formatPrice(sub.baseMonthlyPriceInGrosze) }}</span>
            }
            <span class="font-bold">{{ formatPrice(sub.monthlyPriceInGrosze) }} / mies.</span>
            @if (sub.isFoundingMember) {
              <span class="ml-1 inline-block rounded-md bg-amber-500/15 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide text-amber-700 dark:text-amber-300">FM</span>
            }
          </p>
          @if (sub.currentPeriodEndsAt) {
            <p class="opacity-60 mt-0.5">Odnowienie {{ formatDate(sub.currentPeriodEndsAt) }}</p>
          }
        } @else if (sub.effectiveStatus === 'PastDue') {
          <p class="font-semibold">Konto wymaga aktualizacji — opłać kolejny okres, aby przywrócić dostęp do publicznej rezerwacji.</p>
        } @else if (sub.effectiveStatus === 'Canceled') {
          <p class="opacity-80">Subskrypcja anulowana. Reaktywuj, aby wrócić do działania.</p>
        }

        @if (sub.activePromoCode; as promo) {
          <div class="mt-2 pt-2 border-t border-current/15 text-[11px] opacity-80">
            Aktywny rabat:
            <code class="font-mono font-bold">{{ promo.code }}</code>
            <span class="opacity-70">— {{ promoLabel(promo.discountType, promo.discountValue) }}</span>
            @if (promo.expiresAt) {
              <span class="opacity-60"> do {{ formatDate(promo.expiresAt) }}</span>
            }
          </div>
        }
      </div>
    }
  `,
})
export class SubscriptionStatusComponent {
  private readonly client = inject(SubscriptionClient);
  private readonly auth = inject(AuthSessionService);

  // SystemAdmin nie ma subskrypcji — endpoint /api/Subscription wymaga StaffManagement.
  // Bez tego guarda komponent renderuje się w sidebarze i strzela 403 przy każdym wejściu admina.
  private readonly isSystemAdmin = computed(() => this.auth.currentRole() === 'systemAdmin');

  info = rxResource<SubscriptionInfoDto | null, boolean>({
    params: () => this.isSystemAdmin(),
    stream: ({ params: isAdmin }) => isAdmin ? EMPTY : this.client.get(),
  });

  statusLabel(status: string | undefined): string {
    switch (status) {
      case 'Trial': return 'Wersja próbna';
      case 'Active': return 'Plan aktywny';
      case 'PastDue': return 'Wymagana płatność';
      case 'Canceled': return 'Subskrypcja anulowana';
      default: return status ?? '';
    }
  }

  containerClass(status: string | undefined): string {
    if (status === 'Active') return 'border-emerald-200 bg-emerald-50/80 text-emerald-900 dark:border-emerald-800/50 dark:bg-emerald-950/30 dark:text-emerald-100';
    if (status === 'Trial') return 'border-violet-200 bg-violet-50/80 text-violet-900 dark:border-violet-800/50 dark:bg-violet-950/30 dark:text-violet-100';
    if (status === 'PastDue') return 'border-amber-300 bg-amber-50/80 text-amber-900 dark:border-amber-700/60 dark:bg-amber-950/40 dark:text-amber-100';
    if (status === 'Canceled') return 'border-red-200 bg-red-50/70 text-red-900 dark:border-red-800/50 dark:bg-red-950/30 dark:text-red-100';
    return 'border-surface-200 bg-surface-50/80 text-surface-700 dark:border-surface-200/50 dark:bg-surface-100/30 dark:text-surface-300';
  }

  seatLabel(n: number): string {
    if (n === 1) return 'stanowisko';
    const lastTwo = n % 100;
    const last = n % 10;
    if (lastTwo >= 12 && lastTwo <= 14) return 'stanowisk';
    if (last >= 2 && last <= 4) return 'stanowiska';
    return 'stanowisk';
  }

  formatPrice(grosze: number | undefined): string {
    if (grosze == null) return '';
    const zl = grosze / 100;
    return new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN', maximumFractionDigits: 0 }).format(zl);
  }

  formatDate(date: Date | undefined): string {
    if (!date) return '';
    return new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' }).format(new Date(date));
  }

  promoLabel(type: string | undefined, value: number | undefined): string {
    if (type == null || value == null) return 'rabat aktywny';
    switch (type) {
      case 'PriceOverride': return `cena bazowa ${value.toFixed(0)} zł`;
      case 'PercentOff':    return `${value.toFixed(0)}% zniżki`;
      case 'FreeMonths':    return `${value.toFixed(0)} mies. gratis`;
      case 'TrialExtension': return `+${value.toFixed(0)} dni trialu`;
      default: return 'rabat aktywny';
    }
  }
}
