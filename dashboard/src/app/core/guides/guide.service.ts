import { Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { filter } from 'rxjs';
import type { Config, DriveStep, Driver, Popover } from 'driver.js';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { GuideProgressService } from './guide-progress.service';
import { GUIDE_ME_TOKEN, GuideDef, GuideStep } from './guide.types';
import { resolvePopoverTop } from './popover-placement';
import { pickVisibleElement, waitForElement } from './wait-for-element';

/** Ile czekamy, aż router sam wyląduje na docelowej trasie, zanim nawigujemy siłą. */
const ROUTE_SETTLE_MS = 600;

/**
 * Silnik przewodników zadaniowych (driver.js, MIT).
 *
 * W odróżnieniu od poprzedniej implementacji — która tylko opowiadała o ekranie i przesuwała
 * się przyciskiem „Dalej" — ten silnik potrafi CZEKAĆ, aż użytkownik faktycznie wykona
 * zadanie (krok `action`). Dzięki temu „Ustawmy grafik powtarzalny" kończy się zapisanym
 * grafikiem, a nie samą wiedzą, gdzie ten grafik się ustawia.
 *
 * driver.js ładowany jest lazy (`await import`), żeby nie powiększać initial bundle.
 * CSS jest importowany globalnie w `angular.json`, bo driver dopina overlay do `<body>`,
 * a nie do scoped DOM komponentu.
 */
@Injectable({ providedIn: 'root' })
export class GuideService {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthSessionService);
  private readonly progress = inject(GuideProgressService);
  private readonly messages = inject(MessageService);

  /** Czy jakikolwiek przewodnik aktualnie się odgrywa (do blokowania przycisków). */
  readonly running = signal(false);

  private driverObj: Driver | null = null;
  private steps: GuideStep[] = [];
  private teardownStep: (() => void) | null = null;
  private advancing = false;
  private reachedEnd = false;

  /**
   * Uruchamia przewodnik. Promise rozwiązuje się dopiero, gdy przewodnik zostanie zamknięty
   * (ukończony, przerwany krzyżykiem lub Escape) — wołający może wtedy odświeżyć swój stan.
   */
  async start(def: GuideDef): Promise<void> {
    if (this.running()) return;

    const role = this.auth.currentRole();
    if (!role || !def.roles.includes(role)) {
      this.warn('Ten przewodnik nie dotyczy Twojej roli.');
      return;
    }

    // Trasy przewodników dostępności celują w profil ZALOGOWANEGO pracownika. Bez tego id
    // nawigowalibyśmy pod `/admin/my-availability/null` i przewodnik umarłby w połowie.
    if (this.needsEmployeeId(def) && !this.auth.currentEmployeeId()) {
      this.warn('Twoje konto nie jest powiązane z profilem pracownika, więc nie mogę poprowadzić tego przewodnika.');
      return;
    }

    const prepared = this.prepareSteps(def);
    if (prepared.length === 0) {
      this.warn('Nie mogę teraz uruchomić tego przewodnika.');
      return;
    }

    this.steps = prepared;
    this.reachedEnd = false;
    this.running.set(true);

    const { driver } = await import('driver.js');

    return new Promise<void>((resolve) => {
      const config: Config = {
        showProgress: true,
        animate: true,
        smoothScroll: true,
        stagePadding: 8,
        stageRadius: 16,
        popoverClass: 'zapisz-tour',
        overlayColor: '#0f172a',
        overlayOpacity: 0.55,
        progressText: 'Krok {{current}} z {{total}}',
        nextBtnText: 'Dalej →',
        prevBtnText: '← Wstecz',
        doneBtnText: 'Gotowe',
        // Kliknięcie w przyciemnione tło NIE zamyka przewodnika. W krokach zadaniowych
        // użytkownik celuje w prawdziwe kontrolki i chybienie o kilka pikseli nie może
        // kasować całego postępu.
        overlayClickBehavior: () => undefined,
        steps: this.steps.map((step, index) => this.toDriveStep(step, index)),
        onNextClick: () => {
          void this.advanceTo((this.driverObj?.getActiveIndex() ?? 0) + 1);
        },
        onPrevClick: () => {
          void this.advanceTo((this.driverObj?.getActiveIndex() ?? 0) - 1);
        },
        onDestroyed: () => {
          // „Ukończono" tylko wtedy, gdy użytkownik doszedł do końca. Przewodnik porzucony
          // w połowie zadania nie może odhaczyć kafelka w katalogu — to byłoby kłamstwo
          // o stanie salonu, a katalog jest właśnie po to, by dało się do niego wrócić.
          const finished = this.reachedEnd;
          this.cleanup();
          if (finished) void this.progress.markCompleted(def.id);
          resolve();
        },
      };

      this.driverObj = driver(config);

      void this.ensureStepReady(this.steps[0]).then(() => this.driverObj?.drive());
    });
  }

  /** Przerywa trwający przewodnik (np. gdy użytkownik odszedł na inny ekran). */
  abort(reason?: string): void {
    if (!this.running()) return;
    if (reason) this.warn(reason);
    this.driverObj?.destroy();
  }

  // ————————————————————————————————————————————————————————————————
  // Budowa kroków

  /**
   * Rozstrzyga, które kroki w ogóle wejdą do przewodnika.
   *
   * Pominięcie kroku wolno rozważać TYLKO dla kroku, którego ekran jest już na wierzchu —
   * inaczej wycięlibyśmy wszystko, co czeka na dalszych stronach. Krok bez własnego `route`
   * dziedziczy stronę po poprzedniku, więc „ekran kroku" liczymy narastająco.
   *
   * Zasady dla kroku, którego ekran jest bieżący: `explain`/`outro` bez widocznej kotwicy
   * odpada (albo zostaje bez highlightu, gdy ma `keepWhenMissing`). Krok `action` zostaje
   * ZAWSZE — ciche pomijanie zadań byłoby najgorszym z możliwych zachowań, bo przewodnik
   * „Dodajmy usługę" milcząco przestałby dodawać usługę.
   */
  private prepareSteps(def: GuideDef): GuideStep[] {
    const here = this.currentUrl();
    let effectiveRoute: string | null = null;

    return def.steps.filter((step) => {
      if (step.route) effectiveRoute = this.resolveRoute(step.route);

      if (this.isAction(step)) return true;
      if (!step.element) return true;

      // Krok mieszka na innym ekranie — o jego kotwicy rozstrzygniemy dopiero na miejscu.
      if (effectiveRoute !== null && effectiveRoute !== here) return true;

      return pickVisibleElement(step.element) !== null || step.keepWhenMissing === true;
    });
  }

  private toDriveStep(step: GuideStep, index: number): DriveStep {
    const popover: Popover = {
      title: step.popover.title,
      description: step.popover.description,
      side: step.popover.side,
      align: step.popover.align,
    };

    if (this.isAction(step)) {
      // Sedno kroku zadaniowego: nie ma czym „przeklikać" go dalej. Zostaje krzyżyk,
      // żeby dało się wyjść, ale posuwa go wyłącznie realne wykonanie zadania.
      popover.showButtons = ['close'];
    }

    return {
      // Kroki z `route` rozwiązujemy dopiero po nawigacji — stąd selektor, nie element.
      element: step.element,
      popover,
      onHighlighted: () => this.onStepShown(index),
    };
  }

  // ————————————————————————————————————————————————————————————————
  // Przesuwanie kroków

  /** Wywoływane przez driver.js po pokazaniu kroku — tu uzbrajamy nasłuch zadania. */
  private onStepShown(index: number): void {
    this.teardownCurrentStep();

    const step = this.steps[index];
    if (!step) return;

    this.keepPopoverOffAnchor(step);

    if (!this.isAction(step)) return;

    if (step.element && !pickVisibleElement(step.element)) {
      // Kotwica zadania zniknęła z UI — przewodnik nie ma jak poprowadzić dalej. Mówimy to
      // wprost, zamiast zostawiać użytkownika przed krokiem, którego nie da się wykonać.
      this.abort('Nie znalazłem elementu, na którym miał opierać się ten krok. Przewodnik wymaga aktualizacji.');
      return;
    }

    this.armAction(index, step);
  }

  /**
   * Odsuwa popover od kotwicy, jeśli driver.js położył go NA niej.
   *
   * Na wąskim ekranie popover ma ~320 px i niewiele miejsca na manewr, więc przy
   * `side: 'top'` potrafi wylądować dokładnie na przycisku, w który krok każe kliknąć —
   * a wtedy zadania nie da się wykonać: kliknięcie trafia w popover. Złapane na kroku
   * „Zapisz kartę" w „Dodajmy klientkę" przy szerokości 500 px (na desktopie było dobrze).
   *
   * Naprawiamy w silniku, a nie w definicji kroku, bo to nie jest wada tego jednego
   * przewodnika: ta sama kolizja czeka na każdą kotwicę przy dolnej krawędzi ekranu.
   * Preferujemy przeniesienie NAD kotwicę (użytkownik patrzy w górę od kciuka); jeśli
   * tam nie ma miejsca — pod nią. Gdy nie mieści się nigdzie, zostawiamy jak było:
   * przesuwanie w ciemno byłoby gorsze od kolizji, którą widać.
   */
  private keepPopoverOffAnchor(step: GuideStep): void {
    if (!step.element) return;

    const anchor = pickVisibleElement(step.element);
    const popover = document.querySelector<HTMLElement>('.driver-popover');
    if (!anchor || !popover) return;

    const top = resolvePopoverTop(
      anchor.getBoundingClientRect(),
      popover.getBoundingClientRect(),
      window.innerHeight,
    );
    if (top === null) return;

    popover.style.top = `${Math.round(top)}px`;

    // Strzałka wskazywałaby teraz w próżnię — driver.js wyliczył ją do starej pozycji.
    popover.querySelector<HTMLElement>('.driver-popover-arrow')?.style.setProperty('display', 'none');
  }

  private armAction(index: number, step: GuideStep): void {
    const trigger = step.advanceOn;
    if (!trigger) return;

    const cleanups: (() => void)[] = [];
    const fire = () => {
      this.teardownCurrentStep();
      void this.advanceTo(index + 1);
    };

    if (trigger.on === 'click') {
      const selector = trigger.selector ?? step.element;
      if (selector) {
        // Faza przechwytywania: łapiemy klik zanim komponent zdąży przerysować albo odpiąć cel.
        const handler = (event: Event) => {
          const target = event.target as Element | null;
          if (target?.closest(selector)) fire();
        };
        document.addEventListener('click', handler, true);
        cleanups.push(() => document.removeEventListener('click', handler, true));
      }
    }

    if (trigger.on === 'appear' || trigger.on === 'disappear') {
      const selector = trigger.selector;
      const wanted = trigger.on === 'appear';
      const satisfied = () => (pickVisibleElement(selector) !== null) === wanted;

      if (satisfied()) {
        // Warunek jest już spełniony w chwili pokazania kroku — nie ma na co czekać.
        fire();
      } else {
        const observer = new MutationObserver(() => {
          if (satisfied()) fire();
        });
        // `attributes` są tu istotne: szuflada bywa chowana zmianą stylu/klasy, a nie
        // usunięciem węzła — bez tego „disappear" nigdy by nie zaskoczyło.
        observer.observe(document.body, {
          childList: true,
          subtree: true,
          attributes: true,
          attributeFilter: ['class', 'style', 'hidden'],
        });
        cleanups.push(() => observer.disconnect());
      }
    }

    if (trigger.on === 'route') {
      const target = this.resolveRoute(trigger.route);
      const sub = this.router.events
        .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
        .subscribe(() => {
          if (this.currentUrl() === target) fire();
        });
      cleanups.push(() => sub.unsubscribe());
    } else {
      cleanups.push(this.watchForUserLeaving(index));
    }

    this.teardownStep = () => cleanups.forEach((c) => c());
  }

  /**
   * Krok zadaniowy czeka bezterminowo, więc gdy użytkownik odejdzie na zupełnie inny ekran,
   * przewodnik wisiałby nad obcą stroną. Przerywamy go — ale tylko wtedy, gdy wiemy, dokąd
   * krok bieżący i następny należą; bez zadeklarowanych tras nie mamy podstaw do oceny.
   */
  private watchForUserLeaving(index: number): () => void {
    const expected = [this.steps[index]?.route, this.steps[index + 1]?.route]
      .filter((r): r is string => !!r)
      .map((r) => this.resolveRoute(r));

    if (expected.length === 0) return () => undefined;

    const sub = this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(() => {
        if (!expected.includes(this.currentUrl())) {
          this.abort('Przerwałem przewodnik, bo przeszłaś na inny ekran. Możesz go wznowić z katalogu przewodników.');
        }
      });

    return () => sub.unsubscribe();
  }

  /** Przygotowuje krok o zadanym indeksie (trasa + render) i przesuwa na niego driver. */
  private async advanceTo(index: number): Promise<void> {
    if (this.advancing) return;

    const driverObj = this.driverObj;
    if (!driverObj) return;

    if (index < 0) return;
    if (index >= this.steps.length) {
      // Wyjście poza ostatni krok = użytkownik kliknął „Gotowe" albo domknął ostatnie zadanie.
      this.reachedEnd = true;
      driverObj.destroy();
      return;
    }

    this.advancing = true;
    try {
      await this.ensureStepReady(this.steps[index]);
      driverObj.moveTo(index);
    } finally {
      this.advancing = false;
    }
  }

  /**
   * Doprowadza aplikację do stanu, w którym krok da się pokazać: właściwa trasa + wyrenderowany
   * element.
   *
   * Nawigacja jest ostatecznością, nie pierwszym ruchem. Gdy krok wywołało kliknięcie
   * użytkownika w prawdziwy przycisk, aplikacja NAWIGUJE SAMA i wystarczy poczekać —
   * własne `navigateByUrl` w tym momencie ścigałoby się z routerem.
   */
  private async ensureStepReady(step: GuideStep | undefined): Promise<void> {
    if (!step) return;

    if (step.route) {
      const target = this.resolveRoute(step.route);
      if (this.currentUrl() !== target) {
        const landedOnItsOwn = await this.waitForRoute(target, ROUTE_SETTLE_MS);
        if (!landedOnItsOwn) {
          await this.router.navigateByUrl(target);
        }
      }
    }

    if (step.element) {
      await waitForElement(step.element);
      await this.scrollAnchorIntoView(step.element);
    }
  }

  /**
   * Wciąga kotwicę w pole widzenia JEJ kontenera przewijania, zanim driver.js ją podświetli.
   *
   * driver.js sprawdza tylko, czy element mieści się w oknie — a panel ma własne przewijane
   * obszary. Przycisk „DODAJ" w formularzu klientki leżał 9 px PONIŻEJ dolnej krawędzi
   * kontenera (751 vs 742): `getBoundingClientRect` zwracał sensowne współrzędne w oknie,
   * więc driver uznawał, że wszystko gra i rysował podświetlenie na pustym miejscu, a
   * przycięty przycisk nie odbierał kliknięć. Krok zadaniowy stawał się nieukończalny —
   * na telefonie, bo tylko tam formularz jest wyższy od swojego kontenera.
   *
   * `scrollIntoView` przewija wszystkie przewijalne przodki naraz, więc działa też dla
   * zagnieżdżonych paneli. Czekamy jedną klatkę, żeby driver liczył pozycję po przewinięciu.
   */
  private async scrollAnchorIntoView(selector: string): Promise<void> {
    const anchor = pickVisibleElement(selector);
    if (!anchor) return;

    anchor.scrollIntoView({ block: 'center', inline: 'nearest' });
    await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
  }

  private waitForRoute(target: string, timeoutMs: number): Promise<boolean> {
    if (this.currentUrl() === target) return Promise.resolve(true);

    return new Promise<boolean>((resolve) => {
      let settled = false;

      const finish = (landed: boolean) => {
        if (settled) return;
        settled = true;
        sub.unsubscribe();
        clearTimeout(timer);
        resolve(landed);
      };

      const sub = this.router.events
        .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
        .subscribe(() => {
          if (this.currentUrl() === target) finish(true);
        });

      const timer = setTimeout(() => finish(this.currentUrl() === target), timeoutMs);
    });
  }

  // ————————————————————————————————————————————————————————————————
  // Pomocnicze

  private isAction(step: GuideStep): boolean {
    return step.kind === 'action';
  }

  private needsEmployeeId(def: GuideDef): boolean {
    const routes = [def.entryRoute, ...def.steps.map((s) => s.route ?? '')];
    const triggers = def.steps.map((s) =>
      s.advanceOn?.on === 'route' ? s.advanceOn.route : '');
    return [...routes, ...triggers].some((r) => r.includes(GUIDE_ME_TOKEN));
  }

  /** Podmienia token `:me` na id pracownika zalogowanego użytkownika. */
  resolveRoute(route: string): string {
    const employeeId = this.auth.currentEmployeeId();
    return employeeId ? route.replace(GUIDE_ME_TOKEN, employeeId) : route;
  }

  private currentUrl(): string {
    return this.router.url.split('?')[0].split('#')[0];
  }

  private teardownCurrentStep(): void {
    this.teardownStep?.();
    this.teardownStep = null;
  }

  private cleanup(): void {
    this.teardownCurrentStep();
    this.driverObj = null;
    this.steps = [];
    this.advancing = false;
    this.reachedEnd = false;
    this.running.set(false);
  }

  private warn(detail: string): void {
    this.messages.add({ severity: 'info', summary: 'Przewodnik', detail, life: 5000 });
  }
}
