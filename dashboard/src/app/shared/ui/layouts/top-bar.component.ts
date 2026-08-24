import { ChangeDetectionStrategy, Component, output } from '@angular/core';
import { NotificationsBellComponent } from './notifications-bell.component';
import { UserAvatarMenuComponent } from './user-avatar-menu.component';

@Component({
  selector: 'app-top-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NotificationsBellComponent, UserAvatarMenuComponent],
  template: `
    <header
      class="admin-glass-card flex items-center gap-3 px-4 py-2.5 rounded-3xl mb-4 shrink-0 z-30"
    >
      <button
        type="button"
        (click)="searchTrigger.emit()"
        class="flex items-center gap-3 flex-1 min-w-0 rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/60 dark:bg-surface-50/45 px-4 py-2.5 hover:border-primary/45 transition-colors text-left group"
        aria-label="Otwórz wyszukiwanie"
      >
        <i class="pi pi-search text-surface-500 dark:text-surface-400 text-sm" aria-hidden="true"></i>
        <span class="flex-1 truncate text-sm text-surface-500 dark:text-surface-400 group-hover:text-surface-700 dark:group-hover:text-surface-200 transition-colors">
          Szukaj klientów, akcji…
        </span>
        <span class="hidden md:flex items-center gap-1 shrink-0">
          <kbd class="px-1.5 py-0.5 rounded-md border border-surface-200/70 dark:border-surface-200/70 text-[10px] font-mono font-bold text-surface-500">⌘</kbd>
          <kbd class="px-1.5 py-0.5 rounded-md border border-surface-200/70 dark:border-surface-200/70 text-[10px] font-mono font-bold text-surface-500">K</kbd>
        </span>
      </button>

      <app-notifications-bell />
      <app-user-avatar-menu />
    </header>
  `,
})
export class TopBarComponent {
  readonly searchTrigger = output<void>();
}
