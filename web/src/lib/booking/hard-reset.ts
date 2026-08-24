/**
 * „Spróbuj ponownie" po awarii = twardy reset klienta rezerwacji.
 *
 * Zwykły F5 nie wystarcza w dwóch realnych sytuacjach: (1) w localStorage siedzi hold wskazujący
 * na wizytę, której serwer już nie zna, i front przy starcie odtwarza zepsuty stan; (2) przeglądarka
 * trzyma stary dokument/bundle z poprzedniego deployu. Dlatego czyścimy stan rezerwacji, Cache API
 * i ewentualne Service Workery, a dopiero potem przeładowujemy stronę z parametrem obchodzącym
 * cache dokumentu.
 */

/** Prefiks wszystkich naszych wpisów w storage (hold, sesja self-service). */
const STORAGE_PREFIX = "booking_saas:";
/** Klucz holdu — TO on potrafi zostać po awarii i psuć kolejne wejście. */
const HOLD_PREFIX = "booking_saas:hold:";
/** Parametr cache-bust dodawany do URL-a przy przeładowaniu; `BookingEntry` sprząta go po starcie. */
export const RETRY_PARAM = "_retry";

function removeMatchingKeys(storage: Storage, predicate: (key: string) => boolean): void {
  try {
    const doomed: string[] = [];
    for (let i = 0; i < storage.length; i++) {
      const key = storage.key(i);
      if (key && predicate(key)) doomed.push(key);
    }
    for (const key of doomed) storage.removeItem(key);
  } catch {
    /* private mode / zablokowany storage — nie ma czego czyścić */
  }
}

/**
 * Kasuje lokalny stan rezerwacji.
 *
 * Sesję zweryfikowanego kontaktu (`booking_saas:selfservice:*`) zostawiamy CELOWO: jej skasowanie
 * wymusiłoby kolejny kod OTP, czyli realnie płatnego SMS-a, a token i tak jest weryfikowany
 * i wygaszany po stronie serwera. Awarię powoduje zwykle zawieszony hold, nie ta sesja.
 */
export function clearBookingLocalState(): void {
  if (typeof window === "undefined") return;
  removeMatchingKeys(window.localStorage, (k) => k.startsWith(STORAGE_PREFIX));
  removeMatchingKeys(window.sessionStorage, (k) => k.startsWith(HOLD_PREFIX));
}

/** Kasuje Cache API i wyrejestrowuje Service Workery (obrona przed starym buildem w cache). */
export async function clearBrowserCaches(): Promise<void> {
  if (typeof window === "undefined") return;
  try {
    if ("caches" in window) {
      const keys = await window.caches.keys();
      await Promise.all(keys.map((k) => window.caches.delete(k)));
    }
  } catch {
    /* brak uprawnień / niewspierane — nie blokujemy resetu */
  }
  try {
    if ("serviceWorker" in navigator) {
      const registrations = await navigator.serviceWorker.getRegistrations();
      await Promise.all(registrations.map((r) => r.unregister()));
    }
  } catch {
    /* jw. */
  }
}

/** Adres do przeładowania: ten sam widok, ale ze świeżym parametrem obchodzącym cache dokumentu. */
export function buildRetryUrl(href: string, stamp: number = Date.now()): string {
  const url = new URL(href);
  url.searchParams.set(RETRY_PARAM, String(stamp));
  return url.toString();
}

/**
 * Pełny reset: czyści stan i cache, po czym przeładowuje stronę.
 * `replace` (nie `assign`) — kolejne kliknięcia „Spróbuj ponownie" nie zapychają historii wstecz.
 */
export async function hardResetBookingApp(): Promise<void> {
  if (typeof window === "undefined") return;
  clearBookingLocalState();
  await clearBrowserCaches();
  window.location.replace(buildRetryUrl(window.location.href));
}

/** Usuwa `_retry` z paska adresu po udanym starcie — użytkownik nie musi go oglądać. */
export function stripRetryParam(): void {
  if (typeof window === "undefined") return;
  try {
    const url = new URL(window.location.href);
    if (!url.searchParams.has(RETRY_PARAM)) return;
    url.searchParams.delete(RETRY_PARAM);
    window.history.replaceState({}, "", url.toString());
  } catch {
    /* ignore */
  }
}
