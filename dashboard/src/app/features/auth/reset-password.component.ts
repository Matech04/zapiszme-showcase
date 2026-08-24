import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthClient, AuthSessionDto, FileResponse } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { defaultAdminRouteForRole } from '@core/auth/default-admin-route';
import { formatAuthApiError } from '@core/errors/api-error-messages';
import { finalize, Observable } from 'rxjs';

/**
 * Reguły złożoności hasła — lustrzane odbicie `options.Password` z backendowego Program.cs
 * (RequiredLength=8, RequireLowercase/Uppercase/Digit/NonAlphanumeric). Trzymaj zgodnie,
 * inaczej front przepuści hasło, które serwer odrzuci.
 */
const PASSWORD_RULES = [
  { label: 'co najmniej 8 znaków', test: (v: string) => v.length >= 8 },
  { label: 'mała litera', test: (v: string) => /[a-z]/.test(v) },
  { label: 'wielka litera', test: (v: string) => /[A-Z]/.test(v) },
  { label: 'cyfra', test: (v: string) => /\d/.test(v) },
  { label: 'znak specjalny (np. !@#)', test: (v: string) => /[^a-zA-Z0-9]/.test(v) },
] as const;

function passwordComplexityValidator(control: AbstractControl): ValidationErrors | null {
  const value: string = control.value ?? '';
  const unmet = PASSWORD_RULES.filter((rule) => !rule.test(value)).map((rule) => rule.label);
  return unmet.length ? { passwordComplexity: unmet } : null;
}

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <form
        [formGroup]="form"
        (ngSubmit)="submit()"
        class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 space-y-6"
      >
        <div class="space-y-2 text-center">
          <p class="admin-section-label text-primary">{{ isInvite ? 'Zaproszenie' : 'Reset hasła' }}</p>
          <h1 class="text-2xl font-black tracking-tight">{{ title() }}</h1>
          <p class="text-sm text-surface-600 dark:text-surface-300">{{ description() }}</p>
        </div>

        <label class="block space-y-2">
          <span class="text-xs font-bold uppercase tracking-widest text-surface-500 dark:text-surface-400">Nowe hasło</span>
          <input
            data-testid="reset-password-input"
            type="password"
            formControlName="password"
            autocomplete="new-password"
            class="w-full rounded-xl bg-white/80 dark:bg-surface-950 border border-surface-300 dark:border-surface-200 px-4 py-3 text-surface-900 outline-none focus:border-primary"
          />
        </label>

        <ul class="space-y-1 text-xs">
          @for (rule of passwordRules(); track rule.label) {
            <li
              class="flex items-center gap-2"
              [class.text-green-600]="rule.ok"
              [class.dark:text-green-400]="rule.ok"
              [class.text-surface-500]="!rule.ok"
              [class.dark:text-surface-400]="!rule.ok"
            >
              <span class="w-3 text-center">{{ rule.ok ? '✓' : '○' }}</span>
              <span>{{ rule.label }}</span>
            </li>
          }
        </ul>

        <button
          type="submit"
          data-testid="reset-password-submit"
          [disabled]="form.invalid || loading() || !isLinkValid()"
          class="w-full px-8 py-3 rounded-xl bg-primary text-primary-contrast font-semibold shadow-lg hover:opacity-95 disabled:opacity-50 transition-opacity"
        >
          {{ loading() ? 'Zapisywanie...' : buttonLabel() }}
        </button>

        @if (!isLinkValid()) {
          <p class="text-sm text-center text-red-600 dark:text-red-400">
            Link jest nieprawidłowy lub wygasł.
          </p>
        }

        @if (message()) {
          <p
            class="text-sm text-center whitespace-pre-line"
            [class.text-red-600]="isError()"
            [class.dark:text-red-400]="isError()"
            [class.text-surface-600]="!isError()"
            [class.dark:text-surface-300]="!isError()"
          >
            {{ message() }}
          </p>
        }

        <p class="text-center text-sm text-surface-500 dark:text-surface-400">
          <a routerLink="/login" class="text-primary hover:underline">Wróć do logowania</a>
        </p>
      </form>
    </div>
  `,
})
export class ResetPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authClient = inject(AuthClient);
  private readonly authSession = inject(AuthSessionService);

  protected readonly loading = signal(false);
  protected readonly message = signal('');

  private readonly userId = this.route.snapshot.queryParamMap.get('userId');
  private readonly token = this.route.snapshot.queryParamMap.get('token');
  protected readonly isInvite = this.route.snapshot.routeConfig?.path === 'accept-invite';

  protected readonly title = computed(() => (this.isInvite ? 'Przyjmij zaproszenie' : 'Reset hasła'));
  protected readonly description = computed(() =>
    this.isInvite ? 'Ustaw hasło do nowego konta pracownika.' : 'Ustaw nowe hasło do swojego konta.',
  );
  protected readonly buttonLabel = computed(() => (this.isInvite ? 'Aktywuj konto' : 'Zmień hasło'));
  protected readonly isLinkValid = computed(() => !!this.userId && !!this.token);

  form = this.fb.nonNullable.group({
    password: ['', [Validators.required, passwordComplexityValidator]],
  });

  private readonly passwordValue = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  protected readonly passwordRules = computed(() =>
    PASSWORD_RULES.map((rule) => ({ label: rule.label, ok: rule.test(this.passwordValue()) })),
  );
  protected readonly isError = signal(false);

  submit(): void {
    if (this.form.invalid || this.loading() || !this.userId || !this.token) {
      return;
    }

    this.loading.set(true);
    const request = {
      userId: this.userId,
      token: this.token,
      password: this.form.controls.password.value,
    };
    const action: Observable<AuthSessionDto | FileResponse> = this.isInvite
      ? this.authClient.acceptInvite(request)
      : this.authClient.resetPassword(request);

    action.pipe(finalize(() => this.loading.set(false))).subscribe({
      next: () => {
        this.isError.set(false);
        this.message.set('Hasło zostało zapisane.');
        void this.router.navigate(
          [defaultAdminRouteForRole(this.authSession.currentRole(), this.authSession.currentEmployeeId())],
          {
            replaceUrl: true,
          },
        );
      },
      error: (err: unknown) => {
        this.isError.set(true);
        this.message.set(formatAuthApiError(err));
      },
    });
  }
}
