import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { rxResource } from '@angular/core/rxjs-interop';
import { MessageService } from 'primeng/api';
import { catchError, of } from 'rxjs';
import { AppointmentsClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { GuideProgressService } from '@core/guides/guide-progress.service';
import { GuideService } from '@core/guides/guide.service';
import { GuideDef } from '@core/guides/guide.types';
import { startHereItems } from '@core/guides/start-here';
import { environment } from '@env/environment';

/**
 * „Zacznij tutaj" nad kalendarzem — link do rezerwacji i przewodniki dobrane do wyborów z kreatora.
 *
 * Zastąpiła checklistę pierwszych kroków, ale robi coś innego niż ona. Tamta sprawdzała STAN
 * konfiguracji („czy masz grafik?") i przez to dublowała kreator, a przy grafiku ad-hoc nie dawała
 * się domknąć w ogóle. Ta proponuje UMIEJĘTNOŚCI, i to takie, których wymaga wybrany sposób pracy:
 * kto planuje miesiąc samodzielnie, dostaje otwieranie dnia z kalendarza; kto prowadzi grafik
 * powtarzalny — wyjątek na jeden dzień.
 *
 * Rozróżnienie jest celowe: „kreator dodał usługi" nie znaczy „umiem dodać usługę". Dlatego pozycje
 * znikają wyłącznie po PRZEJŚCIU przewodnika (postęp w bazie, więc spójnie między urządzeniami),
 * nigdy przez wykrycie danych. Wyjątkiem jest pierwsza pozycja — link do rezerwacji — bo to nie
 * nauka, tylko jednorazowa czynność: znika, gdy salon ma pierwszą wizytę.
 *
 * Koszt: jeden lekki EXISTS. Slug leży w cache `OnboardingStateService` (wypełnia go
 * `onboardingGuard`), postęp przewodników i tak ładuje panel, a `hasTeam` podaje kalendarz z listy,
 * którą sam pobiera — karta nie dokłada zapytania o pracowników.
 */
@Component({
  selector: 'app-start-here-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (visible()) {
      @if (zwinieta()) {
        <!--
          Salon, który już przyjmuje wizyty, przychodzi na ten ekran po kalendarz — nie po naukę.
          Pełna karta zabierała 56% ekranu telefonu i spychała wizyty poniżej zgięcia, czyli
          powtarzała grzech skasowanej checklisty. Zwinięty pasek zostawia przewodniki pod ręką,
          nie zasłaniając tego, po co użytkownik tu wszedł.
        -->
        <button
          type="button"
          data-testid="start-here-collapsed"
          (click)="rozwin()"
          class="w-full flex items-center gap-3 rounded-2xl border border-amber-300/40 dark:border-amber-700/30 bg-white/65 dark:bg-surface-50/45 px-4 py-3 text-left hover:border-primary/45 transition-colors"
        >
          <span class="grid size-8 shrink-0 place-items-center rounded-xl bg-primary/12 text-primary">
            <i class="pi pi-compass text-sm" aria-hidden="true"></i>
          </span>
          <span class="flex-1 min-w-0 text-sm font-bold text-surface-900">
            Zacznij tutaj
            <span class="font-semibold text-surface-500 dark:text-surface-400">
              · {{ licznikPozycji() }}
            </span>
          </span>
          <i class="pi pi-chevron-down text-xs text-surface-400 shrink-0" aria-hidden="true"></i>
        </button>
      } @else {
      <section
        class="admin-glass-card rounded-3xl p-5 sm:p-6 border border-amber-300/40 dark:border-amber-700/30"
        data-testid="start-here-card"
      >
        <span class="admin-section-label text-primary mb-1 block">Zacznij tutaj</span>
        <h2 class="admin-h3 text-surface-900">{{ heading() }}</h2>

        @if (showBookingLink()) {
          <p class="text-sm text-surface-600 dark:text-surface-400 mt-1.5 leading-relaxed">
            Nikt się jeszcze nie zapisał. To Twój link do rezerwacji — wyślij go klientkom albo
            dodaj do social mediów.
          </p>

          <!--
            Link ZAWIJAMY (break-all), nie ucinamy: skrócony do „localhost:4378…" nie dałby się
            sprawdzić przed skopiowaniem. Pokazujemy bez protokołu — jak ekran „Gotowe" kreatora —
            ale kopiujemy i otwieramy PEŁNY adres, bo skrócony nie wklei się do Instagrama.
          -->
          <div
            class="mt-3 flex items-center gap-2 rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50/70 dark:bg-surface-50/40 px-3 py-2.5"
          >
            <span class="flex-1 min-w-0 break-all text-left text-sm font-mono" data-testid="start-here-link">
              {{ displayLink() }}
            </span>
          </div>

          <div class="mt-3 flex flex-wrap gap-2">
            <button
              type="button"
              data-testid="start-here-copy"
              (click)="copy()"
              class="inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-xs font-bold bg-primary/10 text-primary hover:bg-primary/20 transition-colors"
            >
              <i class="pi" [class.pi-copy]="!copied()" [class.pi-check]="copied()"></i>
              {{ copied() ? 'Skopiowano!' : 'Skopiuj link' }}
            </button>
            <a
              [href]="publicLink()"
              target="_blank"
              rel="noopener"
              data-testid="start-here-open"
              class="inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-xs font-bold bg-surface-200/80 dark:bg-surface-200/60 text-surface-900 hover:bg-surface-300/80 dark:hover:bg-surface-600/60 transition-colors"
            >
              <i class="pi pi-external-link"></i>
              Otwórz jak klient
            </a>
          </div>
        }

        @if (items().length > 0) {
          <ul class="mt-4 grid gap-2" data-testid="start-here-guides">
            @for (item of items(); track item.guide.id) {
              <li>
                <button
                  type="button"
                  [attr.data-testid]="'start-here-guide-' + item.guide.id"
                  [disabled]="guideRunning()"
                  (click)="run(item.guide)"
                  class="w-full flex items-center gap-3 rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 px-4 py-3 text-left hover:border-primary/45 transition-colors disabled:opacity-50"
                >
                  <span
                    class="grid size-9 shrink-0 place-items-center rounded-xl bg-primary/12 text-primary"
                  >
                    <i [class]="item.guide.icon" class="text-sm" aria-hidden="true"></i>
                  </span>
                  <span class="flex-1 min-w-0">
                    <span class="block text-sm font-bold text-surface-900">{{ item.guide.title }}</span>
                    <span class="block text-xs text-surface-500 dark:text-surface-400 leading-relaxed">
                      {{ item.reason }}
                    </span>
                  </span>
                  <i class="pi pi-play text-xs text-surface-400 shrink-0" aria-hidden="true"></i>
                </button>
              </li>
            }
          </ul>

          <a
            routerLink="/admin/guides"
            data-testid="start-here-all"
            class="mt-3 inline-flex items-center gap-1.5 text-xs font-bold text-primary hover:underline"
          >
            Wszystkie przewodniki
            <i class="pi pi-arrow-right text-[10px]"></i>
          </a>
        }
      </section>
      }
    }
  `,
})
export class StartHereCardComponent {
  private readonly appointmentsClient = inject(AppointmentsClient);
  private readonly onboardingState = inject(OnboardingStateService);
  private readonly auth = inject(AuthSessionService);
  private readonly guides = inject(GuideService);
  private readonly progress = inject(GuideProgressService);
  private readonly messages = inject(MessageService);

  /**
   * Czy w salonie jest ktoś poza właścicielką. Podaje kalendarz, bo listę pracowników i tak
   * pobiera — własne zapytanie byłoby dokładnie tym duplikatem, przez który poprzednia checklista
   * dokładała cztery żądania do montowania kalendarza.
   */
  readonly hasTeam = input<boolean>(false);

  protected readonly copied = signal(false);
  protected readonly guideRunning = this.guides.running;

  constructor() {
    void this.progress.load();
  }

  /**
   * Lekki EXISTS — „czy salon ma JAKĄKOLWIEK wizytę?". Pusty tydzień w kalendarzu nie jest
   * odpowiedzią, a `GetAppointments(range)` ma limit zakresu 366 dni. Błąd traktujemy jak „są
   * wizyty": lepiej nie pokazać linku niż zaczepiać salon pracujący od miesięcy.
   */
  private readonly hasAppointment = rxResource({
    stream: () => this.appointmentsClient.hasAnyAppointment().pipe(catchError(() => of(true))),
    defaultValue: true,
  });

  protected readonly publicLink = computed(() => {
    const slug = this.onboardingState.state()?.slug?.trim();
    if (!slug) {
      return '';
    }
    const base = environment.bookingBaseUrl?.replace(/\/+$/, '') ?? '';
    return base ? `${base}/${slug}` : '';
  });

  protected readonly displayLink = computed(() => this.publicLink().replace(/^https?:\/\//, ''));

  protected readonly showBookingLink = computed(
    () => !this.hasAppointment.value() && !!this.publicLink(),
  );

  protected readonly items = computed(() =>
    startHereItems({
      // Brak stanu onboardingu czytamy jako grafik powtarzalny — to wariant, w którym podpowiadamy
      // wyjątek na jeden dzień, więc pomyłka najwyżej podsuwa mniej pilny przewodnik.
      usesAdHocSchedule: this.onboardingState.state()?.usesAdHocSchedule ?? false,
      hasTeam: this.hasTeam(),
      role: this.auth.currentRole(),
      completedGuideIds: this.progress.completed(),
    }),
  );

  /**
   * Czy ten salon w ogóle przeszedł przez kreator.
   *
   * `Industry` ustawia WYŁĄCZNIE krok branży (`ApplyIndustryTemplateCommand`), a migracja
   * wprowadzająca kreator backfillowała istniejącym salonom tylko `onboarding_completed_at` —
   * branżę zostawiła pustą. To czyni ją deterministycznym znacznikiem „przeszedł nowy kreator",
   * bez nowego pola i bez zgadywania po dacie.
   *
   * Konsekwencja jest zamierzona: salony pracujące w aplikacji od dawna, a także zakładane przez
   * admina i demo, nie dostają podpowiedzi na start. Uczą się panelu od miesięcy — lista „Zacznij
   * tutaj" byłaby dla nich wyłącznie szumem nad kalendarzem. Przewodniki zostają im dostępne tam,
   * gdzie ich szukają: w menu Przewodniki i pod pigułką „?" na ekranie.
   *
   * Brak stanu czytamy jako „nie pokazuj" — lepiej nie mignąć kartą, niż pokazać ją na sekundę
   * komuś, kto nie powinien jej widzieć.
   */
  private readonly poNowymKreatorze = computed(
    () => this.onboardingState.state()?.hasIndustry === true,
  );

  protected readonly visible = computed(
    () => this.poNowymKreatorze() && (this.showBookingLink() || this.items().length > 0),
  );

  /** Rozwinięcie ręczne — żyje tylko w tej instancji karty. */
  private readonly rozwinietaRecznie = signal(false);

  /**
   * Zwijamy dopiero od pierwszej wizyty. Wcześniej kalendarz jest pusty, więc pełna karta nie ma
   * czego zasłaniać, a link do rezerwacji jest wtedy najważniejszą rzeczą na ekranie.
   *
   * Stanu rozwinięcia świadomie NIE utrwalamy: pasek kosztuje jedno kliknięcie, a każda flaga
   * w localStorage to ta sama pułapka, przez którą checklista wracała odhaczona na jednym
   * urządzeniu i pusta na drugim.
   */
  protected readonly zwinieta = computed(
    () => !this.showBookingLink() && !this.rozwinietaRecznie(),
  );

  /** „2 przewodniki" — lista ma najwyżej trzy pozycje, więc wystarczą dwie formy. */
  protected readonly licznikPozycji = computed(() => {
    const n = this.items().length;
    return n === 1 ? '1 przewodnik' : `${n} przewodniki`;
  });

  /**
   * Domknięcie listy. Bez tego ostatnia pozycja znikała razem z kartą bez słowa, co czyta się
   * jak usterka, a nie jak koniec.
   *
   * Warunek `isLoaded` jest konieczny: zanim postęp dojedzie z serwera, zbiór ukończonych jest
   * pusty, więc lista ma pozycje i zaraz spada do zera — bez tej bramki komunikat wyskakiwałby
   * przy każdym wejściu na kalendarz komuś, kto przeszedł już wszystko.
   */
  private byłyPozycje = false;
  private readonly _domkniecie = effect(() => {
    if (!this.progress.isLoaded()) {
      return;
    }
    const teraz = this.items().length;
    if (this.byłyPozycje && teraz === 0) {
      this.messages.add({
        severity: 'success',
        summary: 'To wszystko',
        detail: 'Przeszłaś podpowiedzi na start. Resztę przewodników znajdziesz w menu Przewodniki.',
        life: 6000,
      });
    }
    this.byłyPozycje = teraz > 0;
  });

  protected rozwin(): void {
    this.rozwinietaRecznie.set(true);
  }

  /** Nagłówek mówi, czego dotyczy karta w tym konkretnym momencie. */
  protected readonly heading = computed(() => {
    if (this.showBookingLink()) {
      return 'Roześlij link i naucz się panelu';
    }
    return this.items().length === 1 ? 'Została jedna rzecz' : 'Kilka rzeczy warto umieć';
  });

  protected run(guide: GuideDef): void {
    void this.guides.start(guide);
  }

  protected async copy(): Promise<void> {
    const link = this.publicLink();
    if (!link) {
      return;
    }
    try {
      await navigator.clipboard.writeText(link);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      /* clipboard niedostępny w starszych przeglądarkach */
    }
  }
}
