import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Select } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { lastValueFrom } from 'rxjs';
import { DayOfWeek, OnboardingClient, SlotGenerationMode } from '@core/api/api-client';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { formatAuthApiError } from '@core/errors/api-error-messages';
import { WizardShellComponent } from '../ui/wizard-shell.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

interface DayToggle {
  value: DayOfWeek;
  label: string;
}

/**
 * Krok „Jak układasz grafik?" — najpierw pytanie o powtarzalność, potem (warunkowo) dni i godziny.
 *
 * Pytanie jest postawione MIESIĄCEM, nie tygodniem („powtarza się przez cały miesiąc" / „układam
 * każdy miesiąc osobno"), bo tak myśli o swojej pracy stylistka: tydzień jest jednostką techniczną
 * grafiku, ale planuje się miesiąc. Druga karta mówi też, co robi właścicielka, a nie czego jej
 * BRAKUJE — poprzednie „Nie masz stałego rytmu" opisywało świadomy sposób pracy jako niedostatek,
 * choć silnik obsługuje go w pełni (urlop → wyjątek → grafik → niedostępny, więc dzień specjalny
 * działa bez grafiku tygodniowego).
 *
 * Dwie ścieżki:
 *  - Powtarzalny tydzień → presety dni + godziny; w trybie stałym dodatkowo wspólne godziny startu.
 *  - „Planuję każdy miesiąc osobno" (papierowy kalendarz) → formularz się zwija, bo nie ma czego
 *    ustawiać. Dostępność powstaje później, dzień po dniu, przez dni specjalne.
 *
 * Pytanie stoi NAD swoim skutkiem, na jednym ekranie: klikasz „powtarzalny" → formularz się rozwija,
 * klikasz „każdy miesiąc osobno" → znika. Dlatego nie jest osobnym krokiem i licznik kreatora nie
 * musi być warunkowy.
 *
 * Wybór trybu wyznaczania terminów (Elastyczny/Stałe) NIE jest tu powtarzany — to poprzedni krok
 * i dotyczy obu ścieżek: `Employee.SlotGenerationMode` jest podpowiedzią startową dla każdego
 * nowego dnia specjalnego, a tryb i tak rozstrzyga się per dzień.
 */
