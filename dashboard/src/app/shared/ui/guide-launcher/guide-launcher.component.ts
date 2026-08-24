import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs';
import { MenuItem } from 'primeng/api';
import { Menu } from 'primeng/menu';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { GuideService } from '@core/guides/guide.service';
import { GuideDef } from '@core/guides/guide.types';
import { guidesForRoute } from '@core/guides/guides.registry';

/**
 * Kontekstowy wyzwalacz przewodników — pigułka „?" w nagłówku strony.
 *
 * Sam ustala, co pokazać: bierze z rejestru przewodniki, które ZACZYNAJĄ SIĘ na bieżącej
 * trasie i są dostępne dla roli zalogowanego użytkownika. Strona nie musi więc nic o nich
 * wiedzieć — wystarczy, że osadzi komponent. Gdy dla ekranu nie ma żadnego przewodnika,
 * komponent nie renderuje niczego.
 *
 * Zastąpił dawny `app-tour-launcher` razem z jego trybem banera „Pierwszy raz tutaj?".
 * Banery zniknęły, bo przewodniki mają teraz własne miejsce — katalog `/admin/guides`.
 */
@Component({
  selector: 'app-guide-launcher',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Menu],
  template: `
    @if (available().length > 0) {
      <button
        type="button"
        class="shrink-0 inline-flex items-center justify-center gap-1.5 rounded-full border border-surface-300 dark:border-surface-200 bg-white/60 dark:bg-surface-50/50 px-3 py-1.5 text-xs font-bold text-surface-700 hover:border-primary/45 hover:text-primary transition-colors disabled:opacity-50"
        [disabled]="running()"
        (click)="onClick($event)"
        aria-label="Pokaż przewodnik dla tego ekranu"
        title="Przewodniki"
      >
        <i class="pi pi-question-circle"></i>
        <span class="hidden sm:inline">Przewodnik</span>
      </button>

      <p-menu #menu [popup]="true" [model]="menuItems()" [appendTo]="'body'" />
    }
  `,
})
export class GuideLauncherComponent {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthSessionService);
  private readonly guides = inject(GuideService);

  protected readonly running = this.guides.running;

  /** Bieżący URL, odświeżany po każdej nawigacji — od niego zależy zestaw przewodników. */
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(() => this.router.url),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly available = computed<GuideDef[]>(() =>
    guidesForRoute(this.url(), this.auth.currentRole()),
  );

  protected readonly menuItems = computed<MenuItem[]>(() =>
    this.available().map((guide) => ({
      label: guide.title,
      icon: guide.icon,
      command: () => void this.guides.start(guide),
    })),
  );

  /** Menu istnieje w DOM tylko gdy są przewodniki, stąd zapytanie opcjonalne. */
  private readonly menu = viewChild<Menu>('menu');

  protected onClick(event: Event): void {
    const guides = this.available();
    if (guides.length === 0) return;

    // Jeden przewodnik — startujemy od razu; kilka — użytkownik wybiera z listy.
    if (guides.length === 1) {
      void this.guides.start(guides[0]);
      return;
    }

    this.menu()?.toggle(event);
  }
}
