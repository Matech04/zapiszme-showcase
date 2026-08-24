import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';

export const ZapiszMeTheme = definePreset(Aura, {
  semantic: {
    primary: {
      // Accent inspirowany landing page (ciepły amber).
      50: '#fff8eb',
      100: '#feefc7',
      200: '#fde08a',
      300: '#fccc4b',
      400: '#fbb124',
      500: '#f59e0b',
      600: '#d97706',
      700: '#b45309',
      800: '#92400e',
      900: '#78350f',
      950: '#451a03'
    },
    colorScheme: {
      light: {
        surface: {
          0: '#ffffff',
          50: '#f8f3ec',
          100: '#f1f5f9',
          200: '#e2e8f0',
          300: '#cbd5e1',
          400: '#94a3b8',
          500: '#64748b',
          600: '#475569',
          700: '#334155',
          800: '#1e293b',
          900: '#0f172a',
          950: '#020617'
        }
      },
      // Ciepła, fioletowa rampa w dark — spójna z public app (web). Wartości
      // muszą się zgadzać z `.dark { --p-surface-* }` w styles.css. Skala
      // odwrócona: surface-0 = baza (najciemniej), surface-900 = tekst (biel).
      dark: {
        surface: {
          0: '#181427',
          50: '#211b36',
          100: '#2a2342',
          200: '#3a3357',
          // surface-300/400 = muted-text tier (text-surface-300/400 ~280×).
          // Rozjaśnione do WCAG AA 4.5:1 na karcie (300=4.72, 400=5.51).
          300: '#8c84ad',
          400: '#9990b6',
          500: '#a59cc0',
          600: '#c5bedb',
          700: '#ded9ec',
          800: '#f1eef8',
          900: '#ffffff',
          950: '#0f0b1a'
        },
        // Akcent w dark = fiolet marki (#7C3AED = violet-600), tak jak web.
        // W light pozostaje amber (primary.500). Bazowy odcień = violet-600:
        // biały tekst na przycisku = 5.70:1 (AA), hover rozjaśnia (violet-500).
        primary: {
          color: '#7c3aed',
          contrastColor: '#ffffff',
          hoverColor: '#8b5cf6',
          activeColor: '#6d28d9'
        },
        // Pola formularzy PrimeNG (input/select/textarea/datepicker): Aura mapuje
        // kolor tekstu na {surface.0}, który w odwróconej rampie jest CIEMNY →
        // ciemny tekst na ciemnym polu. Wymuszamy jasny tekst (surface.900 = biel).
        formField: {
          color: '{surface.900}',
          placeholderColor: '{surface.400}',
          iconColor: '{surface.400}',
          disabledColor: '{surface.400}'
        }
        // Tła overlayów/paneli/toastów PrimeNG (białe w odwróconej rampie) są
        // nadpisywane bezpośrednio w styles.css `.dark` — patrz tam (PrimeNG
        // trzyma te tokeny w adopted stylesheet, nadpisanie zmiennej jest pewne).
      }
    }
  }
});