import { Injectable, signal } from '@angular/core';
import {
  buildDashboardPrimaryCssVars,
  DASHBOARD_DEFAULT_PRIMARY_HEX,
  DASHBOARD_PRIMARY_CSS_VAR_KEYS,
  DASHBOARD_THEME_STORAGE_KEY,
  normalizePrimaryHex,
} from './dashboard-primary-palette';

@Injectable({
  providedIn: 'root',
})
export class DashboardThemeService {
  /** Zapisany w localStorage kolor (`null` = motyw z presetu aplikacji). */
  readonly savedPrimaryHex = signal<string | null>(null);

  /** Odczyt z localStorage i zastosowanie przy starcie aplikacji. */
  hydrateFromStorage(): void {
    try {
      const raw = globalThis.localStorage?.getItem(DASHBOARD_THEME_STORAGE_KEY) ?? null;
      const hex = raw ? normalizePrimaryHex(raw) : null;
      if (hex) {
        this.savedPrimaryHex.set(hex);
        this.applyVars(hex);
      } else {
        this.savedPrimaryHex.set(null);
        this.clearInlinePrimaryVars();
      }
    } catch {
      this.savedPrimaryHex.set(null);
      this.clearInlinePrimaryVars();
    }
  }

  /** Zapis w localStorage + natychmiastowe zastosowanie. */
  setAndPersistPrimaryHex(input: string): boolean {
    const hex = normalizePrimaryHex(input);
    if (!hex) {
      return false;
    }
    try {
      globalThis.localStorage?.setItem(DASHBOARD_THEME_STORAGE_KEY, hex);
    } catch {
      /* ignore quota / private mode */
    }
    this.savedPrimaryHex.set(hex);
    this.applyVars(hex);
    return true;
  }

  /** Usuń nadpisanie — wraca wygląd z `ThemePreset`. */
  clearCustomPrimary(): void {
    try {
      globalThis.localStorage?.removeItem(DASHBOARD_THEME_STORAGE_KEY);
    } catch {
      /* ignore */
    }
    this.savedPrimaryHex.set(null);
    this.clearInlinePrimaryVars();
  }

  private applyVars(hex: string): void {
    const vars = buildDashboardPrimaryCssVars(hex);
    if (!vars) {
      return;
    }
    const root = document.documentElement;
    for (const [k, v] of Object.entries(vars)) {
      root.style.setProperty(k, v);
    }
  }

  private clearInlinePrimaryVars(): void {
    const root = document.documentElement;
    for (const k of DASHBOARD_PRIMARY_CSS_VAR_KEYS) {
      root.style.removeProperty(k);
    }
  }

  /** Wartość startowa dla pickera (bez zapisu). */
  pickerSeedHex(): string {
    return this.savedPrimaryHex() ?? DASHBOARD_DEFAULT_PRIMARY_HEX;
  }
}
