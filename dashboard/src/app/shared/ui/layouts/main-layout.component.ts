import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, viewChild } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { catchError, of, filter, map } from 'rxjs';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { SidebarContentComponent } from './sidebar-content.component';
import { MobileNavComponent } from './mobile-nav.component';
import { TopBarComponent } from './top-bar.component';
import { CommandPaletteComponent } from './command-palette.component';
import { MoreSheetComponent } from './more-sheet.component';
import { NotificationsBellComponent } from './notifications-bell.component';
import { ImpersonationBannerComponent } from './impersonation-banner.component';
import { IosInstallGuideComponent } from '@shared/ui/pwa/ios-install-guide.component';
import { PwaInstallService } from '@core/pwa/pwa-install.service';
import { NavigationService } from '@core/services/NavigationService';
import { NotificationCenterService } from '@core/services/notification-center.service';
import { BookingPauseStore } from '@core/services/booking-pause.store';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { LastScheduleEmployeeStore } from '@domains/appointments/data-access/last-schedule-employee.store';
import {
  SalonSettingsClient,
  StaffCalendarVisibilityPolicy,
} from '@core/api/api-client';
import type { NavItem } from '@core/models/navigation.model';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    RouterLink,
    SidebarContentComponent,
    MobileNavComponent,
    TopBarComponent,
    CommandPaletteComponent,
    MoreSheetComponent,
    NotificationsBellComponent,
    ImpersonationBannerComponent,
    IosInstallGuideComponent,
  ],
  template: `
    <div class="flex h-dvh overflow-hidden bg-transparent p-3 sm:p-4">
      <!-- Tablet (lg) = szyna z samymi ikonami; od xl pełny sidebar z etykietami. -->
      <aside
        class="admin-glass-card hidden lg:flex w-20 xl:w-[20rem] shrink-0 flex-col rounded-[2rem] z-40 overflow-hidden"
      >
        <app-sidebar-content [menuGroups]="menuGroups()"></app-sidebar-content>
      </aside>

      <div class="flex-1 flex flex-col min-w-0 relative lg:pl-5">
        <div class="hidden lg:block">
          <app-top-bar (searchTrigger)="commandPalette().open()" />
        </div>

        <main class="flex-1 overflow-y-auto relative z-10 [scrollbar-gutter:stable]">
          <header
            class="admin-mobile-chrome-only admin-glass-card lg:hidden flex items-center justify-between px-5 h-18 rounded-3xl mb-3 text-surface-900"
          >
            <div class="flex flex-col">
              <span class="font-black text-lg tracking-tight text-slate-950 dark:text-slate-100">
                zapisz.me
              </span>
              @if (activeNavLabel(); as label) {
                <span class="admin-section-label text-primary">{{ label }}</span>
              }
            </div>
            <div class="flex items-center gap-3">
              <button
                type="button"
                (click)="commandPalette().open()"
                class="grid size-10 place-items-center rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 text-surface-700"
                aria-label="Szukaj"
              >
                <i class="pi pi-search text-sm"></i>
              </button>
              <app-notifications-bell />
            </div>
          </header>

          @if (isDemo()) {
            <div
              data-testid="demo-banner"
              class="flex flex-wrap items-center justify-center gap-x-3 gap-y-1 rounded-2xl mb-3 px-4 py-2 text-sm bg-amber-400/20 border border-amber-400/50 text-amber-900 dark:text-amber-100"
              role="status"
            >
              <span class="font-semibold">✨ Tryb demo</span>
              <span class="text-amber-800/80 dark:text-amber-200/80">
                Przeglądasz przykładowy salon — dane resetują się automatycznie.
              </span>
              <a
                routerLink="/register"
                class="font-semibold underline underline-offset-2 hover:opacity-80"
              >
                Załóż własny salon
              </a>
              <button
                type="button"
                data-testid="demo-exit"
                (click)="exitDemo()"
                class="ml-1 rounded-lg px-2 py-0.5 border border-amber-500/50 hover:bg-amber-400/25 transition-colors"
              >
                Zakończ demo
              </button>
            </div>
          }
          @if (bookingPause.paused()) {
            <div
              data-testid="booking-pause-banner"
              class="flex flex-wrap items-center justify-center gap-x-3 gap-y-1 rounded-2xl mb-3 px-4 py-2 text-sm bg-red-400/20 border border-red-400/50 text-red-900 dark:text-red-100"
              role="alert"
            >
              <span class="font-semibold">⏸️ Rezerwacje wstrzymane</span>
              <span class="text-red-800/80 dark:text-red-200/80">
                Klienci nie mogą teraz rezerwować online. Wznów, gdy skończysz zmiany w grafiku.
              </span>
              @if (canManageBookingPause()) {
                <button
                  type="button"
                  data-testid="booking-pause-banner-resume"
                  [disabled]="resumingBooking()"
                  (click)="resumeBooking()"
                  class="ml-1 rounded-lg px-2 py-0.5 border border-red-500/50 hover:bg-red-400/25 transition-colors disabled:opacity-60"
                >
                  Wznów rezerwacje
                </button>
              }
            </div>
          }
          <app-impersonation-banner />
          <router-outlet></router-outlet>
        </main>

        <app-mobile-nav
          [menuItems]="bottomNavMenuWithBadges()"
          [showMoreButton]="true"
          (moreClick)="moreOpen.set(true)"
          class="admin-mobile-chrome-only lg:hidden shrink-0"
        />
      </div>

      <app-command-palette #palette />
      <app-more-sheet [(visible)]="moreOpen" [groups]="moreGroups()" />
      <app-ios-install-guide />
    </div>
  `,
})
export class MainLayoutComponent {
  private readonly navService = inject(NavigationService);
  private readonly router = inject(Router);
  private readonly notifs = inject(NotificationCenterService);

