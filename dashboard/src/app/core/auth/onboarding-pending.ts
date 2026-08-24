/**
 * „Porzucona rejestracja" — jedyny stan onboardingu warty trzymania w przeglądarce PRZED
 * utworzeniem tenanta (potem źródłem prawdy jest backend). Trzymamy tylko e-mail i etap;
 * NIGDY hasła ani numeru telefonu (PII na współdzielonym dysku).
 */
export const ONBOARDING_PENDING_KEY = 'zapisz.onboarding.pending';

export type OnboardingPendingStage = 'confirm-email' | 'confirm-phone';

export interface OnboardingPending {
  email: string;
  stage: OnboardingPendingStage;
}

export function setOnboardingPending(pending: OnboardingPending): void {
  try {
    globalThis.localStorage?.setItem(ONBOARDING_PENDING_KEY, JSON.stringify(pending));
  } catch {
    /* storage niedostępny */
  }
}

export function readOnboardingPending(): OnboardingPending | null {
  try {
    const raw = globalThis.localStorage?.getItem(ONBOARDING_PENDING_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as Partial<OnboardingPending>;
    return parsed.email ? { email: parsed.email, stage: parsed.stage ?? 'confirm-email' } : null;
  } catch {
    return null;
  }
}

export function clearOnboardingPending(): void {
  try {
    globalThis.localStorage?.removeItem(ONBOARDING_PENDING_KEY);
  } catch {
    /* ignore */
  }
}

/** `m***@gmail.com` — maskuje lokalną część adresu (zostaje pierwsza litera). */
export function maskEmail(email: string): string {
  const at = email.indexOf('@');
  if (at <= 0) {
    return email;
  }
  const local = email.slice(0, at);
  const domain = email.slice(at);
  const first = local[0] ?? '';
  return `${first}***${domain}`;
}
