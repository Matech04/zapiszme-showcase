import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { interval, map } from 'rxjs';
import { AuthClient } from '@core/api/api-client';

/** Cooldown po resendzie — chroni przed klikaniem na klar i daje serwerowemu rate-limiterowi oddech. */
const RESEND_COOLDOWN_SECONDS = 60;

@Component({
  selector: 'app-check-email',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <div
        class="admin-glass-card relative z-10 w-full max-w-lg rounded-4xl p-8 space-y-6 flex flex-col text-center"
      >
        <!--
          Etykieta etapu, NIE licznik. Było „Krok 2 z 2", co jest nieprawdą: po tym ekranie czeka
          jeszcze kod SMS, logowanie i osiem kroków kreatora — komunikat mówił „jesteś na końcu",
          gdy użytkowniczka była na jednej piątej drogi. Slot admin-section-label trzyma na
          sąsiednich ekranach etykiety etapu („Rejestracja", „Weryfikacja", „Logowanie”),
          więc licznik trafił tu przez pomyłkę.
        -->
        <p class="admin-section-label text-primary">Aktywacja konta</p>
        <h1 class="text-3xl font-black tracking-tight">Potwierdź swój adres email</h1>

        @if (email()) {
          <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
            Wysłaliśmy link aktywacyjny na adres
            <span class="font-semibold text-surface-800">{{ email() }}</span
            >. Kliknij w link w wiadomości, żeby aktywować konto i się zalogować.
          </p>
        } @else {
          <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
            Wysłaliśmy link aktywacyjny na podany przez Ciebie adres email. Kliknij w link
            w wiadomości, żeby aktywować konto i się zalogować.
          </p>
        }

        <div
          class="rounded-xl border border-amber-300/40 bg-amber-50/40 dark:bg-amber-900/15 px-4 py-3 text-xs text-surface-600 dark:text-surface-300 text-left"
        >
          <p class="font-semibold mb-1">Nie widzisz wiadomości?</p>
          <ul class="space-y-1 list-disc list-inside">
            <li>Sprawdź folder Spam / Oferty.</li>
            <li>Upewnij się, że podany adres jest poprawny.</li>
            <li>Mail może iść kilka minut — daj mu chwilę.</li>
          </ul>
        </div>

        <div class="flex flex-col items-center gap-2">
          <button
            type="button"
            (click)="resend()"
            [disabled]="!canResend()"
            [attr.aria-busy]="pending() || null"
            class="w-full sm:w-auto px-6 py-2 rounded-xl border border-primary text-primary font-semibold hover:bg-primary/10 disabled:opacity-50 transition-opacity inline-flex items-center justify-center gap-2"
          >
            @if (pending()) {
              <svg
                class="animate-spin h-4 w-4"
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                aria-hidden="true"
              >
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"></path>
              </svg>
            }
            <span>{{ resendButtonLabel() }}</span>
          </button>

          @if (sentToast()) {
            <p class="text-xs text-emerald-600 dark:text-emerald-300" role="status" aria-live="polite">
              Wysłaliśmy nową wiadomość. Sprawdź skrzynkę.
            </p>
          }
          @if (errorToast()) {
            <p class="text-xs text-red-600 dark:text-red-300" role="alert">
              Nie udało się wysłać. Spróbuj ponownie za chwilę.
            </p>
          }
        </div>

        <p class="text-center text-sm text-surface-500 dark:text-surface-400">
          Konto już aktywne?
          <a routerLink="/login" class="text-primary hover:underline">Przejdź do logowania</a>
        </p>
      </div>
    </div>
  `,
})
export class CheckEmailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly authClient = inject(AuthClient);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly email = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('email') ?? '')),
    { initialValue: '' },
  );

  protected readonly pending = signal(false);
  protected readonly sentToast = signal(false);
  protected readonly errorToast = signal(false);

  /** Sekund pozostało do końca cooldownu; 0 = można klikać. */
  protected readonly cooldownLeft = signal(0);

  protected readonly canResend = computed(
    () => !this.pending() && this.cooldownLeft() === 0 && this.email().length > 0,
  );

  protected readonly resendButtonLabel = computed(() => {
    if (this.pending()) return 'Wysyłanie…';
    const left = this.cooldownLeft();
    if (left > 0) return `Wyślij ponownie (${left} s)`;
    return 'Wyślij ponownie';
  });

  constructor() {
    // Pojedyncze tykanie cooldownu — żyje tylko przez czas trwania komponentu.
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const left = this.cooldownLeft();
        if (left > 0) {
          this.cooldownLeft.set(left - 1);
        }
      });

    // Toasty „wysłaliśmy" / „błąd" znikają po 6s.
    effect((onCleanup) => {
      if (this.sentToast()) {
        const id = setTimeout(() => this.sentToast.set(false), 6000);
        onCleanup(() => clearTimeout(id));
      }
    });
    effect((onCleanup) => {
      if (this.errorToast()) {
        const id = setTimeout(() => this.errorToast.set(false), 6000);
        onCleanup(() => clearTimeout(id));
      }
    });
  }

  protected async resend(): Promise<void> {
    const email = this.email();
    if (!email || this.pending() || this.cooldownLeft() > 0) {
      return;
    }
    this.pending.set(true);
    this.errorToast.set(false);
    try {
      await new Promise<void>((resolve, reject) => {
        this.authClient.resendConfirmEmail({ email }).subscribe({
          next: () => resolve(),
          error: (err) => reject(err),
        });
      });
      this.sentToast.set(true);
      this.cooldownLeft.set(RESEND_COOLDOWN_SECONDS);
    } catch {
      this.errorToast.set(true);
    } finally {
      this.pending.set(false);
    }
  }
}