  /** URL bez query/hash — do wyznaczenia aktywnej pozycji nawigacji (podtytuł nagłówka mobilnego). */
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(() => this.router.url.split('?')[0].split('#')[0]),
    ),
    { initialValue: this.router.url.split('?')[0].split('#')[0] },
  );

  /**
   * Etykieta aktualnego ekranu (np. „Kalendarz", „Klienci") — pokazywana jako podtytuł pod
   * wordmarkiem na mobilnym nagłówku. Zastępuje dawny statyczny „Dashboard" (usunięty).
   */
  readonly activeNavLabel = computed(() => {
    const url = this.currentUrl();
    const item = this.navService
      .filteredMenu()
      .find((i) => this.navService.isItemActive(i.path, url));
    return item?.label ?? '';
  });
  private readonly auth = inject(AuthSessionService);
  private readonly lastScheduleEmployee = inject(LastScheduleEmployeeStore);
  protected readonly bookingPause = inject(BookingPauseStore);
  private readonly install = inject(PwaInstallService);

  constructor() {
    // Auto-podpowiedź instalacji na iOS Safari (raz na sesję, o ile nie „nie pokazuj ponownie").
    // Tu, w uwierzytelnionym layoucie, żeby nie wyskakiwała na ekranie logowania.
    this.install.maybeAutoShowIosGuide();
  }

  protected readonly commandPalette = viewChild.required<CommandPaletteComponent>('palette');

  /** Stan zapisu szybkiego „Wznów" z banera — blokuje przycisk na czas żądania. */
  readonly resumingBooking = signal(false);

  /** Tylko Owner/Manager (BusinessManagement) mogą wznowić rezerwacje z banera. */
  readonly canManageBookingPause = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager';
  });

  /**
   * Pobierz stan wstrzymania rezerwacji raz na sesję (GET ustawień = GeneralAccess). Tylko dla ról
   * salonowych — admin platformy (systemAdmin) nie ma tenanta, więc GET /api/SalonSettings zwróciłby
   * 400 (tenant.missing) i pokazał zbędny toast. `refresh()` jest idempotentny (flaga `loaded`).
   */
  private readonly _loadBookingPause = effect(() => {
    const role = this.auth.currentRole();
    // `null` = sesja jeszcze nie wróciła z /api/auth/me. Zgodnie z kontraktem `currentRole`
    // traktujemy ten stan jak BRAK uprawnień, a nie „na pewno nie admin". Wcześniejszy warunek
    // `!== 'systemAdmin'` przepuszczał `null`, więc pierwszy przebieg efektu strzelał w
    // /api/SalonSettings jeszcze przed poznaniem roli — u admina platformy kończyło się to
    // 400 `tenant.missing`. Efekt i tak przeliczy się ponownie, gdy rola się ustali.
    if (role === null || role === 'systemAdmin') return;
    this.bookingPause.refresh();
  });

  resumeBooking(): void {
    if (this.resumingBooking()) return;
    this.resumingBooking.set(true);
    this.salonSettingsClient.setBookingPause({ paused: false }).subscribe({
      next: () => {
        this.bookingPause.set(false);
        this.resumingBooking.set(false);
      },
      error: () => {
        // Zachowaj baner — błąd zostanie zgłoszony przez globalny errorInterceptor.
        this.resumingBooking.set(false);
      },
    });
  }

  readonly menuGroups = this.navService.groupedMenu;
  readonly bottomNavMenu = this.navService.bottomNavMenu;
  readonly moreNavMenu = this.navService.moreNavMenu;
  readonly moreGroups = this.navService.moreNavGroups;

  /**
   * `canSeeTeam` dla nawigacji (pozycja „Zespół" pracownika). Owner/manager/kiosk = zawsze;
   * pracownik tylko gdy właściciel włączył widoczność zespołu (policy ≥ TeamReadOnly).
   * Ustawienia salonu pobieramy tylko dla pracownika (GeneralAccess → 200; owner/manager i tak true).
   */
  private readonly salonSettingsClient = inject(SalonSettingsClient);

  private readonly teamPolicyResource = rxResource({
    // Klucz skalarny, nie obiekt: `params` zwracające świeży literał ma za każdym razem nową
    // tożsamość, więc rxResource refetchuje przy KAŻDEJ emisji zależności — także gdy wartość
    // się nie zmieniła (tu: każde przejście hydratacji sesji).
    params: () => this.auth.currentRole() === 'employee',
    stream: ({ params: isEmployee }) => {
      if (!isEmployee) return of(undefined);
      return this.salonSettingsClient.get().pipe(catchError(() => of(undefined)));
    },
    defaultValue: undefined,
  });

  /**
   * Karmi NavigationService id-em pracownika, żeby link „Kalendarz" celował prosto
   * w `/admin/schedule/:employeeId`. Goła trasa wpada w redirect, który odtwarza komponent
   * kalendarza — zmierzone 44 requesty zamiast 22 przy każdym kliknięciu w menu.
   */
  private readonly _syncScheduleEmployeeHint = effect(() => {
    const userId = this.auth.currentUserId();
    this.navService.setScheduleEmployeeHints(
      this.auth.currentEmployeeId() ?? null,
      this.lastScheduleEmployee.read(userId),
    );
  });

  private readonly _syncCanSeeTeam = effect(() => {
    const role = this.auth.currentRole();
    if (role !== 'employee') {
      this.navService.setCanSeeTeam(role === 'owner' || role === 'manager' || role === 'kiosk');
      return;
    }
    const policy = this.teamPolicyResource.value()?.staffCalendarVisibilityPolicy;
    this.navService.setCanSeeTeam(
      policy === StaffCalendarVisibilityPolicy.TeamReadOnly ||
        policy === StaffCalendarVisibilityPolicy.TeamFull,
    );
  });

  /**
   * Bottom-nav z dynamicznymi badge'ami (F3.3). Liczbę oczekujących wizyt doklejamy do
   * pozycji 'schedule' (Plan dnia / Kalendarz) — tylko tam ma sens, bo CTA prowadzi do
   * widoku akcji. Resztę pozycji zwracamy bez zmian, żeby unikać podwójnych dot-ów.
   */
  readonly bottomNavMenuWithBadges = computed<NavItem[]>(() => {
    const pending = this.notifs.pendingCount();
    return this.bottomNavMenu().map((item) =>
      item.path === 'schedule' ? { ...item, badge: pending } : item,
    );
  });

  readonly moreOpen = signal(false);

  /** Steruje banerem trybu demo (sesja efemerycznego demo-tenanta). */
  readonly isDemo = this.auth.isDemo;

  /** Wyjście z demo = wylogowanie → powrót na ekran logowania. */
  exitDemo(): void {
    this.auth.logout();
  }
}