@Component({
  selector: 'app-onboarding-schedule-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WizardShellComponent, FormsModule, Select, SelectButtonModule],
  template: `
    <app-wizard-shell
      step="schedule"
      title="Jak układasz grafik?"
      nextLabel="Zakończ konfigurację"
      [nextDisabled]="!canSubmit()"
      [nextPending]="saving()"
      [error]="globalError()"
      (next)="onNext()"
    >
      <div class="grid gap-5">
        <!-- Pytanie o powtarzalność — steruje wszystkim poniżej -->
        <div class="grid gap-3 sm:grid-cols-2">
          <button
            type="button"
            data-testid="setup-schedule-recurring"
            (click)="setAdHoc(false)"
            class="flex flex-col gap-1 rounded-2xl border px-4 py-3.5 text-left transition-colors"
            [class.border-primary]="!usesAdHoc()"
            [class.ring-2]="!usesAdHoc()"
            [class.ring-primary]="!usesAdHoc()"
            [class.border-surface-200]="usesAdHoc()"
            [class.hover:border-primary]="usesAdHoc()"
          >
            <span class="text-sm font-bold">Powtarzam ten sam tydzień</span>
            <span class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed">
              Ustawiasz godziny na wybrane dni tygodnia raz, a one wracają w każdym kolejnym
              tygodniu miesiąca. Pojedynczy dzień i tak zmienisz później.
            </span>
          </button>

          <button
            type="button"
            data-testid="setup-schedule-adhoc"
            (click)="setAdHoc(true)"
            class="flex flex-col gap-1 rounded-2xl border px-4 py-3.5 text-left transition-colors"
            [class.border-primary]="usesAdHoc()"
            [class.ring-2]="usesAdHoc()"
            [class.ring-primary]="usesAdHoc()"
            [class.border-surface-200]="!usesAdHoc()"
            [class.hover:border-primary]="!usesAdHoc()"
          >
            <span class="text-sm font-bold">Planuję każdy miesiąc osobno</span>
            <span class="text-xs text-surface-500 dark:text-surface-400 leading-relaxed">
              Sama układasz, w które dni i w jakich godzinach przyjmujesz. Nic się nie powtarza —
              otwarte są tylko te dni, które wpiszesz.
            </span>
          </button>
        </div>

        <!--
          Wskazujemy ścieżkę z KALENDARZA, nie z huba dostępności: dodawanie dni „na bieżąco" robi
          się tam, gdzie i tak patrzysz na swój tydzień. Cytujemy dokładną etykietę przycisku
          („Ustaw godziny na ten dzień" — visit-schedule.component.ts), żeby dało się go znaleźć.
          Drugie zdanie jest kluczowe: kalendarz startuje PUSTY i bez niego wygląda na zepsuty.
        -->
        @if (usesAdHoc()) {
          <div
            class="rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-50/60 dark:bg-surface-50/40 px-4 py-4 text-sm text-surface-600 dark:text-surface-300 leading-relaxed"
          >
            Miesiąc układasz bezpośrednio w kalendarzu — wybierasz konkretny dzień i klikasz
            <strong>„Ustaw godziny na ten dzień"</strong>. Domyślnie nie pracujesz, więc klientki
            zobaczą wyłącznie te dni, które sama otworzysz.
          </div>
        }
      </div>

      @if (!usesAdHoc()) {
      <div class="grid gap-5 mt-5">
        <!-- Presety -->
        <div class="flex flex-wrap gap-2">
          <button
            type="button"
            data-testid="setup-preset-weekdays"
            (click)="applyPreset('weekdays')"
            class="rounded-full border border-surface-300 dark:border-surface-200 px-4 py-1.5 text-xs font-semibold hover:border-primary transition-colors"
          >
            Pn–Pt 9–17
          </button>
          <button
            type="button"
            data-testid="setup-preset-sat"
            (click)="applyPreset('withSat')"
            class="rounded-full border border-surface-300 dark:border-surface-200 px-4 py-1.5 text-xs font-semibold hover:border-primary transition-colors"
          >
            Pn–Sob 9–17
          </button>
        </div>

        <!-- Dni -->
        <div class="flex flex-col gap-2">
          <span class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
            Dni pracy
          </span>
          <div class="flex flex-wrap gap-2">
            @for (day of dayToggles; track day.value) {
              <button
                type="button"
                [attr.data-testid]="'setup-day-' + day.value"
                (click)="toggleDay(day.value)"
                class="grid size-10 place-items-center rounded-full border text-xs font-bold transition-colors"
                [class.bg-primary]="isSelected(day.value)"
                [class.text-primary-contrast]="isSelected(day.value)"
                [class.border-primary]="isSelected(day.value)"
                [class.border-surface-300]="!isSelected(day.value)"
                [class.dark:border-surface-200]="!isSelected(day.value)"
              >
                {{ day.label }}
              </button>
            }
          </div>
        </div>

        <!--
          Godziny TYLKO w trybie siatki. W trybie stałych godzin dzień nie ma okna pracy — liczą się
          wyłącznie godziny startu poniżej. Wcześniej Od/Do wisiało tu zawsze: front blokował „Dalej"
          do czasu wybrania poprawnego zakresu, a backend go wyrzucał (puste WorkRanges).
        -->
        @if (!isFixed()) {
          <!--
            Wybór trybu godzin jako p-selectbutton, a nie tekstowy link „Różne godziny w różne dni":
            link wyglądał jak podpis i ukrywał samą MOŻLIWOŚĆ — trzeba go było przeczytać, żeby
            wiedzieć, że da się inaczej. Selectbutton pokazuje obie opcje naraz. Ten sam wzorzec
            rozwiązuje identyczny problem na ekranie dni specjalnych (special-day-mode).
          -->
          <div class="flex flex-col gap-2">
            <span class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
              Godziny
            </span>
            <p-selectbutton
              data-testid="setup-hours-mode"
              [options]="hoursModeOptions"
              [ngModel]="perDayHours()"
              (ngModelChange)="setPerDayHours($event)"
              optionLabel="label"
              optionValue="value"
              [allowEmpty]="false"
              ariaLabel="Sposób ustalania godzin pracy"
            />
          </div>

          @if (!perDayHours()) {
            <!-- Wspólne godziny — domyślnie, żeby preset dalej był jednym kliknięciem. -->
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="flex flex-col gap-2">
                <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                  Od
                </label>
                <p-select
                  [options]="timeOptions"
                  optionLabel="label"
                  optionValue="value"
                  [(ngModel)]="startTimeModel"
                  appendTo="body"
                  styleClass="w-full"
                  data-testid="setup-start-time"
                />
              </div>
              <div class="flex flex-col gap-2">
                <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                  Do
                </label>
                <p-select
                  [options]="timeOptions"
                  optionLabel="label"
                  optionValue="value"
                  [(ngModel)]="endTimeModel"
                  appendTo="body"
                  styleClass="w-full"
                  data-testid="setup-end-time"
                />
              </div>
            </div>

            @if (!sharedRangeValid()) {
              <p class="text-xs text-red-600 dark:text-red-300 px-1">
                Godzina zakończenia musi być późniejsza niż rozpoczęcia.
              </p>
            }
          } @else {
            <!-- Wiersz na każdy zaznaczony dzień: Pn 9–17, Wt 10–18 to normalny grafik. -->
            <div class="flex flex-col gap-2">
              @for (day of selectedDayToggles(); track day.value) {
                <div class="flex items-center gap-2">
                  <span class="w-9 shrink-0 text-xs font-bold text-surface-700 dark:text-surface-300">
                    {{ day.label }}
                  </span>
                  <p-select
                    [options]="timeOptions"
                    optionLabel="label"
                    optionValue="value"
                    [ngModel]="hoursFor(day.value).start"
                    (ngModelChange)="setDayStart(day.value, $event)"
                    appendTo="body"
                    styleClass="flex-1"
                    [attr.data-testid]="'setup-day-start-' + day.value"
                  />
                  <span class="text-xs text-surface-400">–</span>
                  <p-select
                    [options]="timeOptions"
                    optionLabel="label"
                    optionValue="value"
                    [ngModel]="hoursFor(day.value).end"
                    (ngModelChange)="setDayEnd(day.value, $event)"
                    appendTo="body"
                    styleClass="flex-1"
                    [attr.data-testid]="'setup-day-end-' + day.value"
                  />
                </div>
              }
            </div>

            @if (!perDayRangesValid()) {
              <p class="text-xs text-red-600 dark:text-red-300 px-1">
                W każdym dniu godzina zakończenia musi być późniejsza niż rozpoczęcia.
              </p>
            }
          }

        }

        <!-- Stałe godziny startu (tylko tryb FixedStartTimes) -->
        @if (isFixed()) {
          <div class="flex flex-col gap-2 rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-50/60 dark:bg-surface-50/40 px-4 py-3">
            <span class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider">
              Godziny startu wizyt
            </span>
            <p class="text-xs text-surface-500 dark:text-surface-400">
              Klient wybierze wizytę tylko o tych godzinach (wspólne dla wszystkich dni).
            </p>
            <div class="flex items-center gap-2">
              <p-select
                [options]="timeOptions"
                optionLabel="label"
                optionValue="value"
                [(ngModel)]="fixedTimeToAdd"
                appendTo="body"
                styleClass="w-40"
                placeholder="Wybierz"
                data-testid="setup-fixed-add-select"
              />
              <button
                type="button"
                data-testid="setup-fixed-add"
                (click)="addFixedTime()"
                class="rounded-xl bg-primary/10 text-primary px-4 py-2 text-sm font-bold hover:bg-primary/20 transition-colors"
              >
                Dodaj
              </button>
            </div>
            @if (fixedTimes().length > 0) {
              <div class="flex flex-wrap gap-2 pt-1">
                @for (t of fixedTimes(); track t) {
                  <span class="inline-flex items-center gap-1.5 rounded-full bg-primary/10 text-primary px-3 py-1 text-xs font-bold">
                    {{ t }}
                    <button type="button" (click)="removeFixedTime(t)" aria-label="Usuń" class="hover:opacity-70">✕</button>
                  </span>
                }
              </div>
            } @else {
              <p class="text-xs text-red-600 dark:text-red-300">Dodaj co najmniej jedną godzinę startu.</p>
            }
          </div>
        }
      </div>
      }
    </app-wizard-shell>
  `,
})
export class OnboardingScheduleStepComponent {
  private readonly store = inject(OnboardingWizardStore);
  private readonly client = inject(OnboardingClient);
  private readonly onboardingState = inject(OnboardingStateService);
  private readonly router = inject(Router);

