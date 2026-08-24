import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { environment } from '@env/environment';
import { clearOnboardingPending } from '@core/auth/onboarding-pending';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

/** Ekran „Gotowe" — publiczny link do rezerwacji, kopiowanie, podgląd jak klient, wejście do panelu. */
@Component({
  selector: 'app-onboarding-done-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-4 py-8 sm:px-6 sm:py-10">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>

      <div class="admin-glass-card relative z-10 w-full max-w-lg rounded-4xl p-6 sm:p-8 space-y-6 text-center">
        <div class="mx-auto grid size-14 place-items-center rounded-full bg-emerald-500/15 text-emerald-600 dark:text-emerald-300">
          <i class="pi pi-check text-2xl"></i>
        </div>
        <div class="space-y-1.5">
          <h1 class="text-2xl sm:text-3xl font-black tracking-tight">Salon gotowy!</h1>
          <p class="text-surface-600 dark:text-surface-300 text-sm leading-relaxed">
            To Twój publiczny link do rezerwacji. Wyślij go klientom albo dodaj do social mediów.
          </p>
        </div>

        @if (publicLink()) {
          <!--
            Link ma być CZYTELNY: flex-1 z truncate ucinało go do „http://localhost:4359…", więc
            nie dało się sprawdzić, co się kopiuje — a to najważniejsza rzecz na tym ekranie.
            Zawijamy (break-all) zamiast ucinać i pokazujemy bez protokołu, tak jak prefiks na kroku
            „Nazwa salonu i link". Kopiowanie i „Otwórz jak klient" biorą PEŁNY adres z publicLink().
          -->
          <div class="flex items-center gap-2 rounded-xl border border-surface-200 dark:border-surface-200 bg-surface-50/70 dark:bg-surface-50/40 px-3 py-2.5">
            <span class="flex-1 min-w-0 break-all text-left text-sm font-mono" data-testid="setup-done-link">{{ displayLink() }}</span>
            <button
              type="button"
              data-testid="setup-done-copy"
              (click)="copy()"
              class="inline-flex items-center gap-1.5 rounded-lg bg-primary/10 text-primary px-3 py-1.5 text-xs font-bold hover:bg-primary/20 transition-colors shrink-0"
            >
              <i class="pi" [class.pi-copy]="!copied()" [class.pi-check]="copied()"></i>
              {{ copied() ? 'Skopiowano' : 'Kopiuj' }}
            </button>
          </div>

          <a
            [href]="publicLink()"
            target="_blank"
            rel="noopener"
            data-testid="setup-done-open"
            class="inline-flex items-center justify-center gap-2 text-sm font-semibold text-primary hover:underline"
          >
            <i class="pi pi-external-link"></i>
            Otwórz jak klient
          </a>
        }

        <button
          type="button"
          data-testid="setup-done-cta"
          (click)="goToPanel()"
          class="w-full px-8 py-3 rounded-xl bg-primary text-primary-contrast font-semibold shadow-lg hover:opacity-95 transition-opacity"
        >
          Przejdź do kalendarza
        </button>

        <!--
          „Co system zrobił beze mnie" — pytanie, które właścicielka ma DOKŁADNIE TERAZ, w chwili
          „to co, już?". Wcześniej ten akapit wisiał na kalendarzu w checklistcie pierwszych kroków,
          czyli tydzień za późno i w miejscu, gdzie sąsiadował z listą rzeczy zrobionych już
          w kreatorze. Zwinięty, bo to odpowiedź na wątpliwość, a nie zadanie do wykonania.
        -->
        <details
          class="group rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-50/60 dark:bg-surface-50/40 px-4 py-2.5 text-left"
          data-testid="setup-done-preconfigured"
        >
          <summary
            class="cursor-pointer list-none flex items-center gap-2 text-xs font-bold text-surface-600 dark:text-surface-300 select-none"
          >
            <i class="pi pi-info-circle"></i>
            <span class="flex-1">Już za Ciebie ustawione</span>
            <i class="pi pi-chevron-down text-[10px] transition-transform group-open:rotate-180"></i>
          </summary>
          <div
            class="mt-3 pt-3 border-t border-surface-200/70 dark:border-surface-200/70 text-xs text-surface-600 dark:text-surface-400 leading-relaxed"
          >
            Poza wyborami z kreatora system założył: konto pracownika powiązane z Twoim loginem,
            krok rezerwacji co 15 minut, polskie stawki VAT, 14-dniowy okres próbny oraz domyślne
            powiadomienia e-mail. Wszystko zmienisz później w Ustawieniach salonu.
          </div>
        </details>
      </div>
    </div>
  `,
})
export class OnboardingDoneStepComponent implements OnInit {
  private readonly onboardingState = inject(OnboardingStateService);
  private readonly store = inject(OnboardingWizardStore);
  private readonly auth = inject(AuthSessionService);
  private readonly router = inject(Router);

  private readonly slug = signal<string | null>(null);
  protected readonly copied = signal(false);

  protected readonly publicLink = computed(() => {
    const slug = this.slug()?.trim();
    if (!slug) {
      return '';
    }
    const base = (environment as { bookingBaseUrl?: string }).bookingBaseUrl ?? '';
    return base ? `${base}/${slug}` : '';
  });

  /**
   * Wersja do POKAZANIA — bez protokołu, tak jak prefiks na kroku „Nazwa salonu i link".
   * Kopiowanie i „Otwórz jak klient" używają `publicLink()`, czyli pełnego adresu: skrócony jest
   * tylko czytelniejszy, ale nie kliknąłby się z maila ani nie wkleił do Instagrama.
   */
  protected readonly displayLink = computed(() => this.publicLink().replace(/^https?:\/\//, ''));

  ngOnInit(): void {
    // Po ukończeniu czyścimy banner „dokończ rejestrację" i lokalne drafty kreatora.
    clearOnboardingPending();
    this.store.clearDrafts();

    // Świeży stan (slug) z backendu — complete() ustawił onboardingCompleted; refresh omija cache.
    this.onboardingState.refresh().subscribe((state) => {
      this.slug.set(state?.slug ?? null);
    });

    // Sesja została zhydratowana przy loginie — ZANIM CompleteProfile utworzył Tenant+Employee,
    // więc w kliencie employeeId/tenantId są jeszcze puste. Wymuszamy re-fetch /me, żeby panel
    // (i checklist „Pierwsze kroki") widział świeży kontekst salonu zaraz po wejściu.
    this.auth.refreshSession().subscribe();
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

  protected goToPanel(): void {
    void this.router.navigate(['/admin/schedule'], { replaceUrl: true });
  }
}
