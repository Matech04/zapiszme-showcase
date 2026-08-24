import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MessageService } from 'primeng/api';
import {
  AppointmentPreviewDto,
  AppointmentsClient,
  SwapAppointmentsRequest,
  SwapPreviewDto,
} from '@core/api/api-client';
import { AdminDrawerComponent } from './admin-drawer.component';

/**
 * Dialog zamiany terminów dwóch wizyt. Po otwarciu pobiera podgląd (`previewSwap`):
 *  - mieści się „jak jest" → przycisk „Zamień miejscami" (bez zmiany usług),
 *  - różne długości i nie mieści się, ale można skrócić dłuższą → prosi o potwierdzenie
 *    skrócenia (zmiana usługi dłuższej wizyty na krótszą) i wysyła `harmonizeToShorter=true`,
 *  - nie da się dopasować → akcja zablokowana z komunikatem.
 */
@Component({
  selector: 'app-swap-appointments-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, AdminDrawerComponent],
  template: `
    <app-admin-drawer
      [isOpen]="isVisible()"
      [isDesktop]="isDesktop()"
      title="Zamień terminy"
      label="Kalendarz"
      [closeable]="!isSubmitting()"
      (closeRequested)="onCancel()"
    >
      <div drawer-body>
        @if (first(); as a) {
          @if (second(); as b) {
            <div class="flex flex-col gap-4">
              <div class="grid grid-cols-1 gap-3">
                <div class="rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-surface-50/70 dark:bg-surface-50/40 px-4 py-3">
                  <p class="admin-section-label text-primary">Wizyta 1</p>
                  <p class="text-sm font-bold text-surface-900 mt-0.5">{{ serviceLine(a) }}</p>
                  <p class="text-xs text-surface-600 dark:text-surface-300 mt-0.5 font-mono">{{ dateLabel(a) }} • {{ shortTime(a.startTime) }} – {{ shortTime(a.endTime) }}</p>
                </div>
                <div class="flex justify-center text-surface-400">
                  <i class="pi pi-arrow-right-arrow-left" aria-hidden="true"></i>
                </div>
                <div class="rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-surface-50/70 dark:bg-surface-50/40 px-4 py-3">
                  <p class="admin-section-label text-primary">Wizyta 2</p>
                  <p class="text-sm font-bold text-surface-900 mt-0.5">{{ serviceLine(b) }}</p>
                  <p class="text-xs text-surface-600 dark:text-surface-300 mt-0.5 font-mono">{{ dateLabel(b) }} • {{ shortTime(b.startTime) }} – {{ shortTime(b.endTime) }}</p>
                </div>
              </div>

              @if (previewResource.isLoading()) {
                <div class="py-4 flex justify-center">
                  <i class="pi pi-spin pi-spinner text-xl text-primary" aria-hidden="true"></i>
                </div>
              } @else if (previewResource.error()) {
                <p class="rounded-xl bg-red-50 dark:bg-red-950/30 border border-red-200/70 dark:border-red-800/50 px-3 py-2 text-xs text-red-700 dark:text-red-300">
                  Nie udało się sprawdzić możliwości zamiany. Spróbuj ponownie.
                </p>
              } @else if (preview(); as p) {
                @if (canPlainSwap()) {
                  <p class="rounded-xl bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-200/70 dark:border-emerald-800/50 px-3 py-2 text-xs text-emerald-800 dark:text-emerald-200">
                    Wizyty zostaną zamienione miejscami. Klienci zostaną powiadomieni o nowych terminach.
                  </p>
                } @else if (canHarmonize()) {
                  <div class="rounded-xl bg-amber-50 dark:bg-amber-950/30 border border-amber-200/70 dark:border-amber-800/50 px-3 py-2.5 text-xs text-amber-900 dark:text-amber-200 space-y-1.5">
                    <p class="font-bold">Wizyty mają różną długość i nie zmieszczą się bez zmian.</p>
                    <p>
                      Aby je dopasować, dłuższa wizyta zostanie skrócona:
                      <span class="font-semibold">{{ p.fromServiceName }}</span> →
                      <span class="font-semibold">{{ p.toServiceName }}</span>.
                    </p>
                    @if (p.oldPrice != null && p.newPrice != null) {
                      <p>Cena zmieni się z <span class="font-semibold">{{ p.oldPrice }} zł</span> na <span class="font-semibold">{{ p.newPrice }} zł</span>.</p>
                    }
                  </div>
                } @else {
                  <p class="rounded-xl bg-surface-100 dark:bg-surface-100/60 border border-surface-200/70 dark:border-surface-200/60 px-3 py-2 text-xs text-surface-600 dark:text-surface-300">
                    Tych wizyt nie można zamienić — nie mieszczą się w grafiku (kolizja lub poza godzinami pracy).
                  </p>
                }
              }

              @if (submitError()) {
                <p class="rounded-xl bg-red-50 dark:bg-red-950/30 border border-red-200/70 dark:border-red-800/50 px-3 py-2 text-xs text-red-700 dark:text-red-300">
                  {{ submitError() }}
                </p>
              }
            </div>
          }
        }
      </div>

      <div drawer-footer class="flex items-center justify-end gap-2">
        <button
          type="button"
          [disabled]="isSubmitting()"
          (click)="onCancel()"
          class="rounded-xl border border-surface-300 dark:border-surface-600 font-bold py-2 px-4 text-surface-700 hover:border-primary/45 transition-colors disabled:opacity-50"
        >
          Anuluj
        </button>
        <button
          type="button"
          [disabled]="!canSubmit() || isSubmitting()"
          (click)="onSubmit()"
          class="rounded-xl bg-surface-900 dark:bg-surface-100 text-surface-0 dark:text-surface-900 font-bold py-2 px-4 transition-opacity hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          @if (isSubmitting()) {
            <i class="pi pi-spin pi-spinner mr-2" aria-hidden="true"></i>
          }
          {{ canHarmonize() && !canPlainSwap() ? 'Skróć i zamień' : 'Zamień miejscami' }}
        </button>
      </div>
    </app-admin-drawer>
  `,
})
export class SwapAppointmentsDialogComponent {
  readonly first = input<AppointmentPreviewDto | null>(null);
  readonly second = input<AppointmentPreviewDto | null>(null);
  readonly isDesktop = input<boolean>(false);

