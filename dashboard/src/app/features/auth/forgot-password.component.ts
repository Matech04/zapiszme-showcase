import { AfterViewInit, Component, ElementRef, inject, OnDestroy, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthClient } from '@core/api/api-client';
import { environment } from '../../../environments/environment';
import { finalize } from 'rxjs';

// Globalne typy Turnstile zadeklarowane w login.component.ts (window.turnstile).

@Component({
  selector: 'app-forgot-password',
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
          <p class="admin-section-label text-primary">Reset hasła</p>
          <h1 class="text-2xl font-black tracking-tight">Odzyskiwanie dostępu</h1>
          <p class="text-sm text-surface-600 dark:text-surface-300">
            Podaj email, a wyślemy link do ustawienia nowego hasła.
          </p>
        </div>

        <label class="block space-y-2">
          <span class="text-xs font-bold uppercase tracking-widest text-surface-500 dark:text-surface-400">Email</span>
          <input
            data-testid="forgot-email"
            type="email"
            formControlName="email"
            autocomplete="email"
            class="w-full rounded-xl bg-white/80 dark:bg-surface-950 border border-surface-300 dark:border-surface-200 px-4 py-3 text-surface-900 outline-none focus:border-primary"
          />
        </label>

        @if (turnstileEnabled) {
          <div class="space-y-2">
            <div #turnstileContainer></div>
            @if (turnstileError()) {
              <p class="text-sm text-red-300">{{ turnstileError() }}</p>
            }
          </div>
        }

        <button
          type="submit"
          data-testid="forgot-submit"
          [disabled]="form.invalid || loading() || (turnstileEnabled && !turnstileToken())"
          class="w-full px-8 py-3 rounded-xl bg-primary text-primary-contrast font-semibold shadow-lg hover:opacity-95 disabled:opacity-50 transition-opacity"
        >
          {{ loading() ? 'Wysyłanie...' : 'Wyślij link' }}
        </button>

        @if (message()) {
          <p class="text-sm text-center text-surface-600 dark:text-surface-300">{{ message() }}</p>
        }

        <p class="text-center text-sm text-surface-500 dark:text-surface-400">
          <a routerLink="/login" class="text-primary hover:underline">Wróć do logowania</a>
        </p>
      </form>
    </div>
  `,
})
export class ForgotPasswordComponent implements AfterViewInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly authClient = inject(AuthClient);
  private readonly turnstileContainer = viewChild<ElementRef<HTMLElement>>('turnstileContainer');

  protected readonly loading = signal(false);
  protected readonly message = signal('');
  protected readonly turnstileEnabled = !!environment.turnstileSiteKey;
  protected readonly turnstileToken = signal('');
  protected readonly turnstileError = signal('');
  private turnstileWidgetId: string | null = null;

  form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

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

  submit(): void {
    if (this.form.invalid || this.loading()) {
      return;
    }
    if (this.turnstileEnabled && !this.turnstileToken()) {
      this.turnstileError.set('Potwierdź zabezpieczenie formularza.');
      return;
    }

    this.loading.set(true);
    this.authClient
      .forgotPassword({
        ...this.form.getRawValue(),
        turnstileToken: this.turnstileToken() || undefined,
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: () => this.message.set('Jeśli konto istnieje, link resetujący został wysłany.'),
        error: () => this.message.set('Nie udało się wysłać linku resetującego.'),
      });
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
      callback: (token: string) => {
        this.turnstileToken.set(token);
        this.turnstileError.set('');
      },
      'expired-callback': () => {
        this.turnstileToken.set('');
      },
      'error-callback': () => {
        this.turnstileToken.set('');
        this.turnstileError.set('Nie udało się zweryfikować zabezpieczenia formularza.');
      },
    });
  }
}
