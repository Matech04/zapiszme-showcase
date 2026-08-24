import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  EMPTY,
  lastValueFrom,
  map,
  switchMap,
  tap,
} from 'rxjs';
import { AuthClient, OnboardingClient, OnboardingStateDto } from '@core/api/api-client';
import { environment } from '@env/environment';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { getAuthProblemJson } from '@core/errors/auth-form-field-errors';
import { formatAuthApiError } from '@core/errors/api-error-messages';
import { WizardShellComponent } from '../ui/wizard-shell.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

const SLUG_PATTERN = /^[a-zA-Z0-9-]+$/;

/**
 * Nazwa salonu → link. Backendowy walidator wymaga `^[a-zA-Z0-9-]+$`, a `Guard.ReplaceSpaces`
 * zamienia TYLKO spacje na myślniki i nie rusza polskich znaków — transliteracja musi więc być tutaj,
 * inaczej „Studio Łucja" dawałoby slug odrzucany przez API.
 *
 * `ł` obsługujemy osobno: to jedyna polska litera, która NIE rozkłada się w NFD (nie jest „l"
 * z diakrytykiem, tylko odrębnym znakiem), więc samo zdejmowanie znaków łączących ją przepuszcza.
 */
function toSlug(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/ł/g, 'l')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 100);
}

/**
 * Krok „Nazwa salonu + link". `completeProfile` tworzy tu Tenant + Employee (z imienia/nazwiska
 * zbuforowanych w kroku profilu). Slug sprawdzamy na żywo (`getRegisterOwnerSlugAvailability`);
 * 409 (zajęty) obsługujemy dodatkowo inline po stronie zapisu (idempotencja: powtórka zwróci to samo id).
 */
@Component({
  selector: 'app-onboarding-salon-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WizardShellComponent],
  template: `
    <app-wizard-shell
      step="salon"
      title="Nazwa salonu i link"
      subtitle="Link to publiczny adres, pod którym klienci rezerwują wizyty."
      [nextDisabled]="!canSubmit()"
      [nextPending]="saving()"
      [error]="globalError()"
      (next)="onNext()"
    >
      <div class="grid gap-5">
        <div class="flex flex-col gap-2">
          <label for="salonName" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
            Nazwa salonu
          </label>
          <input
            id="salonName"
            data-testid="setup-salon-name"
            [value]="salonName()"
            (input)="onNameInput($event)"
            placeholder="np. Studio Uroda"
            maxlength="100"
            class="w-full rounded-xl border border-surface-300 bg-surface-0 px-4 py-3 text-sm dark:border-surface-200 dark:bg-surface-50 focus:outline-none focus:ring-2 focus:ring-primary"
          />
        </div>

        <div class="flex flex-col gap-2">
          <label for="salonSlug" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
            Link do kalendarza
          </label>
          <div class="flex items-center gap-2">
            <span class="text-sm text-surface-500 dark:text-surface-400 shrink-0">{{ bookingHost }}/</span>
            <input
              id="salonSlug"
              data-testid="setup-salon-slug"
              [value]="salonSlug()"
              (input)="onSlugInput($event)"
              placeholder="moj-salon"
              maxlength="100"
              autocomplete="off"
              class="w-full rounded-xl border border-surface-300 bg-surface-0 px-4 py-3 text-sm dark:border-surface-200 dark:bg-surface-50 focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </div>
          @if (slugFormatError()) {
            <p class="text-xs text-red-600 dark:text-red-300 px-1">{{ slugFormatError() }}</p>
          } @else if (slugState() === 'loading') {
            <p class="text-xs opacity-70 px-1">Sprawdzanie dostępności…</p>
          } @else if (slugState() === 'ok') {
            <p class="text-xs text-emerald-700 dark:text-emerald-300 px-1">✓ Ten link jest wolny</p>
          } @else if (slugState() === 'taken') {
            <p class="text-xs text-red-600 dark:text-red-300 px-1">
              Ten link jest już zajęty — wybierz inny.
            </p>
          } @else if (slugState() === 'networkError') {
            <p class="text-xs text-amber-600 dark:text-amber-300 px-1">
              Nie udało się sprawdzić dostępności — spróbuj ponownie.
            </p>
          }
        </div>
      </div>
    </app-wizard-shell>
  `,
})
export class OnboardingSalonStepComponent {
  private readonly store = inject(OnboardingWizardStore);
  private readonly onboardingClient = inject(OnboardingClient);
  private readonly authClient = inject(AuthClient);
  private readonly onboardingState = inject(OnboardingStateService);
  private readonly router = inject(Router);

