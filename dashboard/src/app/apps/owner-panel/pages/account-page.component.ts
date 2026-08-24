import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { AuthClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';

@Component({
  selector: 'app-account-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InputTextModule, ButtonModule],
  template: `
    <div class="mx-auto w-full max-w-2xl px-4 py-6 flex flex-col gap-6">
      <header>
        <p class="admin-section-label text-primary">Konto</p>
        <h1 class="text-2xl font-black tracking-tight text-surface-900">Moje konto</h1>
        <p class="text-sm text-surface-500 dark:text-surface-400 mt-1">
          Zmień swoje dane logowania. E-mail i hasło dotyczą Twojego konta w tym salonie.
        </p>
      </header>

      <!-- Profil -->
      <section class="rounded-3xl border border-surface-200/70 dark:border-surface-200/70 bg-white/85 dark:bg-surface-50/55 p-6 flex flex-col gap-4">
        <div>
          <h2 class="text-base font-bold text-surface-900">Nazwa wyświetlana</h2>
          <p class="text-xs text-surface-500 dark:text-surface-400">Widoczna w panelu i podpisach.</p>
        </div>
        <div class="flex flex-col gap-2">
          <label for="displayName" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Nazwa</label>
          <input
            pInputText id="displayName" data-testid="account-display-name"
            [value]="displayName()" (input)="displayName.set($any($event.target).value)"
            class="w-full py-3 px-4 rounded-xl"
          />
        </div>
        <div class="flex justify-end">
          <p-button
            label="Zapisz nazwę" icon="pi pi-check" size="small"
            [loading]="busyProfile()" [disabled]="!displayName().trim()"
            (onClick)="saveProfile()" />
        </div>
      </section>

      <!-- E-mail -->
      <section class="rounded-3xl border border-surface-200/70 dark:border-surface-200/70 bg-white/85 dark:bg-surface-50/55 p-6 flex flex-col gap-4">
        <div>
          <h2 class="text-base font-bold text-surface-900">Adres e-mail (login)</h2>
          <p class="text-xs text-surface-500 dark:text-surface-400">
            Obecny: <span class="font-semibold text-surface-700 dark:text-surface-300">{{ currentEmail() || '—' }}</span>
          </p>
          <p class="text-xs text-surface-500 dark:text-surface-400 mt-1">
            Dla bezpieczeństwa wyślemy link potwierdzający na nowy adres — e-mail zmieni się po jego kliknięciu.
          </p>
        </div>
        @if (emailLinkSentTo(); as sent) {
          <div class="rounded-2xl border border-emerald-200 dark:border-emerald-900/50 bg-emerald-50/70 dark:bg-emerald-950/30 px-4 py-3 text-sm text-emerald-800 dark:text-emerald-200 flex items-start gap-2">
            <i class="pi pi-envelope mt-0.5"></i>
            <span>Wysłaliśmy link potwierdzający na <strong>{{ sent }}</strong>. Kliknij go, aby dokończyć zmianę adresu.</span>
          </div>
        }
        <div class="flex flex-col gap-2">
          <label for="newEmail" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Nowy e-mail</label>
          <input
            pInputText id="newEmail" type="email" data-testid="account-new-email"
            [value]="newEmail()" (input)="newEmail.set($any($event.target).value)"
            placeholder="np. wlascicielka@salon.pl"
            class="w-full py-3 px-4 rounded-xl"
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="emailPassword" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Potwierdź hasłem</label>
          <input
            pInputText id="emailPassword" type="password" data-testid="account-email-password"
            [value]="emailPassword()" (input)="emailPassword.set($any($event.target).value)"
            placeholder="Twoje obecne hasło"
            class="w-full py-3 px-4 rounded-xl"
          />
        </div>
        <div class="flex justify-end">
          <p-button
            label="Zmień e-mail" icon="pi pi-envelope" size="small"
            [loading]="busyEmail()" [disabled]="!canSubmitEmail()"
            (onClick)="saveEmail()" />
        </div>
      </section>

      <!-- Hasło -->
      <section class="rounded-3xl border border-surface-200/70 dark:border-surface-200/70 bg-white/85 dark:bg-surface-50/55 p-6 flex flex-col gap-4">
        <div>
          <h2 class="text-base font-bold text-surface-900">Hasło</h2>
          <p class="text-xs text-surface-500 dark:text-surface-400">Minimum 8 znaków.</p>
        </div>
        <div class="flex flex-col gap-2">
          <label for="currentPassword" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Obecne hasło</label>
          <input
            pInputText id="currentPassword" type="password" data-testid="account-current-password"
            [value]="currentPassword()" (input)="currentPassword.set($any($event.target).value)"
            class="w-full py-3 px-4 rounded-xl"
          />
        </div>
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div class="flex flex-col gap-2">
            <label for="newPassword" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Nowe hasło</label>
            <input
              pInputText id="newPassword" type="password" data-testid="account-new-password"
              [value]="newPassword()" (input)="newPassword.set($any($event.target).value)"
              class="w-full py-3 px-4 rounded-xl"
            />
          </div>
          <div class="flex flex-col gap-2">
            <label for="confirmPassword" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Powtórz nowe hasło</label>
            <input
              pInputText id="confirmPassword" type="password" data-testid="account-confirm-password"
              [value]="confirmPassword()" (input)="confirmPassword.set($any($event.target).value)"
              class="w-full py-3 px-4 rounded-xl"
            />
          </div>
        </div>
        @if (passwordMismatch()) {
          <small class="text-red-500 dark:text-red-400 text-xs font-medium px-1">Hasła nie są takie same.</small>
        }
        <div class="flex justify-end">
          <p-button
            label="Zmień hasło" icon="pi pi-lock" size="small"
            [loading]="busyPassword()" [disabled]="!canSubmitPassword()"
            (onClick)="savePassword()" />
        </div>
      </section>
    </div>
  `,
})
export class AccountPageComponent {
  private readonly authClient = inject(AuthClient);
  private readonly auth = inject(AuthSessionService);
  private readonly messages = inject(MessageService);

  protected readonly currentEmail = computed(() => this.auth.session()?.email ?? '');

  protected readonly displayName = signal<string>(this.auth.session()?.displayName ?? '');
  protected readonly newEmail = signal('');
  protected readonly emailPassword = signal('');
  /** Adres, na który wysłano link potwierdzający zmianę e-maila (info do wyświetlenia). */
  protected readonly emailLinkSentTo = signal<string | null>(null);
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');

  protected readonly busyProfile = signal(false);
  protected readonly busyEmail = signal(false);
  protected readonly busyPassword = signal(false);

  protected readonly passwordMismatch = computed(
    () => this.confirmPassword().length > 0 && this.newPassword() !== this.confirmPassword(),
  );

  protected readonly canSubmitEmail = computed(
    () => this.newEmail().trim().length > 0 && this.emailPassword().length > 0,
  );

  protected readonly canSubmitPassword = computed(
    () =>
      this.currentPassword().length > 0 &&
      this.newPassword().length >= 8 &&
      this.newPassword() === this.confirmPassword(),
  );

  saveProfile(): void {
    if (this.busyProfile()) return;
    this.busyProfile.set(true);
    this.authClient.updateMyProfile({ displayName: this.displayName().trim() }).subscribe({
      next: () => {
        this.auth.refreshSession().subscribe();
        this.messages.add({ severity: 'success', summary: 'Zapisano', detail: 'Nazwa wyświetlana zaktualizowana.' });
        this.busyProfile.set(false);
      },
      error: () => this.busyProfile.set(false),
    });
  }

  saveEmail(): void {
    if (this.busyEmail() || !this.canSubmitEmail()) return;
    this.busyEmail.set(true);
    const target = this.newEmail().trim();
    this.authClient
      .changeMyEmail({ currentPassword: this.emailPassword(), newEmail: target })
      .subscribe({
        next: () => {
          this.messages.add({
            severity: 'success',
            summary: 'Sprawdź skrzynkę',
            detail: `Wysłaliśmy link potwierdzający na ${target}.`,
          });
          this.emailLinkSentTo.set(target);
          this.newEmail.set('');
          this.emailPassword.set('');
          this.busyEmail.set(false);
        },
        error: () => this.busyEmail.set(false),
      });
  }

  savePassword(): void {
    if (this.busyPassword() || !this.canSubmitPassword()) return;
    this.busyPassword.set(true);
    this.authClient
      .changeMyPassword({ currentPassword: this.currentPassword(), newPassword: this.newPassword() })
      .subscribe({
        next: () => {
          this.messages.add({ severity: 'success', summary: 'Zmieniono hasło', detail: 'Hasło zostało zaktualizowane.' });
          this.currentPassword.set('');
          this.newPassword.set('');
          this.confirmPassword.set('');
          this.busyPassword.set(false);
        },
        error: () => this.busyPassword.set(false),
      });
  }
}
