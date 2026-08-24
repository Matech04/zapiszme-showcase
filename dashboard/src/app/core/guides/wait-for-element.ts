/**
 * Czeka aż element pasujący do selektora pojawi się w DOM (max `timeoutMs`).
 *
 * Używane przy krokach cross-route: po `router.navigateByUrl(...)` strona
 * (lazy-loaded) renderuje się asynchronicznie, więc element kroku nie istnieje
 * od razu. Zwraca element gdy się pojawi, albo `null` po timeoucie — wtedy
 * `GuideService` pokazuje krok bez highlightu (fallback), zamiast się wywalić.
 */
/**
 * Czy konkretny element jest realnie widoczny (w DOM, niezerowy rozmiar w OBU
 * wymiarach, nie ukryty przez `display:none` / `visibility:hidden`).
 *
 * Uwaga na wymiary: pusty element blokowy ma szerokość = pełny kontener, ale
 * wysokość 0 (np. host `<app-first-booking-card>` po pierwszej rezerwacji).
 * Dlatego warunek to „którykolwiek wymiar zerowy ⇒ niewidoczny" (OR), nie AND.
 */
export function isVisible(el: Element | null | undefined): boolean {
  if (!el || !el.isConnected) return false;

  const rect = el.getBoundingClientRect();
  if (rect.width === 0 || rect.height === 0) return false;

  const style = getComputedStyle(el);
  return style.display !== 'none' && style.visibility !== 'hidden';
}

/**
 * Zwraca pierwszy WIDOCZNY element pasujący do selektora, albo `null`. Pozwala
 * otagować kilka wariantów tego samego celu (np. przycisk „Dodaj wizytę" osobny
 * per widok/viewport) jednym `data-tour` i wskazać ten aktualnie widoczny.
 */
export function pickVisibleElement(selector: string): HTMLElement | null {
  const matches = document.querySelectorAll<HTMLElement>(selector);
  for (const el of Array.from(matches)) {
    if (isVisible(el)) return el;
  }
  return null;
}

/** Czy istnieje choć jeden widoczny element pasujący do selektora. */
export function isElementVisible(selector: string): boolean {
  return pickVisibleElement(selector) !== null;
}

export function waitForElement(selector: string, timeoutMs = 3000): Promise<HTMLElement | null> {
  const existing = document.querySelector<HTMLElement>(selector);
  if (existing) return Promise.resolve(existing);

  return new Promise<HTMLElement | null>((resolve) => {
    let settled = false;

    const finish = (el: HTMLElement | null) => {
      if (settled) return;
      settled = true;
      observer.disconnect();
      clearTimeout(timer);
      resolve(el);
    };

    const observer = new MutationObserver(() => {
      const el = document.querySelector<HTMLElement>(selector);
      if (el) finish(el);
    });
    observer.observe(document.body, { childList: true, subtree: true });

    const timer = setTimeout(() => finish(document.querySelector<HTMLElement>(selector)), timeoutMs);
  });
}
