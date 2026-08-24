/**
 * Raportowanie awarii publicznego kalendarza.
 *
 * Klientka salonu nigdy nie zgłosi błędu z konsoli przeglądarki — jeśli kalendarz się wywali,
 * po prostu odejdzie. Dlatego każda nierozpoznana awaria (transport, 5xx, crash renderowania)
 * leci fire-and-forget do backendu, gdzie Serilog wypycha ją do Seq razem z IP, User-Agentem
 * i `correlationId`, który klientka widzi na ekranie błędu i może podać przez telefon.
 *
 * Czego NIE raportujemy: przerwanych żądań (`AbortError` — nasza własna zmiana kryteriów)
 * i błędów biznesowych 4xx (zajęty slot, zły kod OTP) — to normalna praca aplikacji.
 */
import { getBookingApiBase } from "../booking-api-browser";
import {
  describeBookingError,
  shouldReportKind,
  type BookingErrorDescription,
} from "../booking-api-errors";

/** Trasa CELOWO poza `api/booking/{slug}/…` — inaczej middleware tenanta zwróciłby 404 dla złego slugu. */
const REPORT_PATH = "/api/booking-diagnostics/client-error";

/**
 * Adres odbiornika raportów — w przeglądarce CELOWO same-origin (ścieżka względna).
 *
 * Raport o awarii nie może jechać tym samym kanałem cross-origin, który właśnie padł. Gdy kalendarz
 * przestaje działać przez CORS (host serwujący SPA poza `Cors:AllowedOrigins` — dokładnie to zrobiło
 * `www.zapisz.me`), beacon na `api.<domena>` jest blokowany tym samym mechanizmem co żądanie, o którym
 * miał donieść. Awaria znikała z logów akurat wtedy, gdy była najbardziej potrzebna.
 *
 * Caddy proxuje `/api/booking-diagnostics/*` na API na KAŻDYM hoście serwującym booking (zapisz.me
 * i rezerwacja.<domena>), więc ścieżka względna trafia gdzie trzeba — bez preflightu i bez CORS-a.
 * Jeśli kiedyś dojdzie kolejny host serwujący kalendarz, musi dostać tę samą trasę w caddyfile.
 *
 * Dev/test: pod `astro dev` (:4321) nie ma takiego proxy, więc lecimy pełnym adresem API.
 */
function reportUrl(): string {
  if (import.meta.env.PROD && typeof globalThis.window !== "undefined") return REPORT_PATH;
  return `${getBookingApiBase()}${REPORT_PATH}`;
}

/**
 * Błędy WSTRZYKNIĘTE w naszą stronę przez in-app browsery (Instagram, Facebook, WebView Androida) —
 * nie pochodzą z naszego kodu i niczego nie psują: `window.onerror` w `BookingEntry.svelte` tylko je
 * loguje, nie pokazuje ekranu błędu.
 *
 * W sierpniu 2026 stanowiły 39 z 45 raportów. Zjadały budżet `MAX_REPORTS_PER_SESSION` i zagłuszały
 * realne awarie — po nich w logu nie dało się zobaczyć, że kalendarz komuś naprawdę nie wstał.
 * Zostają w konsoli (do debugowania na miejscu), ale nie idą do backendu.
 *
 * Lista jest świadomie wąska i oparta na zaobserwowanym ruchu: filtrujemy WYŁĄCZNIE sygnatury cudzych
 * mostków JS-natywnych, których nasz kod nigdy nie dotyka. Nie dopisuj tu wzorców „na wszelki wypadek" —
 * każdy kolejny wpis to potencjalnie zagłuszony własny błąd.
 */
const THIRD_PARTY_NOISE = [
  // Instagram/Facebook in-app (iOS) — autouzupełnianie wstrzykiwane do strony.
  "_AutofillCallbackHandler",
  // Ten sam rodzic: mostek do natywnej apki przez WKWebView.
  "window.webkit.messageHandlers",
  // WebView Androida — mostek zerwany, gdy użytkownik przełącza apkę.
  "Java object is gone",
];

/** Czy to szum wstrzyknięty przez in-app browser, a nie awaria naszego kalendarza. */
export function isThirdPartyNoise(technical: string): boolean {
  return THIRD_PARTY_NOISE.some((pattern) => technical.includes(pattern));
}

/** Twardy limit na kartę — awaria w pętli (np. efekt Svelte) nie zamieni się w flood do Seq. */
const MAX_REPORTS_PER_SESSION = 20;
/** Ten sam błąd tej samej operacji raportujemy raz na minutę. */
const DEDUPE_WINDOW_MS = 60_000;
/** Limity długości pól — backend i tak przycina, ale nie ma po co wysyłać megabajtów. */
const MAX_TEXT = 500;
const MAX_URL = 300;

let sessionCorrelationId: string | null = null;
let reportsSent = 0;
const recentReports = new Map<string, number>();
/** Gdy samo raportowanie nie działa (padł endpoint / brak sieci), przestajemy próbować. */
let reportingDisabled = false;

