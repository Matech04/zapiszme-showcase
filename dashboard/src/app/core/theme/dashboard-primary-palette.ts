/** Domyślny akcent zgodny z `ThemePreset` (primary.500). */
export const DASHBOARD_DEFAULT_PRIMARY_HEX = '#f59e0b';

export const DASHBOARD_THEME_STORAGE_KEY = 'booking_saas.dashboard.primary_hex';

const WHITE: [number, number, number] = [255, 255, 255];
const BLACK: [number, number, number] = [15, 23, 42];

function clamp255(n: number): number {
  return Math.max(0, Math.min(255, Math.round(n)));
}

/** Zwraca `#rrggbb` lub `null` przy błędnym wejściu. */
export function normalizePrimaryHex(input: string): string | null {
  let h = input.trim();
  if (!h.startsWith('#')) {
    h = `#${h}`;
  }
  const m = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(h);
  if (!m) {
    return null;
  }
  const v = m[1];
  const expand = (ch: string) => (ch.length === 1 ? ch + ch : ch);
  const full =
    v.length === 3
      ? `${expand(v[0]!)}${expand(v[1]!)}${expand(v[2]!)}`
      : v;
  return `#${full.toLowerCase()}`;
}

function hexToRgb(hex: string): [number, number, number] | null {
  const n = normalizePrimaryHex(hex);
  if (!n) {
    return null;
  }
  const v = n.slice(1);
  return [parseInt(v.slice(0, 2), 16), parseInt(v.slice(2, 4), 16), parseInt(v.slice(4, 6), 16)];
}

function toHex(rgb: [number, number, number]): string {
  return `#${rgb.map((c) => clamp255(c).toString(16).padStart(2, '0')).join('')}`;
}

function mix(a: [number, number, number], b: [number, number, number], t: number): [number, number, number] {
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
}

function relativeLuminance(rgb: [number, number, number]): number {
  const lin = (c: number) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * lin(rgb[0]) + 0.7152 * lin(rgb[1]) + 0.0722 * lin(rgb[2]);
}

/** Kontrast tekstu na tle `rgb` (przyciski / nagłówek). */
function contrastOnPrimary(rgb: [number, number, number]): string {
  const L = relativeLuminance(rgb);
  return L > 0.42 ? '#0f172a' : '#ffffff';
}

/**
 * Zmienne używane przez PrimeNG Aura + tailwindcss-primeui w tym projekcie
 * (skan wygenerowanego CSS).
 */
export function buildDashboardPrimaryCssVars(baseHex: string): Record<string, string> | null {
  const base = hexToRgb(baseHex);
  if (!base) {
    return null;
  }
  /**
   * Gdy użytkownik wybierze bardzo jasny kolor (np. prawie biały), klasy `text-primary`
   * stają się nieczytelne na jasnym tle dashboardu. Delikatnie przyciemniamy bazę.
   */
  const safeBase =
    relativeLuminance(base) > 0.72 ? (mix(base, BLACK, 0.35) as [number, number, number]) : base;
  const p50 = mix(safeBase, WHITE, 0.93);
  const p100 = mix(safeBase, WHITE, 0.8);
  const p300 = mix(safeBase, WHITE, 0.38);
  const p500 = safeBase;
  const p600 = mix(safeBase, BLACK, 0.2);
  const p800 = mix(safeBase, BLACK, 0.48);
  const p900 = mix(safeBase, BLACK, 0.66);
  const contrast = contrastOnPrimary(p500);
  return {
    '--p-primary-50': toHex(p50),
    '--p-primary-100': toHex(p100),
    '--p-primary-300': toHex(p300),
    '--p-primary-500': toHex(p500),
    '--p-primary-600': toHex(p600),
    '--p-primary-800': toHex(p800),
    '--p-primary-900': toHex(p900),
    '--p-primary-color': toHex(p500),
    '--p-primary-contrast-color': contrast,
  };
}

/** Kolejność do usuwania inline override z `document.documentElement`. */
export const DASHBOARD_PRIMARY_CSS_VAR_KEYS = [
  '--p-primary-50',
  '--p-primary-100',
  '--p-primary-300',
  '--p-primary-500',
  '--p-primary-600',
  '--p-primary-800',
  '--p-primary-900',
  '--p-primary-color',
  '--p-primary-contrast-color',
] as const;
