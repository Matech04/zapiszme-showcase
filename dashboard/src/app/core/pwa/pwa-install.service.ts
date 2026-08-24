import { computed, DOCUMENT, inject, Injectable, isDevMode, signal } from '@angular/core';

/** Zdarzenie `beforeinstallprompt` (Chromium) — brak w lib.dom, deklarujemy minimalny kształt. */
interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

/** Gdzie w danej przeglądarce jest przycisk „Udostępnij" — na iPadzie u góry, na iPhonie na dole. */
export type SharePosition = 'top' | 'bottom';

/**
 * Wariant przewodnika iOS:
 * - `safari`  — jest Safari: pokazujemy kroki „Udostępnij → Do ekranu początkowego".
 * - `other`   — inna przeglądarka/in-app na iOS: instalacja NIE zadziała, kierujemy do Safari.
 */
export type IosGuideVariant = 'safari' | 'other';

const IOS_GUIDE_DISMISS_KEY = 'zm.iosInstallGuideDismissed';

/**
 * Steruje instalacją PWA panelu.
 *
 * - Android/Chromium: przechwytuje `beforeinstallprompt`, blokuje domyślny mini-baner i wystawia
 *   `canInstall` + `promptInstall()` do wywołania natywnego okna instalacji z własnego UI.
 * - iOS/iPadOS Safari: nie emituje `beforeinstallprompt` — instalacja jest możliwa wyłącznie ręcznie
 *   („Udostępnij → Do ekranu początkowego"), więc wystawiamy `iosInstructions` i animowany przewodnik
 *   (`iosGuideOpen`) pokazywany automatycznie (raz, z opcją „nie pokazuj ponownie") oraz na żądanie.
 * - Gdy aplikacja działa już jako zainstalowana PWA (standalone) — nic nie pokazujemy.
 */
@Injectable({ providedIn: 'root' })
export class PwaInstallService {
  private readonly window = inject(DOCUMENT).defaultView;

  private deferredPrompt: BeforeInstallPromptEvent | null = null;
  private iosGuideShownThisSession = false;
  // Sygnały (nie zwykłe pola), by zmiana z dev-hooka odświeżała widok przy zoneless change detection.
  private readonly devForcedSharePosition = signal<SharePosition | null>(null);
  private readonly devForcedVariant = signal<IosGuideVariant | null>(null);

  /** Dostępny natywny prompt instalacji (Android/Chromium). */
  readonly canInstall = signal(false);
  /** iOS/iPadOS bez zainstalowanej PWA — pokazujemy instrukcję manualną zamiast przycisku-promptu. */
  readonly iosInstructions = signal(false);
  /** Czy animowany przewodnik instalacji iOS jest otwarty. */
  readonly iosGuideOpen = signal(false);
  /** Czy w ogóle jest co pokazać w UI (przycisk instalacji albo wejście w instrukcję iOS). */
  readonly available = computed(() => this.canInstall() || this.iosInstructions());

  init(): void {
    const win = this.window;
    if (win && isDevMode()) {
      // Podgląd w dev: iOS Safari nie da się zasymulować, a przewodnik i tak nie odpali auto.
      // Udostępniamy w konsoli `__previewIosInstallGuide('top'|'bottom')`, by obejrzeć UI/animację.
      (
        win as Window & {
          __previewIosInstallGuide?: (pos?: SharePosition, variant?: IosGuideVariant) => void;
        }
      ).__previewIosInstallGuide = (pos: SharePosition = 'top', variant: IosGuideVariant = 'safari') => {
        this.devForcedSharePosition.set(pos);
        this.devForcedVariant.set(variant);
        this.iosGuideOpen.set(true);
      };
    }

    if (!win || this.isStandalone()) {
      // Brak window (SSR/testy) albo już zainstalowane — nie ma czego proponować.
      return;
    }

    win.addEventListener('beforeinstallprompt', (event: Event) => {
      event.preventDefault();
      this.deferredPrompt = event as BeforeInstallPromptEvent;
      this.canInstall.set(true);
    });

    win.addEventListener('appinstalled', () => {
      this.deferredPrompt = null;
      this.canInstall.set(false);
      this.iosInstructions.set(false);
      this.iosGuideOpen.set(false);
    });

    if (this.isIos()) {
      this.iosInstructions.set(true);
    }
  }

