import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  OnDestroy,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import {
  email,
  form,
  FormField,
  maxLength,
  minLength,
  pattern,
  required,
  submit,
  validate,
} from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, lastValueFrom, map, switchMap, tap } from 'rxjs';
import { PublicPromoClient, ValidatePromoCodeResult } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { setOnboardingPending } from '@core/auth/onboarding-pending';
import { environment } from '../../../environments/environment';
import { partitionRegisterOwnerAuthErrors } from '@core/errors/auth-form-field-errors';
import {
  DEFAULT_AUTH_LOCALE,
  translateAuthValidationEntry,
} from '@core/i18n/auth-validation.catalog';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';

/**
 * Odpowiada regułom `PasswordOptions` w API (`Program.cs`): cyfra, mała i wielka litera (Unicode),
 * co najmniej jeden znak spoza liter i cyfr (Unicode), długość ≥ 8 — tę ostatnią pokazuje `minLength`.
 */
function registerOwnerPasswordStrengthErrors(password: string): { kind: string; message: string }[] {
  if (!password || password.length < 8) {
    return [];
  }
  const msg = (apiKey: string) =>
    translateAuthValidationEntry(apiKey, [''], DEFAULT_AUTH_LOCALE)[0] ?? apiKey;

  const errors: { kind: string; message: string }[] = [];
  if (!/\p{Nd}/u.test(password)) {
    errors.push({ kind: 'PasswordRequiresDigit', message: msg('PasswordRequiresDigit') });
  }
  if (!/\p{Ll}/u.test(password)) {
    errors.push({ kind: 'PasswordRequiresLower', message: msg('PasswordRequiresLower') });
  }
  if (!/\p{Lu}/u.test(password)) {
    errors.push({ kind: 'PasswordRequiresUpper', message: msg('PasswordRequiresUpper') });
  }
  if (!/[^\p{L}\p{N}]/u.test(password)) {
    errors.push({
      kind: 'PasswordRequiresNonAlphanumeric',
      message: msg('PasswordRequiresNonAlphanumeric'),
    });
  }
  return errors;
}

declare global {
  interface Window {
    turnstile?: {
      render: (
        element: HTMLElement,
        options: {
          sitekey: string;
          theme?: 'light' | 'dark' | 'auto';
          callback: (token: string) => void;
          'expired-callback': () => void;
          'error-callback': () => void;
        },
      ) => string;
      remove: (widgetId: string) => void;
      reset: (widgetId?: string) => void;
    };
  }
}

interface RegisterOwnerFormModel {
  email: string;
  password: string;
  /** Tylko klient — nie wysyłane do API. */
  confirmPassword: string;
  phoneNumber: string;
}

/**
 * Slim rejestracja właściciela: e-mail + hasło + telefon (+ opcjonalny kod promo, opcjonalny Turnstile).
 * Nazwa salonu, slug, imię/nazwisko, strefa czasowa i waluta przeniosły się do kreatora (`/setup`) —
 * `RegisterOwnerRequest` to teraz tylko `{ email, password, phoneNumber, turnstileToken?, promoCode? }`.
 * Po sukcesie zapisujemy „porzuconą rejestrację" i kierujemy na ekran z informacją o mailu.
 */
