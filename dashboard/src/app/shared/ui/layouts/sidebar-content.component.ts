import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { NavGroup, NavItem } from '@core/models/navigation.model';
import { NavigationService } from '@core/services/NavigationService';

@Component({
  selector: 'app-sidebar-content',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="flex flex-col h-full">
      <!-- Na szynie (lg) zostaje sam znaczek — nazwa wróciłaby łamana na dwie linie w 5rem. -->
      <div
        class="h-28 flex items-center justify-center xl:justify-between px-0 xl:px-7 border-b border-surface-200/70 dark:border-surface-200/70"
      >
        <div class="hidden xl:flex flex-col">
          <span class="text-xl font-black tracking-tight text-slate-950 dark:text-slate-100">zapisz.me</span>
          <span class="admin-section-label text-primary">Panel salonu</span>
        </div>
        <span
          class="grid size-10 place-items-center rounded-2xl bg-slate-950 text-white dark:bg-white dark:text-slate-950 shadow-sm shrink-0"
          [attr.aria-label]="'zapisz.me — panel salonu'"
        >
          <i class="pi pi-wave-pulse text-sm"></i>
        </span>
      </div>

      <nav class="flex-1 py-6 flex flex-col gap-3 px-2 xl:px-4 overflow-y-auto overflow-x-hidden">
        @for (group of menuGroups(); track group.section; let isFirst = $first) {
          @if (group.label) {
            <p
              class="hidden xl:block admin-section-label text-surface-500 dark:text-surface-400 px-4"
              [class.mt-3]="!isFirst"
            >
              {{ group.label }}
            </p>
          }
          <div class="flex flex-col gap-1.5">
            @for (item of group.items; track item.label) {
              <a
                [routerLink]="['/admin', ...(item.linkPath ?? item.path).split('/')]"
                [attr.aria-label]="item.label"
                [attr.title]="item.label"
                [class.bg-slate-950]="navLinkActive(item)"
                [class.text-white]="navLinkActive(item)"
                [class.dark:bg-white/10]="navLinkActive(item)"
                [class.dark:text-white]="navLinkActive(item)"
                [class.shadow-lg]="navLinkActive(item)"
                [class.shadow-slate-900/15]="navLinkActive(item)"
                class="flex items-center justify-center xl:justify-start gap-0 xl:gap-4 px-0 xl:px-4 py-3.5 rounded-2xl text-surface-700 dark:text-surface-300 hover:bg-white/80 dark:hover:bg-surface-100/60 hover:text-surface-900 dark:hover:text-surface-900 transition-all group cursor-pointer"
              >
                <span class="relative shrink-0">
                  <i [class]="item.icon" class="text-xl transition-transform group-hover:scale-110"></i>
                  @if (item.badge && item.badge > 0) {
                    <span
                      class="absolute -top-1 -right-2 min-w-4 h-4 px-1 rounded-full bg-amber-500 text-white text-[9px] font-black grid place-items-center border-2 border-current/0"
                      [attr.aria-label]="item.badge + ' nowych'"
                    >
                      {{ item.badge > 9 ? '9+' : item.badge }}
                    </span>
                  }
                </span>
                <!-- Na szynie etykieta znika; nazwę niesie aria-label + natywny tooltip. -->
                <span class="hidden xl:inline font-sans text-[11px] font-bold uppercase tracking-[0.16em]">{{ item.label }}</span>
              </a>
            }
          </div>
        }
      </nav>

      <div class="p-2 xl:p-4 border-t border-surface-200/70 dark:border-surface-200/70">
        <button
          type="button"
          (click)="logout()"
          aria-label="Wyloguj"
          title="Wyloguj"
          class="flex items-center justify-center xl:justify-start gap-0 xl:gap-4 px-0 xl:px-4 py-4 text-red-600 dark:text-red-400 w-full hover:bg-red-50 dark:hover:bg-red-950/30 rounded-2xl transition-all group cursor-pointer"
        >
          <i class="pi pi-sign-out text-xl group-hover:scale-110 transition-transform"></i>
          <span class="hidden xl:inline font-sans text-[11px] font-bold uppercase tracking-[0.16em]">Wyloguj</span>
        </button>
      </div>
    </div>
  `,
})
export class SidebarContentComponent {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthSessionService);
  private readonly nav = inject(NavigationService);

  menuGroups = input.required<NavGroup[]>();

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects.split('?')[0]),
    ),
    { initialValue: this.router.url.split('?')[0] },
  );

  logout(): void {
    this.auth.logout();
  }

  navLinkActive(item: NavItem): boolean {
    return this.nav.isItemActive(item.path, this.currentUrl());
  }
}
