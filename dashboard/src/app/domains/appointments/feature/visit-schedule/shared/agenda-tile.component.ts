import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { NgClass } from '@angular/common';

/**
 * Wspólny kafelek widoku agendy (dzień). Jednakowy dla wszystkich pozycji: początek/koniec pracy,
 * przerwa, wizyta. Neutralne tło (kolor niesie WYŁĄCZNIE pasek między godziną a treścią), stała
 * minimalna wysokość i jeden układ: [godzina start/koniec] · [pasek statusu w-1.5] · [treść (ng-content)].
 *
 * Selektor atrybutowy na `<li>` — komponent JEST elementem listy, więc `divide-y` rodzica działa,
 * a semantyka `<ul><li>` zostaje zachowana.
 */
@Component({
  selector: 'li[appAgendaTile]',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass],
  host: {
    class: 'relative flex items-center gap-3 p-3 sm:p-4 min-h-16 transition-colors focus:outline-none',
    '[class.cursor-pointer]': 'clickable()',
    '[class.hover:bg-surface-50]': 'clickable()',
    '[class.dark:hover:bg-white/5]': 'clickable()',
    '[class.focus-visible:ring-2]': 'clickable()',
    '[class.focus-visible:ring-primary/50]': 'clickable()',
    '[class.focus-visible:ring-inset]': 'clickable()',
    '[class.ring-2]': 'selected()',
    '[class.ring-primary]': 'selected()',
    '[attr.role]': "clickable() ? 'button' : null",
    '[attr.tabindex]': 'clickable() ? 0 : null',
    '[attr.aria-label]': 'ariaLabel() || null',
    '(click)': 'onActivate($event)',
    '(keydown.enter)': 'onActivate($event)',
    '(keydown.space)': 'onActivate($event)',
  },
  template: `
    <div class="w-14 shrink-0 text-center">
      <p class="text-sm font-black tabular-nums text-surface-900 leading-none">
        {{ startLabel() }}
      </p>
      @if (endLabel(); as end) {
        <p class="text-[11px] font-mono text-surface-500 dark:text-surface-400 mt-1 leading-none">{{ end }}</p>
      }
    </div>
    <div class="w-1.5 rounded-full self-stretch shrink-0" [ngClass]="barClass()"></div>
    <div class="min-w-0 flex-1">
      <ng-content />
    </div>

    <!-- Linia „Teraz" nałożona NA kafelek, gdy bieżąca godzina wypada w jego przedziale
         (jak oś czasu w kalendarzu). Sama linia + kropka — bez godziny i napisu „Teraz", żeby
         nie kolidować z treścią kafelka (te etykiety niesie osobny wiersz w luce). Proporcjonalna
         pozycja pionowa; nie przechwytuje kliknięć. -->
    @if (nowFraction() !== null) {
      <div
        class="pointer-events-none absolute inset-x-0 z-10 flex items-center gap-3 px-3 sm:px-4"
        [style.top.%]="clampedNowTop()"
        style="transform: translateY(-50%)"
        aria-hidden="true"
      >
        <span class="w-14 shrink-0"></span>
        <!-- Kropka wyśrodkowana na kolumnie paska statusu (w-1.5) i nieco większa — „siada" na pasku
             i lekko ponad niego wystaje. Linia startuje dopiero od obszaru treści. -->
        <span class="relative w-1.5 shrink-0">
          <span class="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 h-2.5 w-2.5 rounded-full bg-emerald-500"></span>
        </span>
        <span class="flex-1 h-px bg-emerald-500/70"></span>
      </div>
    }
  `,
})
export class AgendaTileComponent {
  /** Godzina wyświetlana u góry kolumny czasu (np. początek wizyty/przerwy lub granica pracy). */
  readonly startLabel = input.required<string>();
  /** Godzina pod spodem (koniec) — pomijana dla znaczników pracy (jedna godzina). */
  readonly endLabel = input<string | null>(null);
  /** Klasy tła paska statusu (`bg-*`). Jedyny nośnik koloru statusu. */
  readonly barClass = input<string>('');
  /** Czy kafelek jest interaktywny (kursor, hover, focus, rola/tabindex, emisja `activate`). */
  readonly clickable = input<boolean>(false);
  /** Podświetlenie (np. pierwsza wizyta w trybie zamiany). */
  readonly selected = input<boolean>(false);
  readonly ariaLabel = input<string | null>(null);
  /**
   * Pozycja linii „Teraz" wewnątrz kafelka jako ułamek [0,1] jego wysokości (0 = góra, 1 = dół).
   * `null` → kafelek nie zawiera bieżącej godziny i linii nie rysujemy.
   */
  readonly nowFraction = input<number | null>(null);

  readonly activate = output<Event>();

  /** Ułamek przycięty do [0,1] i wyrażony w %, żeby linia nie wychodziła poza kafelek. */
  protected clampedNowTop(): number {
    const f = this.nowFraction();
    if (f === null) return 0;
    return Math.min(1, Math.max(0, f)) * 100;
  }

  protected onActivate(event: Event): void {
    if (!this.clickable()) return;
    if (event instanceof KeyboardEvent) event.preventDefault();
    this.activate.emit(event);
  }
}