  readonly closeRequested = output<void>();
  readonly success = output<void>();

  private readonly appointmentsClient = inject(AppointmentsClient);
  private readonly messages = inject(MessageService);

  protected readonly isSubmitting = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected readonly isVisible = computed(() => this.first() != null && this.second() != null);

  protected readonly previewResource = rxResource({
    params: () => {
      const a = this.first()?.id;
      const b = this.second()?.id;
      if (!a || !b) return undefined;
      return { a, b };
    },
    stream: ({ params }) =>
      this.appointmentsClient.previewSwap(params.a, params.b).pipe(
        catchError(() => of(undefined)),
      ),
  });

  protected readonly preview = computed<SwapPreviewDto | undefined>(() => this.previewResource.value() ?? undefined);

  protected readonly canPlainSwap = computed(() => this.preview()?.plainSwapFits === true);

  protected readonly canHarmonize = computed(() => {
    const p = this.preview();
    return !!p && !p.plainSwapFits && p.harmonizationAvailable === true && p.harmonizedSwapFits === true;
  });

  protected readonly canSubmit = computed(() => this.canPlainSwap() || this.canHarmonize());

  protected onCancel(): void {
    if (this.isSubmitting()) return;
    this.closeRequested.emit();
  }

  protected onSubmit(): void {
    const a = this.first();
    const b = this.second();
    if (!a?.id || !b?.id || !this.canSubmit()) return;

    const body = {
      firstAppointmentId: a.id,
      secondAppointmentId: b.id,
      harmonizeToShorter: this.canHarmonize() && !this.canPlainSwap(),
    } as SwapAppointmentsRequest;

    this.isSubmitting.set(true);
    this.submitError.set(null);
    this.appointmentsClient.swapAppointments(body).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.messages.add({
          severity: 'success',
          summary: 'Terminy zamienione',
          detail: 'Klienci zostaną powiadomieni o nowych terminach.',
          life: 3000,
        });
        this.success.emit();
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        const status = (err as { status?: number })?.status;
        if (status === 409) {
          this.submitError.set('Wizyt nie da się zamienić — slot został zajęty.');
        } else if (status === 400) {
          this.submitError.set('Zamiana nie spełnia ograniczeń (godziny pracy, przeszłość, status wizyty).');
        } else if (status === 403) {
          this.submitError.set('Nie masz uprawnień do zamiany tych wizyt.');
        } else {
          this.submitError.set('Nie udało się zamienić terminów. Spróbuj ponownie.');
        }
      },
    });
  }

  protected serviceLine(a: AppointmentPreviewDto): string {
    const name = a.serviceName?.trim();
    return name && name !== '' ? name : 'Wizyta';
  }

  protected shortTime(t: string | undefined): string {
    return (t ?? '').substring(0, 5);
  }

  protected dateLabel(a: AppointmentPreviewDto): string {
    if (!a.date) return '';
    const d = new Date(a.date as unknown as string);
    if (Number.isNaN(d.getTime())) return '';
    return d.toLocaleDateString('pl-PL', { weekday: 'short', day: 'numeric', month: 'short' });
  }
}
