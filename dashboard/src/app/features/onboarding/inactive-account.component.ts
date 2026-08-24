import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { AuthSessionService } from '@core/auth/auth-session.service';

/**
 * Ślepy zaułek konta po dezaktywacji pracownika — NIE krok kreatora.
 *
 * Zdezaktywowana pracownica zachowuje konto User z rolą Employee, ale traci aktywny rekord
 * `Employee`, więc backend nie umie rozwiązać jej tenanta. `onboardingGuard` odbija każdy
 * nieukończony onboarding na `/setup`, przez co trafiała dotąd na kreator ZAKŁADANIA SALONU:
 * komunikat absurdalny (zwolnionej osobie proponujemy założenie własnego salonu) i tak czy owak
 * ślepy, bo mutacje kreatora wymagają `BusinessManagement`, a `CompleteProfile` dodatkowo
 * potwierdzonego telefonu. Ten ekran mówi wprost, co się stało, i daje jedyne sensowne wyjście.
 *
 * Świadomie BEZ nazwy salonu i bez nazwiska osoby, która dezaktywowała konto — to konto już nie
 * ma prawa dostępu do danych salonu, więc ekran po wylogowaniu nie może być ich źródłem.
 */
@Component({
  selector: 'app-inactive-account',
  standalone: true,
  imports: [ButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-dvh items-center justify-center p-6">
      <div
        class="w-full max-w-md rounded-3xl border border-surface-200 dark:border-surface-700 bg-surface-0 dark:bg-surface-900 p-8 text-center shadow-lg"
      >
        <i class="pi pi-lock mb-4 text-4xl text-surface-400" aria-hidden="true"></i>

        <h1 class="text-xl font-bold text-surface-900 dark:text-surface-0">
          Twoje konto nie ma już dostępu
        </h1>

        <p class="mt-3 text-sm leading-relaxed text-surface-600 dark:text-surface-300">
          Dostęp do panelu został wyłączony przez salon. Jeśli uważasz, że to pomyłka, skontaktuj
          się z osobą prowadzącą salon — tylko ona może przywrócić Ci dostęp.
        </p>

        <p-button
          label="Wyloguj się"
          severity="secondary"
          styleClass="mt-6 w-full"
          (onClick)="logout()"
        />
      </div>
    </div>
  `,
})
export class InactiveAccountComponent {
  private readonly auth = inject(AuthSessionService);

  logout(): void {
    this.auth.logout();
  }
}