@Component({
  selector: 'app-register-owner',
  standalone: true,
  imports: [RouterLink, FormFieldComponent, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <div
        class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 space-y-6 flex flex-col"
      >
        <div class="space-y-2 text-center">
          <p class="admin-section-label text-primary">Rejestracja</p>
          <h1 class="text-3xl font-black tracking-tight">Załóż konto</h1>
          <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
            Najpierw konto — salon skonfigurujesz w kilku krokach zaraz po zalogowaniu.
          </p>
        </div>

        <div class="grid gap-5">
          <app-form-field
            testId="register-email"
            label="Email"
            id="email"
            type="email"
            placeholder="np. jan@example.com"
            [formField]="registerOwnerForm.email"
          />

          <div>
            <app-form-field
              testId="register-phone"
              label="Numer telefonu"
              id="phoneNumber"
              type="tel"
              placeholder="np. +48 501 234 567"
              [formField]="registerOwnerForm.phoneNumber"
            />
            <p class="text-xs text-surface-500 dark:text-surface-400 mt-1">
              Wyślemy SMS z kodem po potwierdzeniu maila. Format międzynarodowy, np. +48 501 234 567.
            </p>
          </div>

          <div
            class="min-w-0 w-full rounded-2xl border border-surface-200/80 bg-surface-50/50 p-4 dark:border-surface-200/60 dark:bg-surface-50/25"
          >
            <app-form-field
              testId="register-password"
              label="Hasło"
              id="password"
              type="password"
              placeholder="Wpisz hasło"
              hint="Min. 8 znaków: wielka i mała litera, cyfra, znak specjalny."
              [formField]="registerOwnerForm.password"
            />
          </div>

          <div
            class="min-w-0 w-full rounded-2xl border border-surface-200/80 bg-surface-50/50 p-4 dark:border-surface-200/60 dark:bg-surface-50/25"
          >
            <app-form-field
              testId="register-confirm-password"
              label="Powtórz hasło"
              id="confirmPassword"
              type="password"
              placeholder="Wpisz to samo hasło"
              [formField]="registerOwnerForm.confirmPassword"
            />
          </div>
        </div>

        <!-- Pole opcjonalne: kod promocyjny -->
        <div class="rounded-2xl border border-surface-200/80 bg-surface-50/50 p-4 dark:border-surface-200/60 dark:bg-surface-50/25">
          <button
            type="button"
            (click)="promoCodeOpen.set(!promoCodeOpen())"
            class="flex w-full items-center justify-between text-sm font-semibold text-surface-700"
            [attr.aria-expanded]="promoCodeOpen()"
          >
            <span>Masz kod promocyjny?</span>
            <span class="text-xs opacity-60">{{ promoCodeOpen() ? '▲' : '▼' }}</span>
          </button>

          @if (promoCodeOpen()) {
            <div class="mt-3 flex flex-col gap-2">
              <input
                type="text"
                data-testid="register-promo-code"
                [value]="promoCodeValue()"
                (input)="promoCodeValue.set($any($event.target).value)"
                placeholder="np. FOUNDING10"
                class="w-full rounded-xl border border-surface-300 bg-white px-3 py-2 text-sm dark:border-surface-200 dark:bg-surface-50 focus:outline-none focus:ring-2 focus:ring-primary"
                maxlength="64"
                autocomplete="off"
              />
              @if (promoValidating()) {
                <p class="text-xs opacity-70">Sprawdzanie kodu…</p>
              } @else if (promoValidation()?.isValid) {
                <p class="text-xs text-emerald-700 dark:text-emerald-300">
                  ✓ {{ promoValidation()?.discountPreview }}
                </p>
              } @else if (promoValidation() && !promoValidation()?.isValid) {
                <p class="text-xs text-red-700 dark:text-red-300">
                  {{ promoValidation()?.message ?? 'Kod nieprawidłowy.' }}
                </p>
              }
            </div>
          }
        </div>

        @if (globalError()) {
          <div
            class="rounded-xl border border-red-400/35 bg-red-500/10 px-4 py-3 text-sm text-red-800 dark:text-red-200 whitespace-pre-line"
            role="alert"
            aria-live="polite"
          >
            {{ globalError() }}
          </div>
        }

        @if (turnstileEnabled) {
          <div class="flex flex-col items-center gap-2">
            <div #turnstileContainer></div>
            @if (turnstileError()) {
              <p class="text-sm text-red-300">{{ turnstileError() }}</p>
            }
          </div>
        }

        <button
          type="button"
          data-testid="register-submit"
          (click)="submitRegister()"
          [attr.aria-busy]="loading() || null"
          [disabled]="
            registerOwnerForm().invalid() ||
            registerOwnerForm().pending() ||
            loading() ||
            (turnstileEnabled && !turnstileToken())
          "
          class="w-full px-8 py-3 rounded-xl bg-primary text-primary-contrast font-semibold shadow-lg hover:opacity-95 disabled:opacity-50 transition-opacity inline-flex items-center justify-center gap-2"
        >
          @if (loading()) {
            <svg
              class="animate-spin h-5 w-5"
              xmlns="http://www.w3.org/2000/svg"
              fill="none"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"></path>
            </svg>
          }
          <span>{{ loading() ? 'Tworzenie konta…' : 'Załóż konto' }}</span>
        </button>

        <p class="text-center text-sm text-surface-500 dark:text-surface-400">
          Masz już konto?
          <a routerLink="/login" class="text-primary hover:underline">Wróć do logowania</a>
        </p>
      </div>
    </div>
  `,
  styles: [
    `
      :host ::ng-deep .p-inputtext {
        width: 100%;
      }
    `,
  ],
})
export class RegisterOwnerComponent implements AfterViewInit, OnDestroy {
  private readonly auth = inject(AuthSessionService);
  private readonly promoClient = inject(PublicPromoClient);
  private readonly router = inject(Router);
  private readonly turnstileContainer = viewChild<ElementRef<HTMLElement>>('turnstileContainer');

  protected readonly loading = signal(false);
  private readonly serverFieldErrors = signal<Record<string, string[]>>({});
  protected readonly globalError = signal('');
  protected readonly turnstileEnabled = !!environment.turnstileSiteKey;
  protected readonly turnstileToken = signal('');
  protected readonly turnstileError = signal('');
  private turnstileWidgetId: string | null = null;

  // ── Promo code (opcjonalne) ──
  protected readonly promoCodeOpen = signal(false);
  protected readonly promoCodeValue = signal('');
  protected readonly promoValidating = signal(false);
  protected readonly promoValidation = signal<ValidatePromoCodeResult | null>(null);

  protected readonly registerOwnerModel = signal<RegisterOwnerFormModel>({
    email: '',
    password: '',
    confirmPassword: '',
    phoneNumber: '',
  });

  protected readonly registerOwnerForm = form(this.registerOwnerModel, (schemaPath) => {
    required(schemaPath.email, { message: 'Email jest wymagany' });
    maxLength(schemaPath.email, 255);
    email(schemaPath.email, { message: 'Podaj poprawny adres email' });
    validate(schemaPath.email, () => this.serverMessages('email'));

    required(schemaPath.password, { message: 'Hasło jest wymagane' });
    minLength(schemaPath.password, 8, { message: 'Hasło musi mieć co najmniej 8 znaków' });
    validate(schemaPath.password, (ctx) => [
      ...registerOwnerPasswordStrengthErrors(ctx.value() ?? ''),
      ...this.serverMessages('password'),
    ]);

    required(schemaPath.confirmPassword, { message: 'Powtórzenie hasła jest wymagane' });
    validate(schemaPath.confirmPassword, (ctx) => {
      const password = ctx.valueOf(schemaPath.password) ?? '';
      const confirm = ctx.value() ?? '';
      if (!confirm.trim()) {
        return [];
      }
      if (password !== confirm) {
        const message =
          translateAuthValidationEntry('PasswordMismatch', [''], DEFAULT_AUTH_LOCALE)[0] ??
          'Hasła muszą być takie same.';
        return [{ kind: 'passwordMismatch', message }];
      }
      return [];
    });

    required(schemaPath.phoneNumber, { message: 'Numer telefonu jest wymagany' });
    maxLength(schemaPath.phoneNumber, 32);
    pattern(schemaPath.phoneNumber, /^\+?[0-9\s\-()]{9,20}$/, {
      message: 'Podaj prawidłowy numer telefonu (cyfry, +48..., minimum 9 cyfr)',
    });
    validate(schemaPath.phoneNumber, () => this.serverMessages('phoneNumber'));
  });

  constructor() {
    // Realtime walidacja kodu promocyjnego — debounce 500 ms, ignoruje puste.
    toObservable(this.promoCodeValue)
      .pipe(
        map((s) => s.trim()),
        debounceTime(500),
        distinctUntilChanged(),
        switchMap((code) => {
          if (!code) {
            this.promoValidation.set(null);
            this.promoValidating.set(false);
            return EMPTY;
          }
          this.promoValidating.set(true);
          return this.promoClient.validate({ code }).pipe(
            tap((res) => {
              this.promoValidation.set(res);
              this.promoValidating.set(false);
            }),
            catchError(() => {
              this.promoValidating.set(false);
              this.promoValidation.set({ isValid: false, message: 'Nie udało się sprawdzić kodu.', discountPreview: undefined } as ValidatePromoCodeResult);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  ngAfterViewInit(): void {
    if (!this.turnstileEnabled) {
      return;
    }

    this.loadTurnstileScript()
      .then(() => this.renderTurnstile())
      .catch(() => this.turnstileError.set('Nie udało się załadować zabezpieczenia formularza.'));
  }

  ngOnDestroy(): void {
    if (this.turnstileWidgetId && window.turnstile) {
      window.turnstile.remove(this.turnstileWidgetId);
    }
  }

  protected async submitRegister(): Promise<void> {
    if (this.turnstileEnabled && !this.turnstileToken()) {
      this.turnstileError.set('Potwierdź zabezpieczenie formularza.');
      return;
    }
    this.turnstileError.set('');

    await submit(this.registerOwnerForm, async () => {
      const root = this.registerOwnerForm();
      if (root.invalid() || root.pending()) {
        return;
      }

      this.serverFieldErrors.set({});
      this.globalError.set('');
      this.loading.set(true);
      try {
        const value = root.value();
        const response = await lastValueFrom(
          this.auth.registerOwner({
            email: value.email,
            password: value.password,
            phoneNumber: value.phoneNumber,
            turnstileToken: this.turnstileToken() || undefined,
            promoCode: this.promoCodeValue().trim() || undefined,
          }),
        );
        // Brak auto-loginu — backend wymaga potwierdzenia maila. Zapisujemy „porzuconą rejestrację"
        // (banner na /login) i przenosimy na ekran informacyjny z adresem, na który poszedł link.
        const registeredEmail = response.email ?? value.email;
        setOnboardingPending({ email: registeredEmail, stage: 'confirm-email' });
        await this.router.navigate(['/check-email'], {
          replaceUrl: true,
          queryParams: { email: registeredEmail },
        });
      } catch (err: unknown) {
        const { fieldErrors, globalMessage } = partitionRegisterOwnerAuthErrors(err);
        this.serverFieldErrors.set(fieldErrors);
        this.globalError.set(globalMessage ?? '');
        throw err;
      } finally {
        this.loading.set(false);
      }
    });
  }

  private serverMessages(field: string): { kind: string; message: string }[] {
    return (this.serverFieldErrors()[field] ?? []).map((message) => ({ kind: 'server', message }));
  }

  private loadTurnstileScript(): Promise<void> {
    if (window.turnstile) {
      return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
      const existing = document.querySelector<HTMLScriptElement>('script[data-turnstile-script]');
      if (existing) {
        existing.addEventListener('load', () => resolve(), { once: true });
        existing.addEventListener('error', () => reject(), { once: true });
        return;
      }

      const script = document.createElement('script');
      script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
      script.async = true;
      script.defer = true;
      script.dataset['turnstileScript'] = 'true';
      script.onload = () => resolve();
      script.onerror = () => reject();
      document.head.appendChild(script);
    });
  }

  private renderTurnstile(): void {
    const container = this.turnstileContainer()?.nativeElement;
    if (!container || !window.turnstile) {
      return;
    }

    this.turnstileWidgetId = window.turnstile.render(container, {
      sitekey: environment.turnstileSiteKey,
      theme: 'dark',
      callback: (token) => {
        this.turnstileToken.set(token);
        this.turnstileError.set('');
      },
      'expired-callback': () => {
        this.turnstileToken.set('');
        this.turnstileError.set('Weryfikacja wygasła. Potwierdź formularz ponownie.');
      },
      'error-callback': () => {
        this.turnstileToken.set('');
        this.turnstileError.set('Nie udało się zweryfikować formularza. Spróbuj ponownie.');
      },
    });
  }
}
