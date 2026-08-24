import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthClient } from '@core/api/api-client';

@Component({
  selector: 'app-confirm-change-email',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <section class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 text-center space-y-4">
        <p class="admin-section-label text-primary">Zmiana e-maila</p>
        <h1 class="text-2xl font-black tracking-tight">Potwierdzenie nowego adresu</h1>
        <p class="text-surface-600 dark:text-surface-300">{{ message() }}</p>
        @if (done()) {
          <a routerLink="/login" class="text-primary hover:underline font-semibold">Przejdź do logowania</a>
        }
      </section>
    </div>
  `,
})
export class ConfirmChangeEmailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly authClient = inject(AuthClient);

  protected readonly message = signal('Potwierdzamy nowy adres e-mail...');
  protected readonly done = signal(false);

  ngOnInit(): void {
    const userId = this.route.snapshot.queryParamMap.get('userId');
    const token = this.route.snapshot.queryParamMap.get('token');
    const email = this.route.snapshot.queryParamMap.get('email');
    if (!userId || !token || !email) {
      this.message.set('Link potwierdzający jest nieprawidłowy.');
      this.done.set(true);
      return;
    }

    this.authClient.confirmChangeEmail({ userId, token, email }).subscribe({
      next: () => {
        this.message.set(
          `Adres e-mail został zmieniony na ${email}. Zaloguj się, używając nowego adresu.`,
        );
        this.done.set(true);
      },
      error: () => {
        this.message.set('Nie udało się potwierdzić zmiany adresu — link jest nieprawidłowy lub wygasł.');
        this.done.set(true);
      },
    });
  }
}
