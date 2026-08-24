import { Component, computed, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { EmployeeScheduleDto, EmployeesClient } from '@core/api/api-client';
import { buildStandardWeekScheduleDto } from '@core/api/quick-start-schedule';
import { rxResource } from '@angular/core/rxjs-interop';
import { MessageService } from 'primeng/api';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { GuideLauncherComponent } from '@shared/ui/guide-launcher/guide-launcher.component';

@Component({
  selector: 'app-employee-availability-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, GuideLauncherComponent],
  template: `
    <div class="admin-page-shell">
      <div class="max-w-3xl mx-auto w-full">
        <nav
          class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1"
        >
          <a [routerLink]="hubLink()" class="hover:text-primary transition-colors">{{ hubLabel() }}</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <span class="text-surface-700 dark:text-surface-300">{{ pageTitle() }}</span>
        </nav>

        <div class="mb-8 flex items-start justify-between gap-4">
          <div class="min-w-0">
            <h1 class="text-2xl sm:text-3xl font-black text-surface-900 tracking-tight">
              {{ pageTitle() }}
            </h1>
          </div>
          <app-guide-launcher />
        </div>

        @if (!isSelfMode()) {
          <div class="admin-glass-card rounded-4xl p-4 sm:p-5 shadow-sm mb-8">
            <div class="flex items-center gap-4">
              <div
                class="shrink-0 w-14 h-14 rounded-xl border-2 border-primary/30 bg-primary/5 flex items-center justify-center text-lg font-bold text-primary"
                aria-hidden="true"
              >
                {{ employeeInitials() }}
              </div>
              <div class="min-w-0 flex-1">
                <p class="font-bold text-surface-900 text-base sm:text-lg truncate">
                  {{ employeeDisplayName() }}
                </p>
                <p class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 truncate">
                  {{ employeeSubtitle() }}
                </p>
              </div>
            </div>
          </div>
        }

        <!-- Onboarding: nowy pracownik bez grafiku widzi szybki start. Zaproszenie do
             przewodnika zniknęło stąd wraz z banerami — przewodniki mają własne miejsce
             (katalog /admin/guides) i pigułkę „?" w nagłówku tego ekranu. -->
        @if (showQuickStartBanner()) {
          <section class="rounded-3xl border border-amber-300/60 dark:border-amber-700/40 bg-amber-50/70 dark:bg-amber-950/30 p-5 sm:p-6 mb-6">
            <div class="flex flex-col sm:flex-row sm:items-center gap-4">
              <div class="flex-1 min-w-0">
                <p class="text-xs font-bold text-amber-700 dark:text-amber-300 uppercase tracking-wider mb-1">
                  Szybki start
                </p>
                <h2 class="text-base sm:text-lg font-bold text-surface-900 mb-1">
                  Pracujesz w standardowych godzinach?
                </h2>
                <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed">
                  Utworzymy grafik <strong>9:00–17:00, poniedziałek–piątek, bezterminowo</strong>.
                  Możesz go potem swobodnie edytować.
                </p>
              </div>
              <button
                type="button"
                class="inline-flex items-center justify-center gap-2 rounded-full px-5 py-2.5 text-sm font-bold bg-amber-500 text-amber-950 hover:bg-amber-600 transition-colors disabled:opacity-60 disabled:cursor-not-allowed shrink-0"
                [disabled]="quickStartPending()"
                (click)="onQuickStart()"
              >
                @if (quickStartPending()) {
                  <i class="pi pi-spin pi-spinner"></i> Tworzę…
                } @else {
                  <i class="pi pi-bolt"></i> Utwórz standardowy tydzień
                }
              </button>
            </div>
          </section>
        }

        <!-- Wszystkie akcje jako jednolite kafelki (jedno UI) -->
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
          <a
            data-tour="schedules-card"
            [routerLink]="[routeBase(), id(), 'schedules']"
            class="group rounded-3xl border border-surface-200/90 dark:border-surface-100 bg-white/75 dark:bg-surface-50 p-5 shadow-sm transition-all hover:shadow-md hover:border-primary/35 dark:hover:border-primary/30 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <div
              class="w-11 h-11 rounded-xl bg-primary/12 text-primary flex items-center justify-center mb-4 group-hover:bg-primary/18 transition-colors"
            >
              <i class="pi pi-calendar text-lg" aria-hidden="true"></i>
            </div>
            <h3 class="text-lg font-bold text-surface-900 mb-1">Grafik powtarzalny</h3>
            <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed">
              Twoje godziny pracy, które powtarzają się co tydzień. To je klienci widzą jako wolne terminy.
            </p>
            <span
              class="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-primary group-hover:gap-2 transition-all"
            >
              Otwórz
              <i class="pi pi-arrow-right text-xs" aria-hidden="true"></i>
            </span>
          </a>

          <a
            data-tour="overrides-card"
            [routerLink]="[routeBase(), id(), 'special-days']"
            class="group rounded-3xl border border-surface-200/90 dark:border-surface-100 bg-white/75 dark:bg-surface-50 p-5 shadow-sm transition-all hover:shadow-md hover:border-primary/35 dark:hover:border-primary/30 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <div
              class="w-11 h-11 rounded-xl bg-primary/12 text-primary flex items-center justify-center mb-4 group-hover:bg-primary/18 transition-colors"
            >
              <i class="pi pi-star text-lg" aria-hidden="true"></i>
            </div>
            <h3 class="text-lg font-bold text-surface-900 mb-1">Godziny na wybrany dzień</h3>
            <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed">
              Inne godziny lub wolne dla jednej, konkretnej daty — np. „w ten piątek pracuję do 14". Działa tylko w tym dniu.
            </p>
            <span
              class="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-primary group-hover:gap-2 transition-all"
            >
              Otwórz
              <i class="pi pi-arrow-right text-xs" aria-hidden="true"></i>
            </span>
          </a>

          <a
            data-tour="leaves-card"
            [routerLink]="[routeBase(), id(), 'leave-dashboard']"
            class="group rounded-3xl border border-surface-200/90 dark:border-surface-100 bg-white/75 dark:bg-surface-50 p-5 shadow-sm transition-all hover:shadow-md hover:border-primary/35 dark:hover:border-primary/30 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <div
              class="w-11 h-11 rounded-xl bg-primary/12 text-primary flex items-center justify-center mb-4 group-hover:bg-primary/18 transition-colors"
            >
              <i class="pi pi-calendar-minus text-lg" aria-hidden="true"></i>
            </div>
            <h3 class="text-lg font-bold text-surface-900 mb-1">Urlopy i chorobowe</h3>
            <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed">
              Kilkudniowe nieobecności — w tym czasie klienci nie zarezerwują wizyty.
            </p>
            <span
              class="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-primary group-hover:gap-2 transition-all"
            >
              Otwórz
              <i class="pi pi-arrow-right text-xs" aria-hidden="true"></i>
            </span>
          </a>

          @if (showShiftTemplatesCard()) {
            <a
              routerLink="/admin/resources/shift-templates"
              data-testid="availability-shift-templates-card"
              class="group rounded-3xl border border-surface-200/90 dark:border-surface-100 bg-white/75 dark:bg-surface-50 p-5 shadow-sm transition-all hover:shadow-md hover:border-primary/35 dark:hover:border-primary/30 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <div
                class="w-11 h-11 rounded-xl bg-primary/12 text-primary flex items-center justify-center mb-4 group-hover:bg-primary/18 transition-colors"
              >
                <i class="pi pi-clone text-lg" aria-hidden="true"></i>
              </div>
              <h3 class="text-lg font-bold text-surface-900 mb-1">Szablony zmian</h3>
              <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed">
                Gotowe zestawy godzin, które szybko przypiszesz do dni bez ręcznego wpisywania.
              </p>
              <span
                class="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-primary group-hover:gap-2 transition-all"
              >
                Otwórz
                <i class="pi pi-arrow-right text-xs" aria-hidden="true"></i>
              </span>
            </a>
          }

          @if (!isSelfMode()) {
            <a
              [routerLink]="[routeBase(), id(), 'services']"
              class="group rounded-3xl border border-surface-200/90 dark:border-surface-100 bg-white/75 dark:bg-surface-50 p-5 shadow-sm transition-all hover:shadow-md hover:border-primary/35 dark:hover:border-primary/30 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <div
                class="w-11 h-11 rounded-xl bg-primary/12 text-primary flex items-center justify-center mb-4 group-hover:bg-primary/18 transition-colors"
              >
                <i class="pi pi-briefcase text-lg" aria-hidden="true"></i>
              </div>
              <h3 class="text-lg font-bold text-surface-900 mb-1">Usługi pracownika</h3>
              <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed">
                Usługi, które ten pracownik może wykonywać i oferować klientom w rezerwacji.
              </p>
              <span
                class="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-primary group-hover:gap-2 transition-all"
              >
                Otwórz
                <i class="pi pi-arrow-right text-xs" aria-hidden="true"></i>
              </span>
            </a>
          }
        </div>
      </div>
    </div>
  `
})
export class EmployeeAvailabilityDashboardComponent {
  private employeesService = inject(EmployeesClient);
  private router = inject(Router);
  private messageService = inject(MessageService);
  private auth = inject(AuthSessionService);

  id = input.required<string>();
  readonly isSelfMode = computed(() => this.router.url.startsWith('/admin/my-availability/'));

  /** Tytuł ekranu — w self-mode osobowo („Twoja"), w widoku admina o pracowniku. */
  protected pageTitle = computed(() => (this.isSelfMode() ? 'Twoja dostępność' : 'Dostępność pracownika'));

  /**
   * Czy użytkownik może zarządzać szablonami zmian (Owner/Manager) — zgodnie z polityką API
   * `StaffManagement` oraz `staffManagementGuard` chroniącym route `/admin/resources/shift-templates`.
   * Pracownik (Employee) nie ma tam dostępu, więc nie pokazujemy mu prowadzącej donikąd karty.
   */
  readonly canManageShiftTemplates = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager';
  });

  /**
   * Link „Szablony zmian": w widoku admina (nie self-mode) zawsze; w „Mojej dostępności" tylko gdy
   * użytkownik faktycznie ma dostęp do panelu szablonów (Owner/Manager).
   */
  // public (readonly) — odczytywane w spec; reszta API komponentu pozostaje `protected`.
  readonly showShiftTemplatesCard = computed(() => !this.isSelfMode() || this.canManageShiftTemplates());
  protected routeBase = computed(() =>
    this.isSelfMode() ? '/admin/my-availability' : '/admin/resources/employees');

  protected hubLabel = computed(() =>
    this.isSelfMode() ? 'Moja dostępność' : 'Zarządzanie');

  protected hubLink = computed(() =>
    this.isSelfMode()
      ? ['/admin/schedule']
      : '/admin/resources');

  employeeData = rxResource({
    stream: () => this.employeesService.getEmployee(this.id())
  });

  schedulesData = rxResource({
    // Klucz skalarny — obiektowy literał psuł porównanie tożsamości i wymuszał refetch.
    params: () => this.id(),
    stream: ({ params: employeeId }) => this.employeesService.getEmployeeSchedules(employeeId),
    defaultValue: [] as EmployeeScheduleDto[],
  });

  protected hasAnySchedule = computed(() => {
    if (this.schedulesData.isLoading()) return undefined;
    return (this.schedulesData.value() ?? []).length > 0;
  });

  protected quickStartPending = signal(false);

  /** Banner widoczny tylko gdy mamy potwierdzenie braku grafików (po wczytaniu). */
  protected showQuickStartBanner = computed(() => this.hasAnySchedule() === false);

  employeeDisplayName = computed(() => {
    const e = this.employeeData.value();
    if (!e) return 'Pracownik';
    const parts = [e.firstName, e.lastName].filter(Boolean);
    return parts.length ? parts.join(' ') : 'Pracownik';
  });

  employeeSubtitle = computed(() => {
    const e = this.employeeData.value();
    if (!e?.email) return 'Grafik, urlopy i godziny dni';
    return e.email;
  });

  employeeInitials = computed(() => {
    const e = this.employeeData.value();
    if (!e) return '?';
    const a = (e.firstName?.trim()?.[0] ?? '').toUpperCase();
    const b = (e.lastName?.trim()?.[0] ?? '').toUpperCase();
    const initials = `${a}${b}`.trim();
    return initials || '?';
  });

  onQuickStart(): void {
    if (this.quickStartPending()) return;
    this.quickStartPending.set(true);
    this.employeesService.setEmployeeSchedule(this.id(), buildStandardWeekScheduleDto()).subscribe({
      next: () => {
        this.quickStartPending.set(false);
        this.schedulesData.reload();
        this.messageService.add({
          severity: 'success',
          summary: 'Grafik utworzony',
          detail: 'Standardowy tydzień 9–17 (pon–pt) jest aktywny. Klienci widzą Twoje wolne godziny.',
          life: 5000,
        });
      },
      error: () => {
        this.quickStartPending.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Nie udało się utworzyć grafiku',
          detail: 'Spróbuj ponownie lub utwórz grafik ręcznie w sekcji „Grafik powtarzalny".',
          life: 5000,
        });
      },
    });
  }
}
