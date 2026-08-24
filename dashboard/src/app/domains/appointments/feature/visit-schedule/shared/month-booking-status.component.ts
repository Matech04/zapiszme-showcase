import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

export type MonthBookingState =
  /** Brak jawnej decyzji — miesiąc otwarty w granicach horyzontu rezerwacji salonu. */
  | 'default'
  /** Jawnie otwarty (data otwarcia już minęła). */
  | 'opened'
  /** Zamknięty do konkretnego dnia — otworzy się sam. */
  | 'closedUntil'
  /** Zamknięty bezterminowo — czeka na ręczne otwarcie. */
  | 'closedIndefinitely';

/**
 * Pasek stanu zapisów dla oglądanego miesiąca — mówi, czy KLIENCI mogą już rezerwować terminy
 * w tym miesiącu. Dotyczy wyłącznie rezerwacji online: personel wpisuje wizyty w kalendarzu
 * niezależnie od tego, co pokazuje ten pasek.
 *
 * Stan trzyma rodzic; komponent jest bezstanowy (wzorzec jak `app-employee-strip`).
 *
 * Ustawienie jest PER PRACOWNIK, więc pasek wolno pokazywać tylko tam, gdzie na ekranie jest
 * dokładnie jeden pracownik — czyli w widoku miesiąca zawsze, a w widoku dnia dopiero poza
 * trybem kolumn desktopowych (tam widać cały zespół i pasek mówiłby o jednej osobie z wielu).
 */
@Component({
  selector: 'app-month-booking-status',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Renderowane jako DRUGI WIERSZ karty nawigacji miesiąca (rodzic), nie jako osobne pudełko.
         Wcześniej był to samodzielny slab: w stanie zamkniętym drugi bursztynowy baner tuż pod
         „X do potwierdzenia", a w otwartym goły wiersz tekstu — jedyny element kalendarza poza
         kartą admin-glass-card. Wciągnięcie do karty wiąże status z miesiącem, którego dotyczy. -->
    @if (isClosed() || canEdit()) {
      <div
        class="mt-2 pt-2 border-t border-surface-200 dark:border-surface-700 flex items-center gap-2"
        [attr.role]="isClosed() ? 'status' : null"
        [attr.data-testid]="isClosed() ? 'month-booking-status-closed' : 'month-booking-status-open'"
      >
        <i class="shrink-0 text-sm" [class]="iconClasses()" aria-hidden="true"></i>
        <span class="flex-1 min-w-0 text-xs" [class]="labelClasses()">{{ headline() }}</span>
        @if (canEdit()) {
          <!-- Ten sam format co „Dziś" w tej samej karcie: h-11 = 44 px, czyli cel dotyku zgodny
               z wytycznymi mobile. Wcześniej był to mikro-link tekstowy. -->
          <button
            type="button"
            (click)="edit.emit()"
            class="shrink-0 h-11 px-3 rounded-xl border text-xs font-bold uppercase tracking-wider transition-colors"
            [class]="buttonClasses()"
            data-testid="month-booking-status-edit"
          >
            Zmień
          </button>
        }
      </div>
    }
  `,
})
export class MonthBookingStatusComponent {
  /** Dzień otwarcia zapisów (ISO `YYYY-MM-DD`). `null` przy istniejącym wpisie = zamknięty bezterminowo. */
  readonly opensOn = input<string | null>(null);
  /** Czy dla tego miesiąca istnieje jawna decyzja. Bez niej rządzi horyzont salonu. */
  readonly hasPublication = input<boolean>(false);
  readonly canEdit = input<boolean>(false);
  /** Wstrzykiwane w testach, żeby nie zależeć od zegara systemowego. */
  readonly today = input<Date | null>(null);

  readonly edit = output<void>();

  readonly state = computed<MonthBookingState>(() => {
    if (!this.hasPublication()) return 'default';

    const opensOn = this.opensOn();
    if (!opensOn) return 'closedIndefinitely';

    const parsed = this.parseIso(opensOn);
    if (!parsed) return 'closedIndefinitely';

    const now = this.today() ?? new Date();
    const todayMidnight = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    return parsed <= todayMidnight ? 'opened' : 'closedUntil';
  });

  /** Zamknięty = klienci nie zarezerwują; „opened"/„default" to stan normalny, wyciszony. */
  protected readonly isClosed = computed(
    () => this.state() === 'closedUntil' || this.state() === 'closedIndefinitely'
  );

  // Klasy jako wyliczane łańcuchy, a nie [class.x] — nazwy Tailwind zawierają `/` i `dark:`,
  // których składnia class-binding Angulara nie przyjmuje.
  protected readonly iconClasses = computed(() =>
    this.isClosed() ? 'pi pi-lock text-primary' : 'pi pi-lock-open text-surface-400'
  );

  protected readonly labelClasses = computed(() =>
    this.isClosed()
      ? 'font-bold text-surface-900'
      : 'font-semibold text-surface-500 dark:text-surface-400'
  );

  protected readonly buttonClasses = computed(() =>
    this.isClosed()
      ? 'border-primary/40 text-primary hover:bg-primary/10'
      : 'border-surface-300 dark:border-surface-600 text-surface-700 hover:border-primary/45'
  );

  readonly headline = computed<string>(() => {
    switch (this.state()) {
      case 'closedUntil':
        return `Zapisy zamknięte — otwarcie ${this.formattedOpensOn()}`;
      case 'closedIndefinitely':
        return 'Zapisy zamknięte dla klientów';
      default:
        return 'Zapisy otwarte dla klientów';
    }
  });

  private formattedOpensOn(): string {
    const parsed = this.parseIso(this.opensOn());
    if (!parsed) return '';
    return new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'long' }).format(parsed);
  }

  /** Parsuje `YYYY-MM-DD` jako datę LOKALNĄ — `new Date(iso)` czyta ją jako UTC i cofa o strefę. */
  private parseIso(iso: string | null): Date | null {
    if (!iso) return null;
    const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
    if (!y || !m || !d) return null;
    return new Date(y, m - 1, d);
  }
}