  protected readonly dayToggles: DayToggle[] = [
    { value: DayOfWeek.Monday, label: 'Pn' },
    { value: DayOfWeek.Tuesday, label: 'Wt' },
    { value: DayOfWeek.Wednesday, label: 'Śr' },
    { value: DayOfWeek.Thursday, label: 'Cz' },
    { value: DayOfWeek.Friday, label: 'Pt' },
    { value: DayOfWeek.Saturday, label: 'So' },
    { value: DayOfWeek.Sunday, label: 'Nd' },
  ];

  protected readonly timeOptions = OnboardingScheduleStepComponent.buildTimeOptions();

  protected readonly selectedDays = signal<DayOfWeek[]>([
    DayOfWeek.Monday,
    DayOfWeek.Tuesday,
    DayOfWeek.Wednesday,
    DayOfWeek.Thursday,
    DayOfWeek.Friday,
  ]);
  // Sygnały, nie zwykłe pola: `timeRangeValid` to `computed()` nad nimi, a computed bez
  // producentów-sygnałów policzyłby się raz i zamroził wynik na `true` — walidacja zakresu
  // byłaby martwym kodem i odwrócone godziny szłyby do backendu.
  protected readonly startTimeModel = signal('09:00');
  protected readonly endTimeModel = signal('17:00');
  protected readonly fixedTimes = signal<string[]>([]);
  protected fixedTimeToAdd: string | null = null;

