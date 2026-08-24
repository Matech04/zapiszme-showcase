import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NavItem } from '@core/models/navigation.model';
import { NavigationService } from '@core/services/NavigationService';

@Component({
  selector: 'app-mobile-nav',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <nav class="admin-glass-card w-full rounded-3xl z-50 px-2 pb-[env(safe-area-inset-bottom)]">
      <div class="flex items-center justify-around h-16">
        @for (item of menuItems(); track item.label) {
          <a
            [routerLink]="['/admin', ...(item.linkPath ?? item.path).split('/')]"
            [attr.aria-label]="item.label"
            class="flex flex-col items-center justify-center min-w-[44px] flex-1 py-2 px-1 rounded-2xl transition-all duration-300 group relative cursor-pointer"
          >
            <span class="relative">
              <i
                [class]="item.icon"
                class="text-2xl transition-all duration-300 group-active:scale-90"
                [class.text-primary]="navLinkActive(item)"
                [class.text-surface-400]="!navLinkActive(item)"
                [class.scale-110]="navLinkActive(item)"
              ></i>
              @if (item.badge && item.badge > 0) {
                <span
                  class="absolute -top-1 -right-2 min-w-4 h-4 px-1 rounded-full bg-primary text-primary-contrast text-[9px] font-black grid place-items-center"
                >
                  {{ item.badge > 9 ? '9+' : item.badge }}
                </span>
              }
            </span>
            <span
              class="w-1.5 h-1.5 rounded-full bg-primary transition-all duration-300 absolute -bottom-1"
              [class.opacity-100]="navLinkActive(item)"
              [class.opacity-0]="!navLinkActive(item)"
              [class.scale-100]="navLinkActive(item)"
              [class.scale-0]="!navLinkActive(item)"
            ></span>
          </a>
        }

        @if (showMoreButton()) {
          <button
            type="button"
            (click)="moreClick.emit()"
            aria-label="Więcej"
            class="flex flex-col items-center justify-center min-w-[44px] flex-1 py-2 px-1 rounded-2xl text-surface-400 hover:text-surface-700 dark:hover:text-surface-300 transition-colors"
          >
            <i class="pi pi-ellipsis-h text-2xl"></i>
          </button>
        }
      </div>
    </nav>
  `,
  styles: [
    `
      :host {
        display: block;
        width: 100%;
      }
    `,
  ],
})
export class MobileNavComponent {
  private readonly router = inject(Router);
  private readonly nav = inject(NavigationService);

  menuItems = input.required<NavItem[]>();
  showMoreButton = input<boolean>(false);
  readonly moreClick = output<void>();

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects.split('?')[0]),
    ),
    { initialValue: this.router.url.split('?')[0] },
  );

  navLinkActive(item: NavItem): boolean {
    return this.nav.isItemActive(item.path, this.currentUrl());
  }
}
