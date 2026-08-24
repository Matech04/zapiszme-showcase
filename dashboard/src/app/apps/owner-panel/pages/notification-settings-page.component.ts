import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { CustomerVerificationChannel, SalonSettingsClient, TenantDto } from '@core/api/api-client';
import { MessageService } from 'primeng/api';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { Router } from '@angular/router';
import { lastValueFrom } from 'rxjs';

/** Preferencje powiadomień — jeden przełącznik on/off na typ (mapuje 1:1 na NotificationSettingsDto). */
interface NotificationSettingsModel {
  newBookingToSalon: boolean;
  bookingConfirmationToCustomer: boolean;
  cancellationToSalon: boolean;
  cancellationToCustomer: boolean;
  rescheduleToSalon: boolean;
  rescheduleToCustomer: boolean;
  appointmentReminderToCustomer: boolean;
  awaitingConfirmationToSalon: boolean;
  cancelledBySalonToCustomer: boolean;
  rescheduledBySalonToCustomer: boolean;
  appointmentReminder2hToCustomer: boolean;
  staffBookedAppointmentToCustomer: boolean;
}

interface NotificationToggleRow {
  key: keyof NotificationSettingsModel;
  title: string;
  description: string;
  icon: string;
}

@Component({
  selector: 'app-notification-settings-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormLayoutComponent, ProgressSpinnerModule],
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
            Powiadomienia
          </h1>
          <p class="text-surface-700 dark:text-surface-300 text-sm sm:text-base leading-relaxed">
            Zdecyduj, które powiadomienia mają być wysyłane. Wiadomości do salonu trafiają na e-mail
            i do panelu, a do klientów wysyłamy je kanałem wybranym w ustawieniach salonu.
          </p>
        </header>

        @if (settings.isLoading()) {
          <div class="flex flex-col items-center justify-center py-20 gap-4">
            <p-progressSpinner styleClass="w-12 h-12" />
            <span class="text-surface-500 text-sm">Ładowanie ustawień…</span>
          </div>
        } @else if (settings.error()) {
          <div
            class="rounded-xl border border-red-200 dark:border-red-900/50 bg-red-50 dark:bg-red-950/20 p-6 text-red-800 dark:text-red-200 text-sm"
          >
            Nie udało się wczytać ustawień. Sprawdź połączenie z API albo uprawnienia (wymagana rola
            właściciela lub managera).
          </div>
        } @else {
          <app-form-layout
            title="Powiadomienia"
            [isEdit]="true"
            testId="notification-settings"
            submitButtonLabel="Zapisz"
            (submit)="onSave()"
            (cancel)="onCancel()"
          >
            <div class="grid grid-cols-1 gap-6">
              <div
                data-testid="salon-notifications"
                data-tour="notifications-salon"
                class="rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50 dark:bg-surface-50/40 px-4 py-4 space-y-4"
              >
                <div class="space-y-2">
                  <span class="text-xs font-bold uppercase tracking-wider text-surface-500"
                    >Do salonu</span
                  >
                  <p class="text-xs text-surface-500 leading-relaxed">
                    Wysyłane na e-mail salonu i do panelu.
                  </p>
                  @for (row of notificationsToSalon; track row.key) {
                    <div
                      class="flex items-start gap-3 rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 px-4 py-3"
                    >
                      <span
                        class="pi {{ row.icon }} text-lg text-amber-600 dark:text-amber-400 mt-0.5"
                      ></span>
                      <div class="min-w-0 flex-1">
                        <span
                          class="block text-sm font-black text-surface-900"
                          >{{ row.title }}</span
                        >
                        <span class="block text-xs text-surface-500 mt-0.5 leading-relaxed">{{
                          row.description
                        }}</span>
                      </div>
                      <button
                        type="button"
                        role="switch"
                        [attr.data-testid]="'notif-toggle-' + row.key"
                        [attr.aria-checked]="isOn(row.key)"
                        [attr.aria-label]="row.title"
                        (click)="toggleNotification(row.key)"
                        class="relative h-6 w-11 shrink-0 mt-0.5 rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-amber-500 focus-visible:ring-offset-2"
                        [class.bg-amber-500]="isOn(row.key)"
                        [class.bg-surface-300]="!isOn(row.key)"
                        [class.dark:bg-surface-600]="!isOn(row.key)"
                      >
                        <span
                          class="absolute top-0.5 left-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform"
                          [class.translate-x-5]="isOn(row.key)"
                        ></span>
                      </button>
                    </div>
                  }
                </div>

                <div class="space-y-2">
                  <span class="text-xs font-bold uppercase tracking-wider text-surface-500"
                    >Do klienta</span
                  >
                  <p
                    class="rounded-lg bg-amber-50 dark:bg-amber-950/30 border border-amber-200 dark:border-amber-900/50 px-3 py-2 text-xs text-amber-800 dark:text-amber-200"
                    data-testid="customer-channel-info"
                    data-tour="notifications-customer"
                  >
                    Wiadomości do klientów wysyłamy przez <strong>{{ customerChannelLabel() }}</strong> —
                    zależnie od metody weryfikacji klienta (zmienisz ją w Ustawieniach salonu).
                  </p>
                  @for (row of notificationsToCustomer; track row.key) {
                    <div
                      class="flex items-start gap-3 rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 px-4 py-3"
                    >
                      <span
                        class="pi {{ row.icon }} text-lg text-amber-600 dark:text-amber-400 mt-0.5"
                      ></span>
                      <div class="min-w-0 flex-1">
                        <span
                          class="block text-sm font-black text-surface-900"
                          >{{ row.title }}</span
                        >
                        <span class="block text-xs text-surface-500 mt-0.5 leading-relaxed">{{
                          row.description
                        }}</span>
                      </div>
                      <button
                        type="button"
                        role="switch"
                        [attr.data-testid]="'notif-toggle-' + row.key"
                        [attr.aria-checked]="isOn(row.key)"
                        [attr.aria-label]="row.title"
                        (click)="toggleNotification(row.key)"
                        class="relative h-6 w-11 shrink-0 mt-0.5 rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-amber-500 focus-visible:ring-offset-2"
                        [class.bg-amber-500]="isOn(row.key)"
                        [class.bg-surface-300]="!isOn(row.key)"
                        [class.dark:bg-surface-600]="!isOn(row.key)"
                      >
                        <span
                          class="absolute top-0.5 left-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform"
                          [class.translate-x-5]="isOn(row.key)"
                        ></span>
                      </button>
                    </div>
                  }
                </div>
              </div>
            </div>
          </app-form-layout>
        }
      </div>
    </div>
  `,
})
export class NotificationSettingsPageComponent {
  private readonly salonClient = inject(SalonSettingsClient);
  private readonly messages = inject(MessageService);
  private readonly router = inject(Router);

  readonly settings = rxResource<TenantDto | undefined, undefined>({
    stream: () => this.salonClient.get(),
    defaultValue: undefined,
  });

  /** Kanał komunikacji z klientem = metoda weryfikacji (Phone → SMS, Email → e-mail). */
  readonly customerChannelLabel = computed(() =>
    this.settings.value()?.customerVerificationChannel === CustomerVerificationChannel.Email
      ? 'e-mail'
      : 'SMS',
  );

  readonly notificationSettings = signal<NotificationSettingsModel>({
    newBookingToSalon: true,
    bookingConfirmationToCustomer: true,
    cancellationToSalon: true,
    cancellationToCustomer: true,
    rescheduleToSalon: true,
    rescheduleToCustomer: true,
    appointmentReminderToCustomer: true,
    awaitingConfirmationToSalon: true,
    cancelledBySalonToCustomer: true,
    rescheduledBySalonToCustomer: true,
    appointmentReminder2hToCustomer: true,
    staffBookedAppointmentToCustomer: true,
  });

  readonly notificationsToSalon: NotificationToggleRow[] = [
    {
      key: 'newBookingToSalon',
      title: 'Nowa rezerwacja online',
      description: 'Gdy klient zarezerwuje wizytę przez publiczną stronę salonu.',
      icon: 'pi-calendar-plus',
    },
    {
      key: 'cancellationToSalon',
      title: 'Anulowanie przez klienta',
      description: 'Gdy klient sam anuluje wizytę w panelu samoobsługi.',
      icon: 'pi-times-circle',
    },
    {
      key: 'rescheduleToSalon',
      title: 'Przełożenie przez klienta',
      description: 'Gdy klient sam zmieni termin swojej wizyty.',
      icon: 'pi-refresh',
    },
    {
      key: 'awaitingConfirmationToSalon',
      title: 'Rezerwacja czeka na potwierdzenie',
      description: 'Tryb ręczny: nowa rezerwacja online wymaga zatwierdzenia przez salon.',
      icon: 'pi-hourglass',
    },
  ];

  readonly notificationsToCustomer: NotificationToggleRow[] = [
    {
      key: 'bookingConfirmationToCustomer',
      title: 'Potwierdzenie rezerwacji',
      description: 'Wiadomość do klienta po potwierdzeniu wizyty.',
      icon: 'pi-check-circle',
    },
    {
      key: 'staffBookedAppointmentToCustomer',
      title: 'Wizyta wystawiona przez salon',
      description: 'Wiadomość do klienta, gdy pracownik ręcznie doda mu wizytę w panelu.',
      icon: 'pi-calendar-plus',
    },
    {
      key: 'cancellationToCustomer',
      title: 'Potwierdzenie anulowania',
      description: 'Wiadomość do klienta, gdy sam anuluje wizytę.',
      icon: 'pi-times-circle',
    },
    {
      key: 'rescheduleToCustomer',
      title: 'Potwierdzenie przełożenia',
      description: 'Wiadomość do klienta po samodzielnej zmianie terminu wizyty.',
      icon: 'pi-refresh',
    },
    {
      key: 'cancelledBySalonToCustomer',
      title: 'Odwołanie wizyty przez salon',
      description: 'Wiadomość do klienta, gdy salon odwoła lub odrzuci jego wizytę.',
      icon: 'pi-ban',
    },
    {
      key: 'rescheduledBySalonToCustomer',
      title: 'Zmiana terminu przez salon',
      description: 'Wiadomość do klienta, gdy salon przełoży jego wizytę.',
      icon: 'pi-calendar-clock',
    },
    {
      key: 'appointmentReminderToCustomer',
      title: 'Przypomnienie 24h przed wizytą',
      description: 'Przypomnienie wysyłane do klienta dzień przed wizytą.',
      icon: 'pi-bell',
    },
    {
      key: 'appointmentReminder2hToCustomer',
      title: 'Przypomnienie ~2h przed wizytą',
      description: 'Przypomnienie wysyłane do klienta tuż przed wizytą.',
      icon: 'pi-clock',
    },
  ];

  constructor() {
    effect(() => {
      const dto = this.settings.value();
      if (!dto) {
        return;
      }
      untracked(() => {
        const ns = dto.notificationSettings;
        this.notificationSettings.set({
          newBookingToSalon: ns?.newBookingToSalon ?? true,
          bookingConfirmationToCustomer: ns?.bookingConfirmationToCustomer ?? true,
          cancellationToSalon: ns?.cancellationToSalon ?? true,
          cancellationToCustomer: ns?.cancellationToCustomer ?? true,
          rescheduleToSalon: ns?.rescheduleToSalon ?? true,
          rescheduleToCustomer: ns?.rescheduleToCustomer ?? true,
          appointmentReminderToCustomer: ns?.appointmentReminderToCustomer ?? true,
          awaitingConfirmationToSalon: ns?.awaitingConfirmationToSalon ?? true,
          cancelledBySalonToCustomer: ns?.cancelledBySalonToCustomer ?? true,
          rescheduledBySalonToCustomer: ns?.rescheduledBySalonToCustomer ?? true,
          appointmentReminder2hToCustomer: ns?.appointmentReminder2hToCustomer ?? true,
          staffBookedAppointmentToCustomer: ns?.staffBookedAppointmentToCustomer ?? true,
        });
      });
    });
  }

  toggleNotification(key: keyof NotificationSettingsModel): void {
    this.notificationSettings.update((s) => ({ ...s, [key]: !s[key] }));
  }

  isOn(key: keyof NotificationSettingsModel): boolean {
    return this.notificationSettings()[key];
  }

  onCancel(): void {
    void this.router.navigateByUrl('/admin/schedule');
  }

  async onSave(): Promise<void> {
    const dto = this.settings.value();
    if (!dto) {
      return;
    }
    try {
      await lastValueFrom(
        this.salonClient.put({
          name: dto.name ?? '',
          slug: dto.slug ?? '',
          customerVerificationChannel:
            dto.customerVerificationChannel ?? CustomerVerificationChannel.Phone,
          appointmentSlotStepMinutes: dto.appointmentSlotStepMinutes ?? 15,
          timeZoneId: dto.timeZoneId?.trim() || 'Europe/Warsaw',
          currency: (dto.currency?.trim() || 'PLN').toUpperCase(),
          bookingAccessPolicy: dto.bookingAccessPolicy,
          appointmentConfirmationMode: dto.appointmentConfirmationMode,
          gapFillingSettings: dto.gapFillingSettings,
          notificationSettings: this.notificationSettings(),
        }),
      );
      this.messages.add({
        severity: 'success',
        summary: 'Zapisano',
        detail: 'Ustawienia powiadomień zostały zaktualizowane.',
        life: 4_000,
      });
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Błąd zapisu',
        detail: 'Nie udało się zapisać ustawień powiadomień. Sprawdź uprawnienia.',
        life: 5_000,
      });
    }
  }
}