  protected readonly saving = signal(false);
  protected readonly globalError = signal('');

  /**
   * „Papierowy kalendarz" — brak grafiku powtarzalnego. Wybór żyje tylko na tym ekranie: jest tu
   * używany (zwija formularz) i stąd wysyłany, więc nie musi przechodzić przez store.
   * Po F5 odtwarzamy go z backendu (`state.usesAdHocSchedule`) w konstruktorze.
   */
  protected readonly usesAdHoc = signal(false);

  /**
   * Godziny per dzień (Pn 9–17, Wt 10–18). Domyślnie wyłączone — wspólny zakres trzyma preset
   * w jednym kliknięciu. Wcześniej kreator w ogóle tego nie umiał: jeden zakres szedł do WSZYSTKICH
   * dni, więc właścicielka o różnych godzinach kończyła konfigurację z BŁĘDNYM grafikiem, po którym
   * klientka rezerwowała wtorek na 9:00, choć praca zaczyna się o 10.
   */
  protected readonly perDayHours = signal(false);

  /** Nadpisania per dzień; dni bez wpisu lecą na wspólnym zakresie. */
  private readonly dayHours = signal<Record<number, { start: string; end: string }>>({});

  protected readonly isFixed = computed(
    () => this.store.slotMode() === SlotGenerationMode.FixedStartTimes,
  );

  /** Dni w kolejności tygodnia — wiersze godzin mają iść Pn→Nd, nie w kolejności klikania. */
  protected readonly selectedDayToggles = computed(() =>
    this.dayToggles.filter((d) => this.selectedDays().includes(d.value)),
  );

  protected readonly sharedRangeValid = computed(
    () => this.startTimeModel() < this.endTimeModel(),
  );

  protected readonly perDayRangesValid = computed(() =>
    this.selectedDays().every((d) => {
      const h = this.hoursFor(d);
      return h.start < h.end;
    }),
  );

  protected readonly canSubmit = computed(() => {
    // Na bieżąco: nie ma dni ani godzin do wypełnienia, więc nic nie blokuje „Dalej".
    if (this.usesAdHoc()) {
      return true;
    }
    if (this.selectedDays().length === 0) {
      return false;
    }
    // Tryb stały: dzień nie ma okna pracy — zakres Od/Do NIE jest tu żadnym warunkiem
    // (backend go i tak wyrzuca). Liczą się wyłącznie godziny startu.
    if (this.isFixed()) {
      return this.fixedTimes().length > 0;
    }
    return this.perDayHours() ? this.perDayRangesValid() : this.sharedRangeValid();
  });

  constructor() {
    // Po F5 (albo wejściu z innego urządzenia) wybór mógł już trafić na backend — bez tego karta
    // „na bieżąco" wyglądałaby na niewybraną mimo zapisanej deklaracji, a formularz wróciłby
    // z domyślnym Pn–Pt 9–17, czyli dokładnie tym, czego właścicielka nie chce.
    this.onboardingState
      .ensure()
      .pipe(takeUntilDestroyed())
      .subscribe((state) => {
        if (state?.usesAdHocSchedule) {
          this.usesAdHoc.set(true);
        }
      });
  }

  protected setAdHoc(value: boolean): void {
    this.usesAdHoc.set(value);
    this.globalError.set('');
  }

  /** Godziny danego dnia — własne, albo wspólny zakres, jeśli dzień nie był ruszany. */
  protected hoursFor(day: DayOfWeek): { start: string; end: string } {
    return (
      this.dayHours()[day] ?? { start: this.startTimeModel(), end: this.endTimeModel() }
    );
  }

  protected readonly hoursModeOptions = [
    { label: 'Te same co dzień', value: false },
    { label: 'Różne w różne dni', value: true },
  ];

  protected setPerDayHours(next: boolean): void {
    if (next === this.perDayHours()) {
      return;
    }
    if (next) {
      // Wejście w tryb per dzień zaczyna od wspólnego zakresu — nikt nie chce wypełniać
      // siedmiu wierszy od zera tylko po to, żeby zmienić jeden dzień.
      const seeded: Record<number, { start: string; end: string }> = {};
      for (const day of this.selectedDays()) {
        seeded[day] = this.hoursFor(day);
      }
      this.dayHours.set(seeded);
    }
    this.perDayHours.set(next);
  }

