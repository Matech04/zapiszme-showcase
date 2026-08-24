import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, isDevMode, model } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, NavigationSkipped, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { Drawer } from 'primeng/drawer';
import { NavGroup } from '@core/models/navigation.model';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { ThemeMode, ThemeModeService } from '@core/theme/theme-mode.service';
import { PwaInstallService } from '@core/pwa/pwa-install.service';
import { PushNotificationsService } from '@core/services/push-notifications.service';
import { BackButtonCloseService } from '@core/services/back-button-close.service';

@Component({
  selector: 'app-more-sheet',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Drawer, RouterLink],
  template: `
    <p-drawer
      [(visible)]="visible"
      position="bottom"
      [showCloseIcon]="false"
      styleClass="admin-drawer-shell !h-auto !rounded-t-3xl admin-glass-card !border-none"
    >
      <div class="flex flex-col pb-[env(safe-area-inset-bottom)]">
        <div class="flex justify-center pt-2.5 pb-1">
          <span class="block w-12 h-1.5 rounded-full bg-surface-300 dark:bg-surface-600" aria-hidden="true"></span>
        </div>

        <header class="px-5 pb-3 flex items-center justify-between">
          <div>
            <p class="admin-section-label text-primary">Więcej</p>
            <p class="text-base font-bold text-surface-900">{{ displayName() }}</p>
          </div>
        </header>

        <div class="px-3 pb-2 flex flex-col gap-3">
          @for (group of groups(); track group.section; let isFirst = $first) {
            @if (group.label) {
              <p class="admin-section-label text-surface-500 dark:text-surface-400 px-2" [class.mt-2]="!isFirst">
                {{ group.label }}
              </p>
            }
            <nav class="grid grid-cols-3 gap-2">
              @for (item of group.items; track item.label) {
                <a
                  [routerLink]="['/admin', ...(item.linkPath ?? item.path).split('/')]"
                  [replaceUrl]="true"
                  class="flex flex-col items-center gap-1.5 px-2 py-3 rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 hover:border-primary/45 transition-colors text-surface-700"
                >
                  <i [class]="item.icon" class="text-xl"></i>
                  <span class="text-[11px] font-bold uppercase tracking-wider text-center">{{ item.label }}</span>
                </a>
              }
            </nav>
          }
        </div>

        @if (install.available()) {
          <section class="px-5 pt-3 pb-1">
            <button
              type="button"
              (click)="installApp()"
              data-testid="pwa-install"
              class="w-full flex items-center gap-3 rounded-2xl px-4 py-3 border border-primary/40 bg-primary/10 text-left hover:bg-primary/15 transition-colors"
            >
              <i class="pi pi-download text-primary text-xl"></i>
              <span class="flex-1 min-w-0">
                <span class="block text-sm font-bold text-surface-900">Zainstaluj aplikację</span>
                <span class="block text-xs text-surface-500 dark:text-surface-400">Szybszy dostęp z ekranu głównego</span>
              </span>
            </button>
          </section>
        }

        @if (pushVisible() || isDev) {
          <section class="px-5 pt-3 pb-1">
            <p class="admin-section-label text-surface-500 mb-2">Powiadomienia</p>
            <button
              type="button"
              (click)="togglePush()"
              [disabled]="push.busy()"
              data-testid="more-push-toggle"
              class="w-full flex items-center gap-3 rounded-2xl px-4 py-3 border border-surface-200/70 dark:border-surface-200/70 text-sm font-semibold text-surface-700 hover:border-primary/45 transition-colors disabled:opacity-60"
            >
              <i class="pi pi-bell text-base text-primary"></i>
              <span class="flex-1 min-w-0 text-left">
                {{ push.subscribed() ? 'Wyłącz na tym urządzeniu' : 'Włącz na tym urządzeniu' }}
              </span>
              @if (isDev && !pushVisible()) {
                <span class="shrink-0 rounded-full bg-amber-400/20 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider text-amber-700 dark:text-amber-300">dev</span>
              }
            </button>
          </section>
        }

        <section class="px-5 pt-4 pb-2">
          <p class="admin-section-label text-surface-500 mb-2">Motyw</p>
          <div class="grid grid-cols-3 gap-2">
            @for (m of modes; track m.value) {
              <button
                type="button"
                (click)="setMode(m.value)"
                [class.bg-slate-900]="mode() === m.value"
                [class.text-white]="mode() === m.value"
                [class.dark:bg-surface-200]="mode() === m.value"
                [class.dark:text-surface-900]="mode() === m.value"
                class="flex flex-col items-center gap-1 py-2 rounded-xl border border-surface-200/70 dark:border-surface-200/70 text-xs font-bold uppercase tracking-wider hover:border-primary/45 transition-colors text-surface-700"
              >
                <i [class]="m.icon" class="text-base"></i>
                {{ m.label }}
              </button>
            }
          </div>
        </section>

        <div class="px-3 pt-2 pb-4 border-t border-surface-200/70 dark:border-surface-200/70 mt-3">
          <button
            type="button"
            (click)="logout()"
            class="w-full flex items-center justify-center gap-2 rounded-2xl px-4 py-3 text-red-600 dark:text-red-400 font-bold text-sm hover:bg-red-50 dark:hover:bg-red-950/30 transition-colors"
          >
            <i class="pi pi-sign-out"></i>
            Wyloguj
          </button>
        </div>
      </div>
    </p-drawer>
  `,
})
export class MoreSheetComponent {
  private readonly auth = inject(AuthSessionService);
  private readonly themeMode = inject(ThemeModeService);
  private readonly router = inject(Router);
  protected readonly install = inject(PwaInstallService);
  protected readonly push = inject(PushNotificationsService);
  private readonly backClose = inject(BackButtonCloseService);

