import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { AuthClient } from '@core/api/api-client';

interface PwRules {
  len: boolean;
  lower: boolean;
  upper: boolean;
  digit: boolean;
  special: boolean;
}

/** Reguły hasła zgodne z polityką Identity backendu (Program.cs: min. 8 + lower/upper/digit/special). */
function evalPassword(p: string): PwRules {
  return {
    len: p.length >= 8,
    lower: /[a-z]/.test(p),
    upper: /[A-Z]/.test(p),
    digit: /[0-9]/.test(p),
    special: /[^A-Za-z0-9]/.test(p),
  };
}

function pwValid(r: PwRules): boolean {
  return r.len && r.lower && r.upper && r.digit && r.special;
}

const PW_LABELS: { key: keyof PwRules; label: string }[] = [
  { key: 'len', label: 'min. 8 znaków' },
  { key: 'lower', label: 'mała litera' },
  { key: 'upper', label: 'wielka litera' },
  { key: 'digit', label: 'cyfra' },
  { key: 'special', label: 'znak specjalny' },
];

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

@Component({
  selector: 'app-reception-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InputTextModule, ButtonModule],
  template: `
    <div class="mx-auto w-full max-w-2xl px-4 py-6 flex flex-col gap-6">
      <header>
        <p class="admin-section-label text-primary">Zespół</p>
        <h1 class="text-2xl font-black tracking-tight text-surface-900">Konto recepcji</h1>
        <p class="text-sm text-surface-500 dark:text-surface-400 mt-1">
          Wspólne konto „Recepcja" na laptopa w salonie. Obsługuje kalendarz i wizyty wszystkich
          pracowniczek, ale nie ma dostępu do ustawień, danych zespołu ani rozliczeń.
        </p>
      </header>

      @if (status.isLoading()) {
        <div class="rounded-3xl border border-surface-200/70 dark:border-surface-200/70 p-6 text-sm text-surface-500">Ładowanie…</div>
      } @else if (exists()) {
        <!-- Konto istnieje: pokaż login + reset hasła -->
        <section class="rounded-3xl border border-surface-200/70 dark:border-surface-200/70 bg-white/85 dark:bg-surface-50/55 p-6 flex flex-col gap-4">
          <div class="flex items-center gap-3">
            <span class="grid size-11 place-items-center rounded-2xl bg-primary/12 text-primary">
              <i class="pi pi-desktop text-xl"></i>
            </span>
            <div>
              <h2 class="text-base font-bold text-surface-900">Konto aktywne</h2>
              <p class="text-xs text-surface-500 dark:text-surface-400">Login: <span class="font-semibold text-surface-700 dark:text-surface-300">{{ accountEmail() }}</span></p>
            </div>
          </div>
          <p class="text-sm text-surface-600 dark:text-surface-400">
            Zaloguj się tym adresem na laptopie w salonie. Jeśli hasło wyciekło lub zgubiliście
            urządzenie — ustaw nowe poniżej.
          </p>
        </section>

        <section class="rounded-3xl border border-surface-200/70 dark:border-surface-200/70 bg-white/85 dark:bg-surface-50/55 p-6 flex flex-col gap-4">
          <h2 class="text-base font-bold text-surface-900">Zresetuj hasło</h2>
          <div class="flex flex-col gap-2">
            <label for="resetPassword" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Nowe hasło</label>
            <input
              pInputText id="resetPassword" type="password" data-testid="reception-reset-password"
              [value]="resetPassword()" (input)="resetPassword.set($any($event.target).value)"
              class="w-full py-3 px-4 rounded-xl"
            />
            <ul class="flex flex-wrap gap-x-3 gap-y-1 px-1 mt-1">
              @for (rule of PW_LABELS; track rule.key) {
                <li class="flex items-center gap-1 text-xs"
                    [class.text-emerald-600]="resetRules()[rule.key]" [class.dark:text-emerald-400]="resetRules()[rule.key]"
                    [class.text-surface-400]="!resetRules()[rule.key]" [class.dark:text-surface-500]="!resetRules()[rule.key]">
                  <i class="pi text-[10px]" [class.pi-check-circle]="resetRules()[rule.key]" [class.pi-circle]="!resetRules()[rule.key]"></i>
                  {{ rule.label }}
                </li>
              }
            </ul>
          </div>
          <div class="flex justify-end">
            <p-button
              label="Ustaw nowe hasło" icon="pi pi-lock" size="small"
              [loading]="busyReset()" [disabled]="!canReset()"
              (onClick)="resetKioskPassword()" />
          </div>
        </section>
      } @else {
        <!-- Brak konta: formularz utworzenia -->
        <section class="rounded-3xl border border-surface-200/70 dark:border-surface-200/70 bg-white/85 dark:bg-surface-50/55 p-6 flex flex-col gap-4">
          <h2 class="text-base font-bold text-surface-900">Utwórz konto recepcji</h2>
          <div class="flex flex-col gap-2">
            <label for="email" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">E-mail (login)</label>
            <input
              pInputText id="email" type="email" data-testid="reception-email" data-tour="reception-email"
              [value]="email()" (input)="email.set($any($event.target).value)"
              placeholder="np. recepcja@salon.pl"
              [class.!border-red-400]="emailTouched()"
              class="w-full py-3 px-4 rounded-xl"
            />
            @if (emailTouched()) {
              <small class="text-red-500 dark:text-red-400 text-xs px-1">Podaj poprawny adres e-mail (np. recepcja&#64;salon.pl).</small>
            } @else {
              <p class="text-xs text-surface-500 dark:text-surface-400 px-1">Nie musi to być prawdziwa skrzynka — służy tylko do logowania.</p>
            }
          </div>
          <div class="flex flex-col gap-2">
            <label for="password" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">Hasło</label>
            <input
              pInputText id="password" type="password" data-testid="reception-password"
              [value]="password()" (input)="password.set($any($event.target).value)"
              class="w-full py-3 px-4 rounded-xl"
            />
            <ul class="flex flex-wrap gap-x-3 gap-y-1 px-1 mt-1">
              @for (rule of PW_LABELS; track rule.key) {
                <li class="flex items-center gap-1 text-xs"
                    [class.text-emerald-600]="createRules()[rule.key]" [class.dark:text-emerald-400]="createRules()[rule.key]"
                    [class.text-surface-400]="!createRules()[rule.key]" [class.dark:text-surface-500]="!createRules()[rule.key]">
                  <i class="pi text-[10px]" [class.pi-check-circle]="createRules()[rule.key]" [class.pi-circle]="!createRules()[rule.key]"></i>
                  {{ rule.label }}
                </li>
              }
            </ul>
          </div>
          <div class="flex justify-end">
            <p-button
              data-tour="reception-create" label="Utwórz konto" icon="pi pi-check" size="small"
              [loading]="busyCreate()" [disabled]="!canCreate()"
              (onClick)="createKiosk()" />
          </div>
        </section>
      }
    </div>
  `,
})
export class ReceptionPageComponent {
  private readonly authClient = inject(AuthClient);
  private readonly messages = inject(MessageService);

