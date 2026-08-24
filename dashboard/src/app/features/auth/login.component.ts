import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  OnDestroy,
  OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { email, form, FormField, maxLength, required, submit, validate } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { defaultAdminRouteForRole } from '@core/auth/default-admin-route';
import { getAuthProblemJson, partitionLoginAuthErrors } from '@core/errors/auth-form-field-errors';
import { maskEmail, readOnboardingPending } from '@core/auth/onboarding-pending';
import { environment } from '../../../environments/environment';
import { lastValueFrom } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { CheckboxModule } from 'primeng/checkbox';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';

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

interface LoginFormModel {
  email: string;
  password: string;
  rememberMe: boolean;
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [RouterLink, FormFieldComponent, FormField, FormsModule, CheckboxModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="min-h-dvh relative flex flex-col items-center justify-center gap-8 px-6 py-10"
    >
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <form
        (submit)="$event.preventDefault(); loginSubmit()"
        class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 space-y-6"
      >
        <div class="space-y-2 text-center">
          <p class="admin-section-label text-primary">Logowanie</p>
          <h1 class="text-3xl font-black tracking-tight">Panel salonu</h1>
          <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
            Zaloguj się kontem właściciela, managera albo pracownika.
          </p>
        </div>

        @if (justVerified()) {
          <div
            class="rounded-xl border border-emerald-400/35 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-800 dark:text-emerald-200"
            role="status"
          >
            Konto potwierdzone. Zaloguj się, aby dokończyć konfigurację salonu.
          </div>
        } @else if (resumeEmail()) {
          <div
            class="rounded-xl border border-amber-300/40 bg-amber-300/10 px-4 py-3 text-sm text-amber-900 dark:text-amber-200"
            role="status"
          >
            Dokończ rejestrację — wysłaliśmy link na {{ resumeEmail() }}.
          </div>
        }

        <div class="space-y-4">
          <app-form-field
            testId="login-email"
            label="Email"
            id="login-email"
            type="email"
            placeholder="np. jan@example.com"
            [formField]="loginForm.email"
          />

          <app-form-field
            testId="login-password"
            label="Hasło"
            id="login-password"
            type="password"
            placeholder="Hasło"
            [formField]="loginForm.password"
          />
        </div>

        <!--
          p-checkbox zamiast natywnego inputa — natywny renderował się stylem OS (niebieski)
          i odstawał od amber; konwencje projektu zakazują natywnych kontrolek wprost.
          ngModelOptions standalone, bo to pole leży w formularzu, ale NIE należy do modelu
          signal-form (rememberMe czytamy wprost z loginModel) — bez tego Angular wymaga name.
        -->
        <div class="flex items-center gap-3">
          <p-checkbox
            [binary]="true"
            inputId="rememberMe"
            [ngModel]="loginModel().rememberMe"
            [ngModelOptions]="{ standalone: true }"
            (ngModelChange)="setRememberMe($event)"
          />
          <label
            for="rememberMe"
            class="text-sm text-surface-600 dark:text-surface-300 cursor-pointer select-none"
          >
            Zapamiętaj mnie
          </label>
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
          type="submit"
          data-testid="login-submit"
          [disabled]="
            loginForm().invalid() ||
            loginForm().pending() ||
            loading() ||
            (turnstileEnabled && !turnstileToken())
          "
          class="w-full px-8 py-3 rounded-xl bg-primary text-primary-contrast font-semibold shadow-lg hover:opacity-95 disabled:opacity-50 transition-opacity"
        >
          {{ loading() ? 'Logowanie…' : 'Zaloguj' }}
        </button>

        <p class="text-center text-sm text-surface-500 dark:text-surface-400">
          Zakładasz pierwszy salon?
          <a routerLink="/register" class="text-primary hover:underline">Załóż konto</a>
        </p>
        <p class="text-center text-sm text-surface-500 dark:text-surface-400">
          <a routerLink="/forgot-password" class="text-primary hover:underline">Nie pamiętasz hasła?</a>
        </p>

        @if (demoEnabled()) {
          <div class="flex items-center gap-3 pt-2">
            <span class="h-px flex-1 bg-surface-300/60 dark:bg-surface-600/60"></span>
            <span class="text-xs uppercase tracking-wide text-surface-400">albo</span>
            <span class="h-px flex-1 bg-surface-300/60 dark:bg-surface-600/60"></span>
          </div>

          <button
            type="button"
            data-testid="demo-start"
            (click)="startDemo()"
            [disabled]="demoLoading() || loading()"
            class="w-full px-8 py-3 rounded-xl border border-amber-400/60 bg-amber-300/15 text-amber-800 dark:text-amber-200 font-semibold hover:bg-amber-300/25 disabled:opacity-50 transition-colors"
          >
            {{ demoLoading() ? 'Przygotowuję demo…' : '✨ Wypróbuj demo bez logowania' }}
          </button>
          <p class="text-center text-xs text-surface-400">
            Gotowy salon z przykładowymi wizytami i klientkami. Bez zakładania konta.
          </p>
        }
      </form>
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
export class LoginComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly auth = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly turnstileContainer = viewChild<ElementRef<HTMLElement>>('turnstileContainer');

  protected readonly loading = signal(false);
  protected readonly justVerified = signal(false);
  protected readonly resumeEmail = signal<string | null>(null);
  protected readonly demoEnabled = signal(false);
  protected readonly demoLoading = signal(false);
  protected readonly globalError = signal('');
  private readonly serverFieldErrors = signal<Record<string, string[]>>({});
  protected readonly turnstileEnabled = !!environment.turnstileSiteKey;
  protected readonly turnstileToken = signal('');
  protected readonly turnstileError = signal('');
  private turnstileWidgetId: string | null = null;

  protected readonly loginModel = signal<LoginFormModel>({
    email: '',
    password: '',
    rememberMe: false,
  });

  protected readonly loginForm = form(this.loginModel, (schemaPath) => {
    required(schemaPath.email, { message: 'Email jest wymagany' });
    maxLength(schemaPath.email, 255);
    email(schemaPath.email, { message: 'Podaj poprawny adres email' });
    validate(schemaPath.email, () => this.serverMessages('email'));

    required(schemaPath.password, { message: 'Hasło jest wymagane' });
    validate(schemaPath.password, () => this.serverMessages('password'));
  });

  ngOnInit(): void {
    // Banner „dokończ rejestrację" (porzucony onboarding) / „konto potwierdzone" (po confirm-phone).
    this.justVerified.set(this.route.snapshot.queryParamMap.get('justVerified') === '1');
    const pending = readOnboardingPending();
    this.resumeEmail.set(pending ? maskEmail(pending.email) : null);

    // Podpowiedz e-mail, którym konto zostało założone. Adres i tak tu mamy (banner wyżej pokazuje
    // go zamaskowanego), a użytkowniczka trafia na ten ekran PROSTO z potwierdzenia telefonu —
    // kazanie jej wpisywać adres od nowa było zwykłym marnotrawstwem. Nadpisujemy tylko puste pole,
    // żeby nie kasować czegoś, co ktoś zdążył wpisać sam.
    if (pending?.email && !this.loginModel().email) {
      this.loginModel.update((m) => ({ ...m, email: pending.email }));
    }

    // Deep-link z landingu: /login?demo=1 wpada od razu w demo (jeśli włączone na backendzie).
    const autoStartDemo = this.route.snapshot.queryParamMap.get('demo') === '1';
    this.auth.demoEnabled().subscribe((enabled) => {
      this.demoEnabled.set(enabled);
      if (enabled && autoStartDemo && !this.demoLoading()) {
        void this.startDemo();
      }
    });
  }

  protected async startDemo(): Promise<void> {
    if (this.demoLoading()) {
      return;
    }
    this.demoLoading.set(true);
    this.globalError.set('');
    try {
      await lastValueFrom(this.auth.startDemo());
      await this.router.navigate(
        [defaultAdminRouteForRole(this.auth.currentRole(), this.auth.currentEmployeeId())],
        { replaceUrl: true },
      );
    } catch {
      this.globalError.set('Nie udało się uruchomić demo. Spróbuj ponownie za chwilę.');
    } finally {
      this.demoLoading.set(false);
    }
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

  setRememberMe(checked: boolean): void {
    this.loginModel.update((m) => ({ ...m, rememberMe: checked }));
  }

  protected async loginSubmit(): Promise<void> {
    if (this.turnstileEnabled && !this.turnstileToken()) {
      this.turnstileError.set('Potwierdź zabezpieczenie formularza.');
      return;
    }

    await submit(this.loginForm, async () => {
      const root = this.loginForm();
      if (root.invalid() || root.pending()) {
        return;
      }

      this.serverFieldErrors.set({});
      this.globalError.set('');
      this.loading.set(true);
      try {
        const value = root.value() as LoginFormModel;
        await lastValueFrom(
          this.auth.login({
            email: value.email.trim(),
            password: value.password,
            rememberMe: value.rememberMe,
            turnstileToken: this.turnstileToken() || undefined,
          }),
        );
        await this.router.navigate(
          [defaultAdminRouteForRole(this.auth.currentRole(), this.auth.currentEmployeeId())],
          { replaceUrl: true },
        );
      } catch (err: unknown) {
        // Gdy login zwraca PhoneNotConfirmed (errorCode=auth.phone_not_confirmed), backend
        // dorzuca `userId` w body — przekierowujemy na /confirm-phone zamiast pokazać error.
        if (this.isPhoneNotConfirmed(err)) {
          const userId = this.extractUserIdFromProblem(err);
          await this.router.navigate(['/confirm-phone'], {
            queryParams: { userId },
            replaceUrl: true,
          });
          return;
        }
        const { fieldErrors, globalMessage } = partitionLoginAuthErrors(err);
        this.serverFieldErrors.set(fieldErrors);
        this.globalError.set(globalMessage ?? '');
        // Token Turnstile jest jednorazowy (Cloudflare zużywa go przy siteverify, także gdy
        // logowanie padnie na złym haśle). Bez resetu druga próba wysyła spalony token i backend
        // odrzuca ją inną, mylącą przyczyną. Resetujemy widget → nowy token do kolejnej próby.
        this.resetTurnstile();
        throw err;
      } finally {
        this.loading.set(false);
      }
    });
  }

  // Błąd z `authClient.login()` to NSwag `ApiException` (ciało ProblemDetails w `.response`
  // jako string), a NIE `HttpErrorResponse` z gotowym `.error`. Dlatego czytamy ciało przez
  // `getAuthProblemJson`, które ogarnia oba kształty — inaczej PhoneNotConfirmed nigdy się nie
  // wykryje i logowanie pokazuje mylące "Nieprawidłowy email lub hasło".
  private isPhoneNotConfirmed(err: unknown): boolean {
    return getAuthProblemJson(err)?.['errorCode'] === 'auth.phone_not_confirmed';
  }

  private extractUserIdFromProblem(err: unknown): string | undefined {
    const userId = getAuthProblemJson(err)?.['userId'];
    return typeof userId === 'string' ? userId : undefined;
  }

  private serverMessages(field: 'email' | 'password'): { kind: string; message: string }[] {
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

  private resetTurnstile(): void {
    if (!this.turnstileEnabled) {
      return;
    }
    this.turnstileToken.set('');
    if (this.turnstileWidgetId && window.turnstile) {
      window.turnstile.reset(this.turnstileWidgetId);
    }
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
