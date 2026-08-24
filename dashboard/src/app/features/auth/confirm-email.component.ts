import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthClient } from '@core/api/api-client';

@Component({
  selector: 'app-confirm-email',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <section class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 text-center space-y-4">
        <p class="admin-section-label text-primary">Weryfikacja</p>
        <h1 class="text-2xl font-black tracking-tight">Potwierdzenie emaila</h1>
        <p class="text-surface-600 dark:text-surface-300">{{ message() }}</p>
        @if (smsFailed()) {
          <p
            class="rounded-lg bg-amber-50 dark:bg-amber-950/30 border border-amber-200 dark:border-amber-900/50 px-3 py-2 text-xs text-amber-800 dark:text-amber-200"
          >
            Nie udało się wysłać SMS-a z kodem. Po przejściu do następnego kroku kliknij „wyślij ponownie".
          </p>
        }
        <a routerLink="/login" class="text-primary hover:underline font-semibold"
          >Przejdź do logowania</a
        >
      </section>
    </div>
  `,
})
export class ConfirmEmailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authClient = inject(AuthClient);

  protected readonly message = signal('Potwierdzamy adres email...');
  protected readonly smsFailed = signal(false);

  ngOnInit(): void {
    const userId = this.route.snapshot.queryParamMap.get('userId');
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!userId || !token) {
      this.message.set('Link potwierdzający jest nieprawidłowy.');
      return;
    }

    this.authClient.confirmEmail({ userId, token }).subscribe({
      next: (response) => {
        if (response.phoneNumberConfirmed) {
          this.message.set('Adres email został potwierdzony. Możesz się teraz zalogować.');
          return;
        }
        if (!response.phoneOtpSent) {
          this.smsFailed.set(true);
        }
        const masked = response.maskedPhone;
        this.message.set(
          masked
            ? `Email potwierdzony. Wpisz kod, który wysłaliśmy na ${masked}.`
            : 'Email potwierdzony. Wpisz kod, który wysłaliśmy SMS-em.',
        );
        // Krótka pauza dla user feedback, potem przekierowanie do potwierdzenia telefonu.
        //
        // `masked` przekazujemy dalej, bo ten ekran znika po 1200 ms — numer migał tu i przepadał
        // dokładnie przed ekranem, na którym jest potrzebny. Bez niego literówka w numerze kończy
        // się czekaniem na SMS, który nigdy nie przyjdzie, bez żadnej wskazówki. Numer jest
        // zamaskowany (+*****567), więc w URL-u nie ujawnia niczego.
        setTimeout(() => {
          void this.router.navigate(['/confirm-phone'], {
            queryParams: {
              userId: response.userId,
              smsFailed: this.smsFailed() ? '1' : null,
              masked: masked || null,
            },
            replaceUrl: true,
          });
        }, 1200);
      },
      error: () => this.message.set('Nie udało się potwierdzić adresu email.'),
    });
  }
}
