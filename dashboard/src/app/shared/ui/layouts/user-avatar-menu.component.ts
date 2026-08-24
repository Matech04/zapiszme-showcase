import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Popover } from 'primeng/popover';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { ThemeMode, ThemeModeService } from '@core/theme/theme-mode.service';
import { PushNotificationsService } from '@core/services/push-notifications.service';

@Component({
  selector: 'app-user-avatar-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, Popover],
  template: `
    <button
      type="button"
      (click)="popover().toggle($event)"
      class="grid size-10 place-items-center rounded-2xl bg-slate-950 text-white dark:bg-white dark:text-slate-950 shadow-sm cursor-pointer hover:scale-105 transition-transform"
      aria-label="Profil"
    >
      <span class="text-xs font-black uppercase">{{ initials() }}</span>
    </button>

    <p-popover #pop styleClass="!rounded-3xl !p-0 admin-glass-card !border-none">
      <div class="w-72">
        <header class="px-5 py-4 border-b border-surface-200/70 dark:border-surface-200/70 flex items-center gap-3">
          <span
            class="grid size-11 place-items-center rounded-2xl bg-slate-950 text-white dark:bg-white dark:text-slate-950"
          >
            <span class="text-xs font-black uppercase">{{ initials() }}</span>
          </span>
          <div class="min-w-0">
            <p class="text-sm font-bold text-surface-900 truncate">
              {{ displayName() }}
            </p>
            <p class="text-xs text-surface-500 dark:text-surface-400 truncate">
              {{ email() }}
            </p>
          </div>
        </header>

        <section class="px-3 py-3">
          <p class="admin-section-label text-surface-500 px-2 mb-2">Motyw</p>
          <div class="grid grid-cols-3 gap-1.5">
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

        @if (pushVisible()) {
          <section class="px-3 pb-1">
            <p class="admin-section-label text-surface-500 px-2 mb-2">Powiadomienia</p>
            <button
              type="button"
              (click)="togglePush()"
              [disabled]="push.busy()"
              class="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-semibold text-surface-700 hover:bg-surface-100/70 dark:hover:bg-surface-100/40 transition-colors disabled:opacity-60"
            >
              <i class="pi pi-bell text-base"></i>
              {{ push.subscribed() ? 'Wyłącz na tym urządzeniu' : 'Włącz na tym urządzeniu' }}
            </button>
          </section>
        }

        <div class="border-t border-surface-200/70 dark:border-surface-200/70 px-2 py-2">
          <button
            type="button"
            (click)="goAccount()"
            class="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-semibold text-surface-700 hover:bg-surface-100/70 dark:hover:bg-surface-100/40 transition-colors"
          >
            <i class="pi pi-user text-base"></i>
            Moje konto
          </button>
          <button
            type="button"
            (click)="goSettings()"
            class="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-semibold text-surface-700 hover:bg-surface-100/70 dark:hover:bg-surface-100/40 transition-colors"
          >
            <i class="pi pi-cog text-base"></i>
            Ustawienia
          </button>
          <button
            type="button"
            (click)="goFeedback()"
            class="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-semibold text-surface-700 hover:bg-surface-100/70 dark:hover:bg-surface-100/40 transition-colors"
          >
            <i class="pi pi-comment text-base"></i>
            Zgłoś problem
          </button>
          <button
            type="button"
            (click)="logout()"
            class="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-semibold text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-950/30 transition-colors"
          >
            <i class="pi pi-sign-out text-base"></i>
            Wyloguj
          </button>
        </div>
      </div>
    </p-popover>
  `,
})
export class UserAvatarMenuComponent {
  private readonly auth = inject(AuthSessionService);
  private readonly themeMode = inject(ThemeModeService);
  private readonly router = inject(Router);
  protected readonly push = inject(PushNotificationsService);

  protected readonly popover = viewChild.required<Popover>('pop');

  // Pokazujemy przełącznik gdy push jest wspierany albo gdy iOS wymaga instalacji (wtedy klik
  // pokazuje instrukcję „Dodaj do ekranu głównego"). W dev (SW wyłączony) — ukryte.
  protected readonly pushVisible = computed(
    () => this.push.supported() || this.push.iosNeedsInstall(),
  );

  readonly modes: { label: string; value: ThemeMode; icon: string }[] = [
    { label: 'Jasny', value: 'light', icon: 'pi pi-sun' },
    { label: 'Ciemny', value: 'dark', icon: 'pi pi-moon' },
    { label: 'Auto', value: 'system', icon: 'pi pi-desktop' },
  ];

  readonly mode = this.themeMode.mode;

  readonly displayName = computed(() => {
    const session = this.auth.session();
    return session?.displayName ?? session?.email ?? 'Użytkownik';
  });

  readonly email = computed(() => this.auth.session()?.email ?? '');

  readonly initials = computed(() => {
    const name = this.displayName();
    if (!name) return '?';
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
    return name.slice(0, 2).toUpperCase();
  });

  setMode(value: ThemeMode): void {
    this.themeMode.setMode(value);
  }

  togglePush(): void {
    if (this.push.subscribed()) {
      void this.push.disable();
    } else {
      void this.push.enable();
    }
  }

  goAccount(): void {
    void this.router.navigateByUrl('/admin/account');
    this.popover().hide();
  }

  goSettings(): void {
    void this.router.navigateByUrl('/admin/settings');
    this.popover().hide();
  }

  goFeedback(): void {
    void this.router.navigateByUrl('/admin/feedback');
    this.popover().hide();
  }

  logout(): void {
    this.popover().hide();
    this.auth.logout();
  }
}
