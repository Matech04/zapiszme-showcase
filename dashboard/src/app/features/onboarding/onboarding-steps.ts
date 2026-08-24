/** Kolejność kroków kreatora — steruje paskiem postępu i przyciskiem „Wstecz". */
export interface WizardStepDef {
  readonly path: string;
  readonly label: string;
}

/**
 * Ścieżka jest STAŁA dla każdego właściciela — także dla tego, który nie prowadzi grafiku
 * powtarzalnego. Wybór „planuję każdy miesiąc osobno" nie usuwa kroku „Jak układasz grafik?", tylko
 * zwija na nim formularz dni i godzin (pytanie stoi nad swoim skutkiem, na jednym ekranie).
 * Dzięki temu licznik nie musi być warunkowy.
 *
 * „Zapisy" stoją PRZED parą „Terminy + Godziny", choć z nią nie sąsiadują tematycznie. Powód jest
 * kierunkowy: grafik ma być ostatnią rzeczą w kreatorze, bo z niego wychodzi się prosto do
 * aplikacji — docelowo w interaktywny przewodnik po prawdziwym ekranie grafiku. Pytanie o
 * potwierdzanie wizyt zadane po tym wyjściu przerywałoby tę ciągłość.
 *
 * Konsekwencja: domknięcie onboardingu (`complete()`) przeniosło się z „Zapisów" na „Jak
 * układasz grafik?" — patrz `schedule-step.component.ts`. Krok, który woła `complete()`, MUSI być
 * ostatnim treściowym, bo `setupGuard` odsyła ukończonego właściciela z `/setup/**` do panelu.
 */
export const ONBOARDING_STEPS: readonly WizardStepDef[] = [
  { path: 'profile', label: 'Dane' },
  { path: 'salon', label: 'Salon' },
  { path: 'industry', label: 'Branża' },
  { path: 'services', label: 'Usługi' },
  { path: 'rules', label: 'Zapisy' },
  { path: 'slot-mode', label: 'Terminy' },
  { path: 'schedule', label: 'Godziny' },
  { path: 'done', label: 'Gotowe' },
];

export function stepIndex(path: string): number {
  return ONBOARDING_STEPS.findIndex((s) => s.path === path);
}

/** Trasa poprzedniego kroku (albo `null`, gdy to pierwszy krok / nieznany). */
export function previousStepRoute(path: string): string | null {
  const i = stepIndex(path);
  if (i <= 0) {
    return null;
  }
  return `/setup/${ONBOARDING_STEPS[i - 1].path}`;
}
