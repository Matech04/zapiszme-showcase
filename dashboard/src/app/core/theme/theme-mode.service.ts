import { Injectable, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'zapisz.dashboard.theme-mode';

@Injectable({ providedIn: 'root' })
export class ThemeModeService {
  readonly mode = signal<ThemeMode>('light');
  readonly isDark = signal<boolean>(false);

  private mediaListener: ((e: MediaQueryListEvent) => void) | null = null;

  hydrate(): void {
    const stored = this.readStored();
    this.mode.set(stored);
    this.applyMode(stored);
  }

  setMode(mode: ThemeMode): void {
    this.mode.set(mode);
    try {
      globalThis.localStorage?.setItem(STORAGE_KEY, mode);
    } catch {
      /* private mode / quota */
    }
    this.applyMode(mode);
  }

  toggle(): void {
    const next: ThemeMode = this.isDark() ? 'light' : 'dark';
    this.setMode(next);
  }

  private applyMode(mode: ThemeMode): void {
    const html = document.documentElement;
    const prefersDark =
      mode === 'dark' ||
      (mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);

    html.classList.toggle('dark', prefersDark);
    this.isDark.set(prefersDark);

    if (this.mediaListener) {
      window.matchMedia('(prefers-color-scheme: dark)').removeEventListener('change', this.mediaListener);
      this.mediaListener = null;
    }
    if (mode === 'system') {
      this.mediaListener = (e: MediaQueryListEvent): void => {
        html.classList.toggle('dark', e.matches);
        this.isDark.set(e.matches);
      };
      window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', this.mediaListener);
    }
  }

  private readStored(): ThemeMode {
    try {
      const raw = globalThis.localStorage?.getItem(STORAGE_KEY);
      if (raw === 'dark' || raw === 'light' || raw === 'system') {
        return raw;
      }
    } catch {
      /* ignore */
    }
    return 'light';
  }
}