  /**
   * Host publicznego adresu rezerwacji, bez protokołu i końcowego „/” — na prod „zapisz.me”,
   * w dev host web-a tego worktree. Pokazujemy go jako prefiks pola, bo samo „/” nie mówi
   * właścicielowi, że wpisuje człon SWOJEGO publicznego linku dla klientek.
   *
   * Źródłem jest `environment.bookingBaseUrl` (to samo, którego używa ostatni krok kreatora do
   * „Otwórz jak klient”), a nie zaszyte „zapisz.me” — inaczej w dev obiecywalibyśmy adres,
   * pod którym nic nie stoi.
   */
  protected readonly bookingHost = environment.bookingBaseUrl
    .replace(/^https?:\/\//, '')
    .replace(/\/+$/, '');

  protected readonly salonName = signal(this.store.salonName());
  protected readonly salonSlug = signal(this.store.salonSlug());
  protected readonly saving = signal(false);
  protected readonly globalError = signal('');

  protected readonly slugState = signal<'idle' | 'loading' | 'ok' | 'taken' | 'networkError'>('idle');
  private readonly slugCheckedFor = signal<string | null>(null);

  private readonly slugTrimmed = computed(() => this.salonSlug().trim());

  protected readonly slugFormatError = computed(() => {
    const s = this.slugTrimmed();
    if (!s) {
      return '';
    }
    if (!SLUG_PATTERN.test(s)) {
      return 'Link może zawierać tylko litery, cyfry i myślniki.';
    }
    return '';
  });

  protected readonly canSubmit = computed(() => {
    const name = this.salonName().trim();
    const slug = this.slugTrimmed();
    if (!name || !slug || this.slugFormatError()) {
      return false;
    }
    // Slug musi być sprawdzony i wolny.
    return this.slugCheckedFor() === slug && this.slugState() === 'ok';
  });

  /**
   * Slug salonu, który ten użytkownik JUŻ ma (albo '', jeśli salon jeszcze nie powstał).
   * Po powrocie na ten krok sprawdzarka dostępności widziałaby własny slug jako zajęty —
   * bo faktycznie jest zajęty, przez nas sprzed minuty. Bez tego wyjątku „Dalej” zostaje
   * wyszarzone i krok 2 z 8 staje się ślepym zaułkiem.
   */
  private readonly ownSlug = computed(() => this.onboardingState.state()?.slug?.trim() ?? '');

  constructor() {
    // Świeży stan: po utworzeniu salonu krok woła markStale(), więc cache trzyma jeszcze STARY
    // stan (bez slug). Bez tego ownSlug() byłoby puste dokładnie wtedy, gdy jest potrzebne.
    this.onboardingState
      .ensure()
      .pipe(takeUntilDestroyed())
      .subscribe((state) => {
        this.rehydrateFromBackend(state);

        // Stan mógł dojechać PO tym, jak sprawdzarka zdążyła oznaczyć slug jako zajęty —
        // przeliczamy raz jeszcze, żeby nie zostawić użytkownika z martwym „Dalej”.
        const slug = this.slugTrimmed();
        if (slug && slug === this.ownSlug()) {
          this.slugCheckedFor.set(slug);
          this.slugState.set('ok');
        }
      });

    // Bufor drafty na czas sesji (nie trwały — nazwa/slug bez PII i tak leci do backendu w tym kroku).
    toObservable(this.salonSlug)
      .pipe(
        map((s) => s.trim()),
        debounceTime(400),
        distinctUntilChanged(),
        switchMap((slug) => this.checkSlug(slug)),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  /**
   * Odtwarza formularz po F5 (albo wejściu z innego urządzenia). Bufor kreatora żyje w pamięci
   * i localStorage, więc odświeżenie zostawiało puste pola mimo istniejącego salonu — a plan mówi
   * wprost: „po utworzeniu tenanta stanem jest baza, nie przeglądarka”.
   *
   * Wypełniamy TYLKO puste pola — świeżo wpisany draft ma pierwszeństwo przed zapisanym stanem,
   * inaczej odpowiedź z backendu skasowałaby to, co użytkownik właśnie pisze.
   *
   * Imię/nazwisko idą do store'a, bo `onNext` bierze je stamtąd do `completeProfile`; bez tego
   * F5 z wyczyszczonym localStorage odbijał z powrotem na krok „profil”.
   */
  private rehydrateFromBackend(state: OnboardingStateDto | null): void {
    if (!state?.hasTenant) {
      return;
    }
    if (!this.salonName().trim() && state.salonName) {
      this.salonName.set(state.salonName);
    }
    if (!this.salonSlug().trim() && state.slug) {
      this.salonSlug.set(state.slug);
      // Salon już istnieje → jego link jest USTALONY. Blokujemy podpowiadanie z nazwy, inaczej
      // poprawienie nazwy po powrocie cicho zmieniłoby publiczny adres, który klientki już mają.
      this.slugTouchedByUser.set(true);
    }
    if (!this.store.firstName().trim() && state.firstName) {
      this.store.firstName.set(state.firstName);
    }
    if (!this.store.lastName().trim() && state.lastName) {
      this.store.lastName.set(state.lastName);
    }
  }

  /**
   * Gdy właścicielka sama ruszy link, przestajemy go podpowiadać z nazwy. Bez tego dalsze
   * poprawianie nazwy nadpisywałoby jej świadomy wybór.
   */
  private readonly slugTouchedByUser = signal(false);

  protected onNameInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.salonName.set(value);
    // Link podpowiadamy z nazwy — to pole, przy którym ludzie zamierają („co mam wpisać?").
    // Zawsze edytowalny; podpowiadanie ustaje po pierwszej ręcznej zmianie.
    if (!this.slugTouchedByUser()) {
      this.salonSlug.set(toSlug(value));
    }
    this.globalError.set('');
  }

  protected onSlugInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.salonSlug.set(value);
    this.slugTouchedByUser.set(true);
    this.globalError.set('');
  }

  private checkSlug(slug: string) {
    if (!slug || !SLUG_PATTERN.test(slug) || slug.length > 100) {
      this.slugCheckedFor.set(null);
      this.slugState.set('idle');
      return EMPTY;
    }
    // Własny slug jest „zajęty" przez nas samych — to nie kolizja. Bez HTTP, bo endpoint
    // dostępności jest z czasu rejestracji (anonimowy) i nie wie, czyj jest ten salon.
    if (slug === this.ownSlug()) {
      this.slugCheckedFor.set(slug);
      this.slugState.set('ok');
      return EMPTY;
    }
    this.slugState.set('loading');
    return this.authClient.getRegisterOwnerSlugAvailability(slug).pipe(
      tap((dto) => {
        this.slugCheckedFor.set(slug);
        // Druga siatka na wypadek wyścigu: gdyby stan dojechał dopiero w trakcie żądania,
        // nadal nie chcemy pokazać własnego linku jako zajętego.
        this.slugState.set(dto.available || slug === this.ownSlug() ? 'ok' : 'taken');
      }),
      catchError(() => {
        this.slugCheckedFor.set(slug);
        this.slugState.set('networkError');
        return EMPTY;
      }),
    );
  }

  protected async onNext(): Promise<void> {
    if (!this.canSubmit() || this.saving()) {
      return;
    }
    // Imię/nazwisko z kroku profilu — jeśli brak (wejście z linku), cofnij do profilu.
    const firstName = this.store.firstName().trim();
    const lastName = this.store.lastName().trim();
    if (!firstName || !lastName) {
      void this.router.navigate(['/setup/profile']);
      return;
    }

    this.saving.set(true);
    this.globalError.set('');
    this.store.salonName.set(this.salonName().trim());
    this.store.salonSlug.set(this.slugTrimmed());
    try {
      await lastValueFrom(
        this.onboardingClient.completeProfile({
          firstName,
          lastName,
          salonName: this.salonName().trim(),
          salonSlug: this.slugTrimmed(),
        }),
      );
      this.onboardingState.markStale();
      void this.router.navigate(['/setup/industry']);
    } catch (err: unknown) {
      const code = getAuthProblemJson(err)?.['errorCode'];
      if (code === 'tenant.slug_taken') {
        this.slugCheckedFor.set(this.slugTrimmed());
        this.slugState.set('taken');
        this.globalError.set('Ten link jest już zajęty — wybierz inny.');
      } else {
        this.globalError.set(formatAuthApiError(err));
      }
    } finally {
      this.saving.set(false);
    }
  }
}
