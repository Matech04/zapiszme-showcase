import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { ImpersonationService } from '@core/auth/impersonation.service';

/**
 * Baner trybu wsparcia (support impersonation). Widoczny, gdy admin platformy działa w kontekście
 * salonu klienta (`AuthSessionService.isImpersonating`). Pokazuje nazwę salonu, odliczanie do
 * wygaśnięcia i przycisk zakończenia. Po zakończeniu robi pełny reload — `hydrate()` przywraca
 * kontekst administratora.
 */
@Component({
  selector: 'app-impersonation-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div
        data-testid="impersonation-banner"
        class="flex flex-wrap items-center justify-center gap-x-3 gap-y-1 rounded-2xl mb-3 px-4 py-2 text-sm bg-rose-500/15 border border-rose-500/50 text-rose-900 dark:text-rose-100"
        role="status"
      >
        <span class="font-semibold">🛟 Tryb wsparcia</span>
        <span class="text-rose-800/80 dark:text-rose-200/80">
          Pracujesz jako support w salonie <strong>{{ tenantName() }}</strong>
          @if (readOnly()) {
            <span class="ml-1 rounded px-1.5 py-0.5 text-xs border border-rose-500/50">tylko odczyt</span>
          }
        </span>
        @if (remaining() !== null) {
          <span class="font-mono tabular-nums" data-testid="impersonation-countdown">
            pozostało {{ formattedRemaining() }}
          </span>
        }
        <button
          type="button"
          data-testid="impersonation-exit"
          [disabled]="ending()"
          (click)="end()"
          class="ml-1 rounded-lg px-2 py-0.5 border border-rose-500/60 hover:bg-rose-500/20 transition-colors disabled:opacity-60"
        >
          Zakończ sesję
        </button>
      </div>
    }
  `,
})
export class ImpersonationBannerComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthSessionService);
  private readonly impersonation = inject(ImpersonationService);

  readonly visible = this.auth.isImpersonating;
  readonly tenantName = this.auth.impersonatedTenantName;
  readonly ending = signal(false);
  readonly readOnly = signal(false);
  readonly remaining = signal<number | null>(null);

  readonly formattedRemaining = computed(() => {
    const total = this.remaining();
    if (total === null || total <= 0) return '00:00';
    const mm = Math.floor(total / 60)
      .toString()
      .padStart(2, '0');
    const ss = (total % 60).toString().padStart(2, '0');
    return `${mm}:${ss}`;
  });

  private timer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    if (!this.visible()) return;

    // Szczegóły (pozostały czas, tryb) bierzemy z backendu — sesja w bazie jest źródłem prawdy.
    this.impersonation.current().subscribe({
      next: (status) => {
        if (!status) return;
        this.readOnly.set(!!status.isReadOnly);
        this.remaining.set(status.remainingSeconds ?? null);
        this.startCountdown();
      },
      error: () => {
        /* brak danych odliczania — baner i tak pokazuje nazwę salonu z sesji */
      },
    });
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }

  end(): void {
    this.ending.set(true);
    this.impersonation.end().subscribe({
      next: () => this.reloadToAdmin(),
      error: () => this.reloadToAdmin(),
    });
  }

  private startCountdown(): void {
    this.clearTimer();
    this.timer = setInterval(() => {
      const next = (this.remaining() ?? 0) - 1;
      this.remaining.set(next);
      if (next <= 0) {
        // Sesja wygasła po stronie serwera — przeładuj, by wrócić do kontekstu admina.
        this.reloadToAdmin();
      }
    }, 1000);
  }

  private clearTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  private reloadToAdmin(): void {
    this.clearTimer();
    window.location.href = '/';
  }
}
