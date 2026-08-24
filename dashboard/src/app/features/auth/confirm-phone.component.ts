import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthClient } from '@core/api/api-client';
import { lastValueFrom } from 'rxjs';

/**
 * Potwierdza numer telefonu właściciela 6-cyfrowym kodem z SMS-a (otrzymanym po confirm-email).
 * UserId trzymamy w query param. Po sukcesie redirect na /login.
 */
@Component({
  selector: 'app-confirm-phone',
  standalone: true,
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <section
        class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 space-y-5 text-center"
      >
        <p class="admin-section-label text-primary">Weryfikacja</p>
        <h1 class="text-2xl font-black tracking-tight">Potwierdź numer telefonu</h1>
        <!--
          Numer, na który poszedł kod, jest tu KLUCZOWY: przy literówce w numerze czekasz na SMS,
          który nigdy nie przyjdzie, i nie masz jak tego stwierdzić. Backend zwraca go zamaskowanego
          już przy potwierdzeniu maila — tamten ekran znika po 1200 ms, więc przekazujemy go dalej
          w query („masked").
        -->
        @if (maskedPhone()) {
          <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
            Wpisz 6-cyfrowy kod, który wysłaliśmy SMS-em na numer
            <span class="font-semibold text-surface-800 dark:text-surface-200">{{ maskedPhone() }}</span
            >.
          </p>
        } @else {
          <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
            Wpisz 6-cyfrowy kod, który wysłaliśmy SMS-em po potwierdzeniu maila.
          </p>
        }

        @if (initialSmsFailed()) {
          <div
            class="rounded-lg bg-amber-50 dark:bg-amber-950/30 border border-amber-200 dark:border-amber-900/50 px-3 py-2 text-xs text-amber-800 dark:text-amber-200"
          >
            Pierwsza wysyłka SMS-a się nie udała — kliknij „Wyślij ponownie".
          </div>
        }

        @if (!userId()) {
          <p class="text-sm text-red-600 dark:text-red-300">
            Brak identyfikatora konta w adresie. Wróć do potwierdzenia maila przez link w skrzynce.
          </p>
        } @else {
          <!--
            Placeholder „123456" w tym polu (3xl, tracking 0.6em, mono) renderował się jak WPISANY
            kod, nie jak podpowiedź — kropki są jednoznacznie puste.
          -->
          <input
            type="text"
            inputmode="numeric"
            autocomplete="one-time-code"
            maxlength="6"
            data-testid="confirm-phone-code"
            [(ngModel)]="codeInput"
            placeholder="••••••"
            class="w-full text-center text-3xl tracking-[0.6em] font-mono py-3 rounded-xl border border-surface-300 dark:border-surface-200 bg-surface-0 dark:bg-surface-50 focus:outline-none focus:ring-2 focus:ring-amber-500"
            (input)="onCodeInput($event)"
          />

          @if (error()) {
            <div
              class="rounded-xl border border-red-400/35 bg-red-500/10 px-4 py-3 text-sm text-red-800 dark:text-red-200"
              role="alert"
            >
              {{ error() }}
            </div>
          }

          @if (success()) {
            <div
              class="rounded-xl border border-emerald-400/35 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-800 dark:text-emerald-200"
            >
              Telefon potwierdzony. Zaloguj się, aby dokończyć konfigurację salonu.
            </div>
          }

          <button
            type="button"
            data-testid="confirm-phone-submit"
            (click)="submit()"
            [disabled]="!canSubmit() || loading()"
            class="w-full px-8 py-3 rounded-xl bg-primary text-primary-contrast font-semibold shadow-lg hover:opacity-95 disabled:opacity-50 transition-opacity"
          >
            {{ loading() ? 'Sprawdzam…' : 'Potwierdź telefon' }}
          </button>

          <button
            type="button"
            data-testid="confirm-phone-resend"
            (click)="resend()"
            [disabled]="resendCooldown() > 0 || resending()"
            class="w-full px-4 py-2 rounded-xl border border-surface-300 dark:border-surface-200 text-sm font-medium hover:bg-surface-100 dark:hover:bg-surface-100 disabled:opacity-50"
          >
            @if (resending()) {
              Wysyłanie…
            } @else if (resendCooldown() > 0) {
              Wyślij ponownie za {{ resendCooldown() }}s
            } @else {
              Wyślij kod ponownie
            }
          </button>
        }

        <p class="text-center text-sm text-surface-500 dark:text-surface-400">
          <a routerLink="/login" class="text-primary hover:underline">Wróć do logowania</a>
        </p>
      </section>
    </div>
  `,
})
export class ConfirmPhoneComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authClient = inject(AuthClient);

  protected readonly userId = signal<string>('');

  /** Zamaskowany numer z kroku potwierdzenia maila (query „masked") — patrz komentarz w szablonie. */
  protected readonly maskedPhone = signal<string>('');
  protected readonly initialSmsFailed = signal(false);
  protected readonly codeInput = signal('');
  protected readonly loading = signal(false);
  protected readonly resending = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal(false);
  protected readonly resendCooldown = signal(0);
  private cooldownTimer: ReturnType<typeof setInterval> | null = null;

  protected readonly canSubmit = computed(() => /^\d{6}$/.test(this.codeInput()));

  ngOnInit(): void {
    const uid = this.route.snapshot.queryParamMap.get('userId') ?? '';
    this.userId.set(uid);
    this.maskedPhone.set(this.route.snapshot.queryParamMap.get('masked') ?? '');
    if (this.route.snapshot.queryParamMap.get('smsFailed') === '1') {
      this.initialSmsFailed.set(true);
    }
  }

  ngOnDestroy(): void {
    if (this.cooldownTimer) {
      clearInterval(this.cooldownTimer);
    }
  }

  onCodeInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 6);
    this.codeInput.set(value);
    this.error.set('');
  }

  async submit(): Promise<void> {
    if (!this.canSubmit() || this.loading()) {
      return;
    }
    this.error.set('');
    this.loading.set(true);
    try {
      await lastValueFrom(
        this.authClient.confirmPhone({ userId: this.userId(), code: this.codeInput() }),
      );
      this.success.set(true);
      setTimeout(() => {
        // Nie logujemy automatycznie: hasła nie mamy, a confirm-phone jest idempotentne — auto-login
        // po stronie backendu byłby niebezpieczny. Kierujemy do logowania z flagą „dokończ konfigurację".
        void this.router.navigate(['/login'], {
          replaceUrl: true,
          queryParams: { returnUrl: '/setup', justVerified: '1' },
        });
      }, 1200);
    } catch (err: unknown) {
      this.error.set(this.extractMessage(err) ?? 'Nieprawidłowy kod. Spróbuj ponownie.');
    } finally {
      this.loading.set(false);
    }
  }

  async resend(): Promise<void> {
    if (this.resendCooldown() > 0 || this.resending()) {
      return;
    }
    this.resending.set(true);
    this.error.set('');
    try {
      await lastValueFrom(this.authClient.resendPhoneOtp({ userId: this.userId() }));
      this.initialSmsFailed.set(false);
      this.startCooldown(60);
    } catch (err: unknown) {
      const message = this.extractMessage(err) ?? 'Nie udało się ponownie wysłać kodu.';
      this.error.set(message);
      // Backend zwraca 429 z Retry-After; spróbujmy wyczytać.
      const retry = this.extractRetryAfter(err);
      if (retry) {
        this.startCooldown(retry);
      }
    } finally {
      this.resending.set(false);
    }
  }

  private startCooldown(seconds: number): void {
    if (this.cooldownTimer) {
      clearInterval(this.cooldownTimer);
    }
    this.resendCooldown.set(seconds);
    this.cooldownTimer = setInterval(() => {
      const next = this.resendCooldown() - 1;
      if (next <= 0) {
        this.resendCooldown.set(0);
        if (this.cooldownTimer) {
          clearInterval(this.cooldownTimer);
          this.cooldownTimer = null;
        }
      } else {
        this.resendCooldown.set(next);
      }
    }, 1000);
  }

  private extractMessage(err: unknown): string | null {
    const e = err as { error?: { detail?: string; title?: string } };
    return e?.error?.detail ?? e?.error?.title ?? null;
  }

  private extractRetryAfter(err: unknown): number | null {
    const e = err as { headers?: { get?: (k: string) => string | null } };
    const raw = e?.headers?.get?.('Retry-After');
    if (!raw) {
      return null;
    }
    const n = parseInt(raw, 10);
    return Number.isFinite(n) && n > 0 ? n : null;
  }
}
