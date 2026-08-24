import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { rxResource } from '@angular/core/rxjs-interop';
import { lastValueFrom, of } from 'rxjs';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { InputNumberModule } from 'primeng/inputnumber';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import {
  DepositInstrument,
  DepositMode,
  MerchantOnboardingStatus,
  SalonSettingsClient,
  TenantDto,
} from '@core/api/api-client';

/**
 * Panel „Zadatki" w ustawieniach salonu. Dwie karty:
 *  1. Konto płatności (Stripe) — onboarding + status (steruje dostępnością zadatków).
 *  2. Ustawienia zadatku — tryb (procent/stała), wartość, instrument prawny.
 *
 * Ustawienia zadatku są zablokowane dopóki konto Stripe nie przyjmuje płatności
 * (najpierw konto, potem reguły) — spójnie z fallbackiem backendu.
 */
@Component({
  selector: 'app-deposits-settings-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ButtonModule, SelectModule, InputNumberModule, ToggleSwitchModule],
  template: `
    <div class="space-y-6">
      <!-- Karta: Konto płatności (Stripe) -->
      <div data-tour="deposits-stripe" class="admin-glass-card rounded-3xl p-6 space-y-4">
        <div class="flex items-start justify-between gap-4">
          <div>
            <h2 class="admin-h2 text-surface-900 mb-1">Konto płatności (Stripe)</h2>
            <p class="text-sm text-surface-600 dark:text-surface-300 max-w-xl">
              Zadatki trafiają bezpośrednio na Twoje konto Stripe — my niczego nie przechowujemy.
              Połącz konto, aby móc generować linki do zapłaty.
            </p>
          </div>
          @if (statusBadge(); as b) {
            <span class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-bold whitespace-nowrap"
              [class]="b.cls">
              <i [class]="b.icon"></i>{{ b.label }}
            </span>
          }
        </div>

        @if (merchant.isLoading()) {
          <div class="flex items-center gap-2 text-sm text-surface-500">
            <i class="pi pi-spin pi-spinner"></i> Sprawdzam stan konta…
          </div>
        } @else if (!canAcceptPayments()) {
          <p class="text-sm text-amber-700 dark:text-amber-300">
            {{ merchant.value()?.connected ? 'Dokończ konfigurację konta Stripe, aby przyjmować zadatki.' : 'Konto nie jest jeszcze połączone.' }}
          </p>
        } @else {
          <p class="text-sm text-emerald-700 dark:text-emerald-300">
            <i class="pi pi-check-circle"></i> Konto gotowe — możesz generować zadatki na wizytach.
          </p>
        }

        <p-button
          data-tour="deposits-stripe-connect"
          [label]="merchant.value()?.connected ? 'Otwórz konfigurację Stripe' : 'Połącz konto Stripe'"
          icon="pi pi-external-link"
          [loading]="connecting()"
          (onClick)="connectStripe()"
        />
      </div>

      <!-- Karta: Ustawienia zadatku -->
      <div class="admin-glass-card rounded-3xl p-6 space-y-5" [class.opacity-60]="!canAcceptPayments()">
        <div>
          <h2 class="admin-h2 text-surface-900 mb-1">Ustawienia zadatku</h2>
          <p class="text-sm text-surface-600 dark:text-surface-300 max-w-xl">
            Domyślna kwota prefillowana przy generowaniu linku (możesz ją zmienić przy każdej wizycie)
            oraz nazwa prawna pobieranej opłaty.
          </p>
        </div>

        @if (!canAcceptPayments()) {
          <p class="text-sm text-amber-700 dark:text-amber-300">
            Najpierw połącz i aktywuj konto Stripe powyżej.
          </p>
        }

        <div data-tour="deposits-toggle" class="flex items-center justify-between rounded-2xl border border-surface-200 dark:border-surface-200 px-4 py-3">
          <div>
            <p class="font-semibold text-surface-900">Pobieraj zadatki</p>
            <p class="text-xs text-surface-500">Włącza akcję „Generuj zadatek" na wizytach.</p>
          </div>
          <p-toggleswitch [(ngModel)]="enabled" [disabled]="!canAcceptPayments()" />
        </div>

        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div class="space-y-1.5">
            <label class="text-xs font-bold uppercase tracking-wider text-surface-500">Sposób naliczania</label>
            <p-select
              [options]="modeOptions"
              [(ngModel)]="mode"
              optionLabel="label"
              optionValue="value"
              [disabled]="!enabled() || !canAcceptPayments()"
              styleClass="w-full"
            />
          </div>
          <div class="space-y-1.5">
            <label class="text-xs font-bold uppercase tracking-wider text-surface-500">
              {{ mode() === DepositMode.Percentage ? 'Procent ceny' : 'Kwota' }}
            </label>
            <p-inputnumber
              [(ngModel)]="value"
              [min]="0"
              [max]="mode() === DepositMode.Percentage ? 100 : 100000"
              [suffix]="mode() === DepositMode.Percentage ? ' %' : ' zł'"
              [disabled]="!enabled() || !canAcceptPayments()"
              styleClass="w-full"
              inputStyleClass="w-full"
            />
          </div>
        </div>

        <div class="space-y-1.5">
          <label class="text-xs font-bold uppercase tracking-wider text-surface-500">Instrument prawny</label>
          <p-select
            [options]="instrumentOptions"
            [(ngModel)]="instrument"
            optionLabel="label"
            optionValue="value"
            [disabled]="!enabled() || !canAcceptPayments()"
            styleClass="w-full"
          />
          <p class="text-xs text-surface-500">
            „Zadatek" (art. 394 KC) pozwala zatrzymać kwotę przy nieobecności klienta — wymaga klauzuli
            w Twoim regulaminie. „Zaliczka" jest zawsze zwrotna.
          </p>
        </div>

        @if (preview(); as p) {
          <p class="text-sm text-surface-600 dark:text-surface-300">
            Przykład: dla usługi za <strong>100 zł</strong> zadatek wyniesie <strong>{{ p }}</strong>.
          </p>
        }

        <div class="pt-2">
          <p-button
            data-tour="deposits-save"
            label="Zapisz"
            icon="pi pi-check"
            [loading]="saving()"
            [disabled]="settings.isLoading()"
            (onClick)="save()"
          />
        </div>
      </div>
    </div>
  `,
})
export class DepositsSettingsPageComponent {
  private readonly client = inject(SalonSettingsClient);
  private readonly messages = inject(MessageService);