  protected readonly PW_LABELS = PW_LABELS;

  protected readonly status = rxResource({ stream: () => this.authClient.getKioskAccount() });

  protected readonly exists = computed(() => this.status.value()?.exists === true);
  protected readonly accountEmail = computed(() => this.status.value()?.email ?? '');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly resetPassword = signal('');
  protected readonly busyCreate = signal(false);
  protected readonly busyReset = signal(false);

  protected readonly emailValid = computed(() => EMAIL_RE.test(this.email().trim()));
  /** Błąd e-maila pokazujemy dopiero gdy pole nie jest puste, a wartość niepoprawna. */
  protected readonly emailTouched = computed(() => this.email().length > 0 && !this.emailValid());

  protected readonly createRules = computed(() => evalPassword(this.password()));
  protected readonly resetRules = computed(() => evalPassword(this.resetPassword()));

  protected readonly canCreate = computed(() => this.emailValid() && pwValid(this.createRules()));
  protected readonly canReset = computed(() => pwValid(this.resetRules()));

  createKiosk(): void {
    if (this.busyCreate() || !this.canCreate()) return;
    this.busyCreate.set(true);
    this.authClient
      .createKioskAccount({ email: this.email().trim(), password: this.password() })
      .subscribe({
        next: () => {
          this.messages.add({ severity: 'success', summary: 'Utworzono', detail: 'Konto recepcji jest gotowe.' });
          this.password.set('');
          this.status.reload();
          this.busyCreate.set(false);
        },
        error: () => this.busyCreate.set(false),
      });
  }

  resetKioskPassword(): void {
    if (this.busyReset() || !this.canReset()) return;
    this.busyReset.set(true);
    this.authClient.resetKioskPassword({ password: this.resetPassword() }).subscribe({
      next: () => {
        this.messages.add({ severity: 'success', summary: 'Zmieniono', detail: 'Nowe hasło recepcji zostało ustawione.' });
        this.resetPassword.set('');
        this.busyReset.set(false);
      },
      error: () => this.busyReset.set(false),
    });
  }
}