  readonly visible = model<boolean>(false);
  readonly groups = input.required<NavGroup[]>();

  private readonly closeFromBack = (): void => this.visible.set(false);
  private backRegistered = false;

  constructor() {
    // Zamykamy arkusz DOPIERO po zatwierdzeniu nawigacji (NavigationEnd), nie w handlerze kliknięcia.
    // Gdyby close() padał synchronicznie w (click), efekt wołałby BackButtonCloseService.pop() zanim
    // router zdąży zaktualizować URL — pop() widziałby jeszcze wpis nakładki na wierzchu i robił
    // history.back(), co anulowało nawigację (link „działał" dopiero za drugim razem). Linki mają
    // [replaceUrl], więc wpis nakładki jest konsumowany przez nawigację — pop() nie cofa historii.
    // NavigationSkipped pokrywa tap w pozycję bieżącego ekranu (router pomija nawigację).
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd || e instanceof NavigationSkipped),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        if (this.visible()) {
          this.visible.set(false);
        }
      });

    // „Wstecz" (Android/gest) najpierw zamyka drawer zamiast cofać treść — patrz BackButtonCloseService.
    effect(() => {
      const open = this.visible();
      if (open === this.backRegistered) {
        return;
      }
      this.backRegistered = open;
      if (open) {
        this.backClose.push(this.closeFromBack);
      } else {
        this.backClose.pop(this.closeFromBack);
      }
    });

    // Zniszczenie komponentu przy otwartym arkuszu (wylogowanie) nie przechodzi przez efekt powyżej,
    // więc closer zostałby na stosie i zjadł jedno „wstecz" na ekranie logowania.
    inject(DestroyRef).onDestroy(() => {
      if (this.backRegistered) {
        this.backRegistered = false;
        this.backClose.pop(this.closeFromBack);
      }
    });
  }

  readonly mode = this.themeMode.mode;
  // W dev SW jest wyłączony → supported() = false i sekcja byłaby niewidoczna. Wymuszamy jej pokazanie
  // (z plakietką „dev"), by dało się podejrzeć UI. W prod = false.
  protected readonly isDev = isDevMode();
  // Realna widoczność: push wspierany albo iOS wymaga instalacji PWA (wtedy klik pokazuje instrukcję).
  protected readonly pushVisible = computed(
    () => this.push.supported() || this.push.iosNeedsInstall(),
  );

  readonly modes: { label: string; value: ThemeMode; icon: string }[] = [
    { label: 'Jasny', value: 'light', icon: 'pi pi-sun' },
    { label: 'Ciemny', value: 'dark', icon: 'pi pi-moon' },
    { label: 'Auto', value: 'system', icon: 'pi pi-desktop' },
  ];

  setMode(value: ThemeMode): void {
    this.themeMode.setMode(value);
  }

  togglePush(): void {
    // W dev-preview (sekcja wymuszona, push realnie niewspierany) klik jest no-op — chodzi o podgląd UI.
    if (this.isDev && !this.pushVisible()) {
      return;
    }
    if (this.push.subscribed()) {
      void this.push.disable();
    } else {
      void this.push.enable();
    }
  }

  async installApp(): Promise<void> {
    if (this.install.canInstall()) {
      await this.install.promptInstall();
      this.close();
      return;
    }
    if (this.install.iosInstructions()) {
      // iOS nie ma natywnego promptu — otwieramy animowany przewodnik „Do ekranu początkowego".
      this.close();
      this.install.openIosGuide();
    }
  }

  close(): void {
    this.visible.set(false);
  }

  displayName(): string {
    const session = this.auth.session();
    return session?.displayName ?? session?.email ?? 'Użytkownik';
  }

  logout(): void {
    // Świadomie NIE wołamy close(). PrimeNG trzyma maskę (`.p-overlay-mask`) w <body> i usuwa ją
    // dopiero po animacji wyjścia, a nawigacja na /login niszczy layout szybciej. Drawer.onDestroy
    // sprząta maskę tylko gdy `visible` jest wciąż true — po close() zostawał przyciemniony ekran
    // (i zablokowany scroll body) aż do odświeżenia. Arkusz i tak znika wraz z komponentem.
    // Wpis „wstecz" zdejmuje hook w konstruktorze.
    this.auth.logout();
  }
}
