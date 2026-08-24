import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-login-failed',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10 text-center">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <section class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 space-y-4">
        <div class="mx-auto grid size-14 place-items-center rounded-full bg-red-100 text-red-600 dark:bg-red-900/40 dark:text-red-300">
          <i class="pi pi-times text-xl"></i>
        </div>
        <p class="admin-section-label text-primary">Autoryzacja</p>
        <h1 class="text-2xl font-black tracking-tight">Logowanie nie powiodło się</h1>
        <p class="text-surface-600 dark:text-surface-300 max-w-md text-sm leading-relaxed">
          Sprawdź email, hasło lub powiązanie konta z pracownikiem. Możesz spróbować ponownie.
        </p>
        <a
          routerLink="/login"
          class="inline-flex px-6 py-2.5 rounded-full bg-primary text-primary-contrast font-semibold no-underline"
        >
          Wróć do logowania
        </a>
      </section>
    </div>
  `,
})
export class LoginFailedComponent {}