  /** Wywołuje natywny prompt instalacji. No-op, jeśli przeglądarka go nie udostępniła. */
  async promptInstall(): Promise<void> {
    const prompt = this.deferredPrompt;
    if (!prompt) {
      return;
    }
    // Prompt jest jednorazowy — czyścimy zanim go pokażemy, by nie dało się odpalić dwa razy.
    this.deferredPrompt = null;
    this.canInstall.set(false);
    await prompt.prompt();
    await prompt.userChoice.catch(() => undefined);
  }

  /** Otwiera przewodnik iOS (np. po kliknięciu „Zainstaluj aplikację" na iOS). */
  openIosGuide(): void {
    this.iosGuideOpen.set(true);
  }

  /** Zamyka przewodnik na tę sesję (może pojawić się ponownie następnym razem). */
  closeIosGuide(): void {
    this.iosGuideOpen.set(false);
    this.iosGuideShownThisSession = true;
  }

  /** Zamyka przewodnik i zapamiętuje, że użytkownik nie chce go już widzieć automatycznie. */
  dismissIosGuideForever(): void {
    this.iosGuideOpen.set(false);
    this.iosGuideShownThisSession = true;
    try {
      this.window?.localStorage.setItem(IOS_GUIDE_DISMISS_KEY, '1');
    } catch {
      // localStorage niedostępny (tryb prywatny) — trudno, pominięcie zapisu nie jest krytyczne.
    }
  }

  /**
   * Pokazuje przewodnik automatycznie, ale tylko na iOS bez zainstalowanej PWA, raz na sesję i o ile
   * użytkownik wcześniej nie wybrał „nie pokazuj ponownie". Wołane z uwierzytelnionego layoutu, żeby
   * nie wyskakiwał na ekranie logowania.
   */
  maybeAutoShowIosGuide(): void {
    if (!this.iosInstructions() || this.iosGuideShownThisSession || this.dismissedForever()) {
      return;
    }
    this.iosGuideShownThisSession = true;
    this.iosGuideOpen.set(true);
  }

  /** Pozycja przycisku „Udostępnij" w bieżącej przeglądarce (iPad = góra, iPhone = dół). */
  sharePosition(): SharePosition {
    return this.devForcedSharePosition() ?? (this.isIpad() ? 'top' : 'bottom');
  }

  /**
   * Wariant przewodnika. Na iOS „Dodaj do ekranu początkowego" działa TYLKO w Safari — w Chrome/
   * Firefox/Edge/in-app instalacja się nie uda, więc kierujemy użytkownika do Safari (`other`).
   */
  iosVariant(): IosGuideVariant {
    return this.devForcedVariant() ?? (this.isIosSafari() ? 'safari' : 'other');
  }

  private isIosSafari(): boolean {
    const ua = this.window?.navigator.userAgent;
    if (!ua || !this.isIos()) {
      return false;
    }
    // Inne przeglądarki/in-app na iOS mają własny token w UA (mimo silnika WebKit). Ich obecność =
    // NIE Safari → instalacja PWA niedostępna.
    const nonSafari = /(CriOS|FxiOS|EdgiOS|OPiOS|OPT\/|mercury|DuckDuckGo|FBAN|FBAV|FB_IAB|Instagram|Line|GSA|YaBrowser|Snapchat)/i;
    return /Safari/i.test(ua) && !nonSafari.test(ua);
  }

  private dismissedForever(): boolean {
    try {
      return this.window?.localStorage.getItem(IOS_GUIDE_DISMISS_KEY) === '1';
    } catch {
      return false;
    }
  }

  private isStandalone(): boolean {
    const win = this.window;
    if (!win) {
      return false;
    }
    const nav = win.navigator as Navigator & { standalone?: boolean };
    return win.matchMedia?.('(display-mode: standalone)').matches === true || nav.standalone === true;
  }

  private isIos(): boolean {
    const nav = this.window?.navigator;
    if (!nav) {
      return false;
    }
    return /iphone|ipad|ipod/i.test(nav.userAgent) || this.isIpad();
  }

  private isIpad(): boolean {
    const nav = this.window?.navigator;
    if (!nav) {
      return false;
    }
    // iPadOS 13+ podaje User-Agent Maca — rozpoznajemy po obecności ekranu dotykowego.
    return /ipad/i.test(nav.userAgent) || (/Macintosh/.test(nav.userAgent) && nav.maxTouchPoints > 1);
  }
}