  protected setDayStart(day: DayOfWeek, value: string): void {
    this.dayHours.update((h) => ({ ...h, [day]: { ...this.hoursFor(day), start: value } }));
  }

  protected setDayEnd(day: DayOfWeek, value: string): void {
    this.dayHours.update((h) => ({ ...h, [day]: { ...this.hoursFor(day), end: value } }));
  }

  protected isSelected(day: DayOfWeek): boolean {
    return this.selectedDays().includes(day);
  }

  protected toggleDay(day: DayOfWeek): void {
    this.selectedDays.update((days) =>
      days.includes(day) ? days.filter((d) => d !== day) : [...days, day],
    );
  }

  protected applyPreset(preset: 'weekdays' | 'withSat'): void {
    const base = [
      DayOfWeek.Monday,
      DayOfWeek.Tuesday,
      DayOfWeek.Wednesday,
      DayOfWeek.Thursday,
      DayOfWeek.Friday,
    ];
    this.selectedDays.set(preset === 'withSat' ? [...base, DayOfWeek.Saturday] : base);
    this.startTimeModel.set('09:00');
    this.endTimeModel.set('17:00');
    // Preset to „te same godziny wszędzie" — czyści nadpisania per dzień, inaczej po kliknięciu
    // presetu część dni cicho trzymałaby stare, własne godziny.
    this.dayHours.set({});
    this.perDayHours.set(false);
  }

  protected addFixedTime(): void {
    const t = this.fixedTimeToAdd;
    if (!t) {
      return;
    }
    this.fixedTimes.update((list) => (list.includes(t) ? list : [...list, t].sort()));
    this.fixedTimeToAdd = null;
  }

  protected removeFixedTime(t: string): void {
    this.fixedTimes.update((list) => list.filter((x) => x !== t));
  }

  protected async onNext(): Promise<void> {
    if (!this.canSubmit() || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.globalError.set('');

    const isFixed = this.isFixed();
    const fixedStartTimes = isFixed ? this.fixedTimes().map((t) => `${t}:00`) : undefined;

    // Dni posortowane wg kolejności tygodnia, każdy z WŁASNYMI godzinami. W trybie stałym godziny
    // są null — dzień nie ma wtedy okna pracy, liczą się tylko wspólne godziny startu.
    const days = [...this.selectedDays()]
      .sort((a, b) => a - b)
      .map((day) => ({
        day,
        startTime: isFixed ? undefined : `${this.hoursFor(day).start}:00`,
        endTime: isFixed ? undefined : `${this.hoursFor(day).end}:00`,
      }));

    try {
      await lastValueFrom(
        this.client.setSchedule({
          // slotMode leci ZAWSZE — także dla „na bieżąco". To podpowiedź startowa dla każdego
          // dnia specjalnego, który właścicielka doda później, więc jej wybór z poprzedniego kroku
          // nie może przepaść tylko dlatego, że nie prowadzi grafiku powtarzalnego.
          slotMode: this.store.slotMode(),
          days,
          fixedStartTimes,
          useAdHoc: this.usesAdHoc(),
        }),
      );
      // Domknięcie onboardingu siedzi TUTAJ, bo to ostatni krok treściowy — a `complete()` zapala
      // `onboardingCompleted`, po którym `setupGuard` odsyła właścicielkę z `/setup/**` do panelu
      // (wyjątkiem jest tylko „Gotowe"). Przy okazji dowozi wybór z kroku „Zapisy", który do tej
      // pory czekał w buforze kreatora: `CompleteOnboardingCommand` ustawia tryb potwierdzania
      // i domyka kreator jedną komendą, więc tamten krok nie miał czego zapisać osobno.
      await lastValueFrom(
        this.client.complete({ confirmationMode: this.store.confirmationMode() }),
      );
      this.onboardingState.markStale();
      void this.router.navigate(['/setup/done']);
    } catch (err: unknown) {
      this.globalError.set(formatAuthApiError(err));
    } finally {
      this.saving.set(false);
    }
  }

  /** Opcje godzin 06:00–22:00 co 30 min jako `{ label, value }` dla p-select. */
  private static buildTimeOptions(): { label: string; value: string }[] {
    const opts: { label: string; value: string }[] = [];
    for (let h = 6; h <= 22; h++) {
      for (const m of [0, 30]) {
        const label = `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
        opts.push({ label, value: label });
      }
    }
    return opts;
  }
}
