import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { lastValueFrom } from 'rxjs';
import { AdminMaintenanceClient } from '@core/api/api-client';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';

/**
 * Panel admina platformy: globalny tryb serwisowy (kill-switch). Tylko `systemAdmin` (route guard).
 * Gdy włączony — backend blokuje wszystkie operacje ZAPISU na WSZYSTKICH kontach (poza adminem
 * platformy) i pokazuje klientom ekran prac serwisowych. Do użycia w razie buga/ataku/migracji,
 * żeby nie narazić się na utratę danych. API: GET/PUT /api/admin/system/maintenance.
 */
@Component({
  selector: 'app-maintenance-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ButtonModule, DatePipe],
  template: `

    <div class="p-6 lg:p-10 max-w-3xl mx-auto">
      <div class="mb-8">
        <h1 class="text-3xl font-black tracking-tight text-surface-900">Tryb serwisowy platformy</h1>
        <p class="mt-1 text-sm text-surface-500 dark:text-surface-400">
          Globalny wyłącznik zapisu dla całej platformy — awaryjna ochrona danych.
        </p>
      </div>

      <div
        data-testid="maintenance-card"
        class="rounded-2xl border px-6 py-6 space-y-5 transition-colors"
        [class.border-red-300]="enabled()"
        [class.bg-red-50]="enabled()"
        [class.dark:border-red-900/50]="enabled()"
        [class.dark:bg-red-950/20]="enabled()"
        [class.border-surface-200]="!enabled()"
        [class.bg-surface-50]="!enabled()"
      >
        <div class="flex items-start gap-3">
          <span class="text-2xl">{{ enabled() ? '🔧' : '✅' }}</span>
          <div>
            <p class="text-lg font-black text-surface-900 m-0">
              {{ enabled() ? 'Tryb serwisowy WŁĄCZONY' : 'Platforma działa normalnie' }}
            </p>
            <p class="mt-1 text-sm text-surface-600 dark:text-surface-400 m-0 leading-relaxed">
              @if (enabled()) {
                Wszystkie operacje zapisu (rezerwacje online i akcje w panelach salonów) są
                zablokowane. Tylko administrator platformy może wprowadzać zmiany. Klienci widzą ekran
                prac serwisowych.
              } @else {
                Włącz, aby natychmiast wstrzymać zapisy na wszystkich kontach — np. gdy wykryjesz bug
                lub atak i chcesz uniknąć utraty danych. Odczyty (przeglądanie) pozostają dostępne.
              }
            </p>
            @if (enabled() && startedAtUtc()) {
              <p class="mt-1 text-xs text-red-700 dark:text-red-300 m-0">
                Włączony od: {{ startedAtUtc() | date: 'short' }}
              </p>
            }
          </div>
        </div>

        <div class="flex flex-col gap-2">
          <label
            for="maintenance-message"
            class="text-xs font-bold text-surface-600 dark:text-surface-400 uppercase tracking-wider"
          >
            Komunikat dla klientów (opcjonalny)
          </label>
          <textarea
            id="maintenance-message"
            data-testid="maintenance-message"
            rows="2"
            maxlength="280"
            [(ngModel)]="message"
            placeholder="np. Trwają prace serwisowe — rezerwacje online chwilowo niedostępne."
            class="w-full py-2.5 px-3 rounded-xl border border-surface-300 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 text-sm resize-none"
          ></textarea>
          <span class="text-xs text-surface-500">Pusty = domyślny tekst na stronie rezerwacji.</span>
        </div>

        <div class="flex flex-wrap gap-3 pt-1">
          @if (!enabled()) {
            <p-button
              data-testid="maintenance-enable"
              severity="danger"
              [loading]="saving()"
              (onClick)="toggle(true)"
              label="Włącz tryb serwisowy"
              icon="pi pi-power-off"
            />
          } @else {
            <p-button
              data-testid="maintenance-save-message"
              severity="secondary"
              [loading]="saving()"
              (onClick)="toggle(true)"
              label="Zapisz komunikat"
              icon="pi pi-save"
            />
            <p-button
              data-testid="maintenance-disable"
              [loading]="saving()"
              (onClick)="toggle(false)"
              label="Wyłącz tryb serwisowy"
              icon="pi pi-check"
            />
          }
        </div>
      </div>
    </div>
  `,
})
export class MaintenancePageComponent {
  private readonly client = inject(AdminMaintenanceClient);
  private readonly messages = inject(MessageService);

  readonly enabled = signal(false);
  readonly startedAtUtc = signal<Date | undefined>(undefined);
  message = '';
  readonly saving = signal(false);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const dto = await lastValueFrom(this.client.get());
      this.enabled.set(dto.enabled ?? false);
      this.message = dto.message ?? '';
      this.startedAtUtc.set(dto.startedAtUtc);
    } catch {
      // errorInterceptor pokaże toast; zostaw domyślny (wyłączony) stan.
    }
  }

  async toggle(enabled: boolean): Promise<void> {
    if (this.saving()) return;
    this.saving.set(true);
    try {
      await lastValueFrom(
        this.client.set({ enabled, message: enabled ? this.message.trim() : undefined }),
      );
      this.enabled.set(enabled);
      if (!enabled) {
        this.message = '';
        this.startedAtUtc.set(undefined);
      }
      this.messages.add({
        severity: 'success',
        summary: enabled ? 'Tryb serwisowy włączony' : 'Tryb serwisowy wyłączony',
        detail: enabled
          ? 'Zapisy na wszystkich kontach są teraz zablokowane.'
          : 'Platforma znów działa normalnie.',
        life: 4_000,
      });
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Błąd zapisu',
        detail: 'Nie udało się zmienić trybu serwisowego. Spróbuj ponownie.',
        life: 5_000,
      });
    } finally {
      this.saving.set(false);
    }
  }
}