function randomId(): string {
  const c = globalThis.crypto;
  if (c && typeof c.randomUUID === "function") return c.randomUUID();
  if (c && typeof c.getRandomValues === "function") {
    const buf = new Uint8Array(16);
    c.getRandomValues(buf);
    return [...buf].map((b) => b.toString(16).padStart(2, "0")).join("");
  }
  // Ostatnia deska ratunku — identyfikator ma być czytelny w logu, nie kryptograficzny.
  return `nc-${Date.now().toString(36)}`;
}

/**
 * Identyfikator tej karty przeglądarki. Pokazujemy go na ekranie błędu i wysyłamy w raporcie —
 * po nim znajdujesz w Seq dokładnie to zgłoszenie („mam błąd, kod 7f3a…").
 */
export function bookingCorrelationId(): string {
  if (!sessionCorrelationId) sessionCorrelationId = randomId();
  return sessionCorrelationId;
}

/** Krótka forma dla klientki — pełny UUID przez telefon nikt nie podyktuje. */
export function shortCorrelationId(): string {
  return bookingCorrelationId().replace(/-/g, "").slice(0, 8);
}

function truncate(value: string, max: number): string {
  // Log forging: CR/LF w polu tekstowym potrafi udawać nową linię logu. Backend też to robi,
  // ale taniej uciąć u źródła.
  const flat = value.replace(/[\r\n\t]+/g, " ").trim();
  return flat.length > max ? `${flat.slice(0, max)}…` : flat;
}

export interface BookingErrorContext {
  /** Operacja, która padła: `loadSalon`, `loadSlots`, `createHold`, `render`, `window.onerror`… */
  operation: string;
  salonSlug?: string | null;
  /** Dodatkowy kontekst diagnostyczny (bez danych osobowych!). */
  extra?: Record<string, string | number | boolean | null | undefined>;
}

function currentUrl(): string {
  if (typeof window === "undefined") return "";
  return truncate(window.location.href, MAX_URL);
}

function sendBeacon(payload: Record<string, unknown>): void {
  if (reportingDisabled) return;
  if (typeof fetch !== "function") return;
  const url = reportUrl();
  try {
    void fetch(url, {
      method: "POST",
      // `keepalive` — raport ma dolecieć nawet, gdy klientka zamyka kartę po błędzie.
      keepalive: true,
      // Bez ciasteczek: to log techniczny, nie operacja na sesji rezerwacji.
      credentials: "omit",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    }).catch(() => {
      // Nie raportujemy awarii raportowania — to prosta droga do pętli.
      reportingDisabled = true;
    });
  } catch {
    reportingDisabled = true;
  }
}

/**
 * Loguje awarię (konsola zawsze, backend gdy warto) i zwraca gotowy opis dla UI.
 * Zwracany opis zawiera przyjazny komunikat — nigdy surowej treści wyjątku.
 */
export function reportBookingError(
  error: unknown,
  context: BookingErrorContext,
): BookingErrorDescription {
  const description = describeBookingError(error);
  if (description.kind === "aborted") return description;

  const correlationId = bookingCorrelationId();

  // Konsola: pełny obiekt błędu (stack trace) — dla nas przy debugowaniu na miejscu.
  console.error(
    `[booking] ${context.operation} — ${description.kind}`,
    {
      correlationId,
      salonSlug: context.salonSlug ?? null,
      status: description.status,
      technical: description.technical,
      ...(context.extra ?? {}),
    },
    error,
  );

  if (!shouldReportKind(description.kind)) return description;
  // Szum cudzych mostków JS-natywnych. Odsiewamy PRZED limitem i dedupe, żeby nie zużywał
  // budżetu raportów — inaczej jedna karta w in-app Instagrama potrafiła wyczerpać limit
  // na błędach, których nawet nie wywołaliśmy, i zagłuszyć realną awarię kalendarza.
  if (isThirdPartyNoise(description.technical)) return description;
  if (reportsSent >= MAX_REPORTS_PER_SESSION) return description;

  const dedupeKey = `${context.operation}|${description.kind}|${description.status ?? ""}|${description.technical}`;
  const now = Date.now();
  const previous = recentReports.get(dedupeKey);
  if (previous !== undefined && now - previous < DEDUPE_WINDOW_MS) return description;
  recentReports.set(dedupeKey, now);
  reportsSent += 1;

  sendBeacon({
    correlationId,
    salonSlug: context.salonSlug ? truncate(context.salonSlug, 100) : null,
    operation: truncate(context.operation, 100),
    kind: description.kind,
    message: truncate(description.technical, MAX_TEXT),
    status: description.status,
    pageUrl: currentUrl(),
    context: context.extra ? truncate(JSON.stringify(context.extra), MAX_TEXT) : null,
  });

  return description;
}

/** Reset stanu modułu — wyłącznie dla testów (limity i dedupe są per karta przeglądarki). */
export function __resetBookingErrorReporting(): void {
  sessionCorrelationId = null;
  reportsSent = 0;
  recentReports.clear();
  reportingDisabled = false;
}
