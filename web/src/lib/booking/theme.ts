/**
 * Theming kalendarza kolorem salonu.
 *
 * Paleta `brand` (global.css, Tailwind v4 @theme) jest sterowana JEDNĄ zmienną `--brand-h` (hue
 * w OKLCH) — jasność i chroma per stopień są stałe. Dzięki temu z dowolnego koloru salonu bierzemy
 * tylko HUE i wyprowadzamy całą, gwarantowanie czytelną paletę (light + dark) bez ryzyka kontrastu.
 */

/** Konwersja hex `#RRGGBB` → OKLCH {L (0..1), C, H (stopnie 0..360)}. `null` dla nieprawidłowego. */
export function hexToOklch(hex: string): { L: number; C: number; H: number } | null {
  const m = /^#?([0-9a-fA-F]{6})$/.exec(hex.trim());
  if (!m) return null;
  const int = parseInt(m[1], 16);
  const r = ((int >> 16) & 255) / 255;
  const g = ((int >> 8) & 255) / 255;
  const b = (int & 255) / 255;

  const lin = (c: number) =>
    c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  const rl = lin(r);
  const gl = lin(g);
  const bl = lin(b);

  // linear sRGB → LMS (OKLab)
  const l = 0.4122214708 * rl + 0.5363325363 * gl + 0.0514459929 * bl;
  const mm = 0.2119034982 * rl + 0.6806995451 * gl + 0.1073969566 * bl;
  const s = 0.0883024619 * rl + 0.2817188376 * gl + 0.6299787005 * bl;
  const l_ = Math.cbrt(l);
  const m_ = Math.cbrt(mm);
  const s_ = Math.cbrt(s);
  const L = 0.2104542553 * l_ + 0.793617785 * m_ - 0.0040720468 * s_;
  const oa = 1.9779984951 * l_ - 2.428592205 * m_ + 0.4505937099 * s_;
  const ob = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.808675766 * s_;

  const C = Math.sqrt(oa * oa + ob * ob);
  let H = (Math.atan2(ob, oa) * 180) / Math.PI;
  if (H < 0) H += 360;
  return { L, C, H };
}

/** Hue OKLCH [0,360). `null` dla nieprawidłowego/achromatycznego koloru (zostawiamy motyw domyślny). */
export function hexToOklchHue(hex: string): number | null {
  const ok = hexToOklch(hex);
  if (!ok || ok.C < 1e-4) return null;
  return Math.round(ok.H * 10) / 10;
}

/** Relatywna luminancja WCAG koloru hex (0=czarny, 1=biały). `null` dla nieprawidłowego. */
export function relativeLuminance(hex: string): number | null {
  const m = /^#?([0-9a-fA-F]{6})$/.exec(hex.trim());
  if (!m) return null;
  const int = parseInt(m[1], 16);
  const lin = (c: number) =>
    c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  const r = lin(((int >> 16) & 255) / 255);
  const g = lin(((int >> 8) & 255) / 255);
  const b = lin((int & 255) / 255);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function setVar(name: string, value: string | null | undefined): void {
  const root = document.documentElement;
  if (value && /^#?[0-9a-fA-F]{6}$/.test(value.trim())) {
    const v = value.trim();
    root.style.setProperty(name, v.startsWith("#") ? v : `#${v}`);
  } else {
    root.style.removeProperty(name);
  }
}

export interface BookingThemeColors {
  /** Akcent (przyciski, zaznaczenia) — bierzemy z niego HUE palety brand. */
  accent?: string | null;
  /** Tło strony (kanwa wokół karty). */
  background?: string | null;
  /** Tło karty/paneli — wg jego jasności dobieramy jasny/ciemny schemat tekstu. */
  surface?: string | null;
  /** Kolor tekstu cen. */
  price?: string | null;
}

// Próg luminancji, poniżej którego na danym tle czytelniejszy jest jasny tekst (heurystyka WCAG ~0.18).
const DARK_SURFACE_LUMINANCE = 0.18;

/**
 * Stosuje motyw kalendarza salonu przez zmienne CSS na `<html>` (inline → wygrywa z `:root`/`.dark`).
 * Dla tła karty dodatkowo przełącza schemat jasny/ciemny tekstu wg jego jasności — reużywamy
 * dopracowanych stylów `dark:` zamiast nadpisywać każdy kolor tekstu. Brak wartości → motyw domyślny.
 */
export function applyBookingTheme(theme: BookingThemeColors): void {
  if (typeof document === "undefined") return;
  const root = document.documentElement;

  // Akcent: HUE steruje subtelnymi tintami palety brand, a DOKŁADNY kolor (`--accent`) trafia na
  // solidne wypełnienia (przyciski, zaznaczenia) z auto-dobranym tekstem (`--accent-contrast`).
  // Dzięki temu np. złoto wygląda jak złoto, a nie brąz (stały, ciemny stopień palety).
  const raw = theme.accent?.trim();
  const accentHex =
    raw && /^#?[0-9a-fA-F]{6}$/.test(raw) ? (raw.startsWith("#") ? raw : `#${raw}`) : null;
  const ok = accentHex ? hexToOklch(accentHex) : null;
  if (ok && ok.C >= 1e-4 && accentHex) {
    root.style.setProperty("--brand-h", (Math.round(ok.H * 10) / 10).toString());
    root.style.setProperty("--accent", accentHex);
    root.style.setProperty(
      "--accent-strong",
      `oklch(${(ok.L * 0.88).toFixed(3)} ${ok.C.toFixed(3)} ${ok.H.toFixed(1)})`,
    );
    const lum = relativeLuminance(accentHex) ?? 0;
    root.style.setProperty("--accent-contrast", lum > 0.4 ? "#0a0a0a" : "#ffffff");
  } else {
    for (const v of ["--brand-h", "--accent", "--accent-strong", "--accent-contrast"]) {
      root.style.removeProperty(v);
    }
  }

  setVar("--booking-bg", theme.background);
  setVar("--booking-surface", theme.surface);
  setVar("--booking-price", theme.price);

  // Schemat tekstu dobieramy TYLKO gdy salon ustawił tło karty (inaczej szanujemy wybór usera/systemu).
  const lum = theme.surface ? relativeLuminance(theme.surface) : null;
  if (lum !== null) root.classList.toggle("dark", lum < DARK_SURFACE_LUMINANCE);
}

/** Stary, węższy helper (tylko akcent) — zachowany dla zgodności. */
export function applyBrandColor(hex: string | null | undefined): void {
  applyBookingTheme({ accent: hex });
}