  protected readonly DepositMode = DepositMode;

  protected readonly modeOptions = [
    { label: 'Procent ceny usługi', value: DepositMode.Percentage },
    { label: 'Kwota stała', value: DepositMode.Fixed },
  ];
  protected readonly instrumentOptions = [
    { label: 'Zadatek (art. 394 KC)', value: DepositInstrument.Zadatek },
    { label: 'Zaliczka', value: DepositInstrument.Zaliczka },
    { label: 'Opłata rezerwacyjna', value: DepositInstrument.OplataRezerwacyjna },
  ];

  protected readonly enabled = signal(false);
  protected readonly mode = signal<DepositMode>(DepositMode.Percentage);
  protected readonly value = signal<number>(30);
  protected readonly instrument = signal<DepositInstrument>(DepositInstrument.Zadatek);

  protected readonly connecting = signal(false);
  protected readonly saving = signal(false);

  /** Ładuje ustawienia salonu i inicjuje pola formularza zadatku. */
  protected readonly settings = rxResource({
    params: () => true,
    stream: () => this.client.get(),
  });

  /** Odświeża stan konta Stripe u operatora. */
  protected readonly merchant = rxResource({
    params: () => true,
    stream: () => this.client.getMerchantAccountStatus(),
  });

  private hydrated = false;

  constructor() {
    // Inicjalizacja pól, gdy TenantDto dotrze (raz).
    effect(() => {
      const d = this.settings.value()?.depositSettings;
      if (!d || this.hydrated) return;
      this.hydrated = true;
      this.enabled.set(d.enabled ?? false);
      this.mode.set(d.mode ?? DepositMode.Percentage);
      this.value.set(d.value ?? 30);
      this.instrument.set(d.instrument ?? DepositInstrument.Zadatek);
    });
  }

  protected readonly canAcceptPayments = computed(() => this.merchant.value()?.canAcceptPayments === true);

  protected readonly statusBadge = computed(() => {
    const m = this.merchant.value();
    if (!m || !m.connected) {
      return { label: 'Niepołączone', icon: 'pi pi-circle', cls: 'bg-surface-100 text-surface-600 dark:bg-surface-100 dark:text-surface-300' };
    }
    if (m.canAcceptPayments) {
      return { label: 'Aktywne', icon: 'pi pi-check-circle', cls: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300' };
    }
    return { label: 'W trakcie', icon: 'pi pi-clock', cls: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300' };
  });

  protected readonly preview = computed(() => {
    if (!this.enabled()) return null;
    const v = this.value() ?? 0;
    const amount = this.mode() === DepositMode.Percentage ? Math.round(v) : Math.min(v, 100);
    return `${amount.toFixed(2)} zł`.replace('.', ',');
  });

  async connectStripe(): Promise<void> {
    if (this.connecting()) return;
    this.connecting.set(true);
    try {
      const res = await lastValueFrom(this.client.connectMerchantAccount());
      if (res.onboardingUrl) {
        window.location.href = res.onboardingUrl;
      }
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Stripe',
        detail: 'Nie udało się rozpocząć konfiguracji konta. Spróbuj ponownie.',
        life: 5000,
      });
      this.connecting.set(false);
    }
  }

  async save(): Promise<void> {
    const t = this.settings.value();
    if (!t || this.saving()) return;

    this.saving.set(true);
    try {
      await lastValueFrom(this.client.put(this.buildRequest(t)));
      this.messages.add({
        severity: 'success',
        summary: 'Zapisano',
        detail: 'Ustawienia zadatku zostały zapisane.',
        life: 4000,
      });
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Błąd zapisu',
        detail: 'Nie udało się zapisać ustawień zadatku.',
        life: 5000,
      });
    } finally {
      this.saving.set(false);
    }
  }

  /** Round-trip read-modify-write: odsyłamy wszystkie wymagane pola tenanta + nadpisane zadatki. */
  private buildRequest(t: TenantDto) {
    return {
      name: t.name,
      slug: t.slug,
      customerVerificationChannel: t.customerVerificationChannel,
      appointmentSlotStepMinutes: t.appointmentSlotStepMinutes,
      timeZoneId: t.timeZoneId,
      currency: t.currency,
      bookingAccessPolicy: t.bookingAccessPolicy,
      appointmentConfirmationMode: t.appointmentConfirmationMode,
      gapFillingSettings: t.gapFillingSettings,
      notificationSettings: t.notificationSettings,
      staffCalendarVisibilityPolicy: t.staffCalendarVisibilityPolicy,
      requireCustomerName: t.requireCustomerName,
      collectInstagramHandle: t.collectInstagramHandle,
      depositSettings: {
        enabled: this.enabled(),
        mode: this.mode(),
        value: this.value() ?? 0,
        instrument: this.instrument(),
      },
    } as Parameters<SalonSettingsClient['put']>[0];
  }
}
