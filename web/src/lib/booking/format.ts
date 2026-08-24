/**
 * Czyste pomocniki formatowania współdzielone przez kalendarz klienta i jego demo.
 * Wydzielone z BookingPanel, żeby logika była testowalna i jednakowa w obu wariantach.
 */
import type { Money } from "../booking-openapi-client";

export const WEEKDAYS_SHORT_PL = ["Nd", "Pn", "Wt", "Śr", "Cz", "Pt", "So"];
export const MONTHS_PL = [
  "Styczeń",
  "Luty",
  "Marzec",
  "Kwiecień",
  "Maj",
  "Czerwiec",
  "Lipiec",
  "Sierpień",
  "Wrzesień",
  "Październik",
  "Listopad",
  "Grudzień",
];

const MONTHS_GENITIVE_PL = [
  "stycznia",
  "lutego",
  "marca",
  "kwietnia",
  "maja",
  "czerwca",
  "lipca",
  "sierpnia",
  "września",
  "października",
  "listopada",
  "grudnia",
];

export function startOfDay(d: Date): Date {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x;
}

export function toISODate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

export function todayISO(): string {
  return toISODate(startOfDay(new Date()));
}

/** Parsuje "YYYY-MM-DD" do lokalnej daty (bez przesunięć stref jak w `new Date(iso)`). */
export function parseISODate(iso: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!m) return null;
  return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

export function titleFromSlug(slug: string): string {
  return slug
    .split(/[-_]/)
    .filter(Boolean)
    .map((w) => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase())
    .join(" ");
}

/** Etykieta ceny darmowej (amount === 0) jako pojedynczej kwoty. */
export const FREE_LABEL = "Bezpłatnie";

export function formatMoney(p: Money | undefined): string {
  if (!p) return "—";
  if ((p.amount ?? 0) === 0) return FREE_LABEL;
  try {
    return new Intl.NumberFormat("pl-PL", {
      style: "currency",
      currency: p.currency || "PLN",
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(p.amount ?? 0);
  } catch {
    return `${p.amount} ${p.currency}`;
  }
}

/** Surowa kwota walutowa bez podmiany 0 → "Bezpłatnie" (do granic widełek). */
function formatAmountRaw(p: Money | undefined): string {
  if (!p) return "—";
  try {
    return new Intl.NumberFormat("pl-PL", {
      style: "currency",
      currency: p.currency || "PLN",
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(p.amount ?? 0);
  } catch {
    return `${p.amount} ${p.currency}`;
  }
}

/** Sama liczba (bez symbolu waluty), bez groszy — dolna granica kompaktowego zakresu. */
function formatAmountNumber(amount: number): string {
  try {
    return new Intl.NumberFormat("pl-PL", {
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(amount);
  } catch {
    return `${amount}`;
  }
}

/**
 * Cena z opcjonalnymi widełkami w formie KOMPAKTOWEJ: "100–120 zł" gdy `maxAmount` ustawione
 * (i większe od ceny bazowej), w przeciwnym razie pojedyncza cena. Waluta z `price`.
 * Kwoty pokazujemy bez groszy (zaokrąglone do pełnych złotych).
 *
 * Kompaktowy zapis (jedno "zł", myślnik między liczbami) jest ~40% węższy od "100 zł – 120 zł",
 * dzięki czemu mieści się w jednej linii na wąskiej karcie usługi i nie rozjeżdża się w pionie.
 *
 * Pojedyncza cena 0 → "Bezpłatnie". Przy widełkach z dolną granicą 0 pokazujemy
 * surową kwotę ("0–50 zł"), bo zakres nie jest "darmowy".
 */
export function formatMoneyRange(
  price: Money | undefined,
  maxAmount?: number,
): string {
  if (maxAmount == null || price == null) return formatMoney(price);
  if (maxAmount <= (price.amount ?? 0)) return formatMoney(price);
  const base = formatAmountNumber(price.amount ?? 0);
  const max = formatAmountRaw({ amount: maxAmount, currency: price.currency });
  return `${base}–${max}`;
}

/**
 * Czas wykonania: "60–90 min" gdy podano przedział (min i max), w przeciwnym razie
 * pojedyncza wartość planowana "60 min". Czas 0 (usługa-dodatek) → "" — nie pokazujemy
 * zera w kalendarzu klienta. Brak danych → "—".
 */
export function formatDurationRange(
  planned?: number,
  min?: number,
  max?: number,
): string {
  if (min != null && max != null) {
    return min === max ? `${min} min` : `${min}–${max} min`;
  }
  if (planned === 0) return "";
  return planned != null ? `${planned} min` : "—";
}

export function initials(firstName?: string, lastName?: string): string {
  const a = (firstName?.[0] ?? "").toUpperCase();
  const b = (lastName?.[0] ?? "").toUpperCase();
  return a + b || "?";
}

export function normalizeTimeOnly(t: string): string {
  const s = t.trim();
  const parts = s.split(":");
  if (parts.length === 2) {
    const h = parts[0].padStart(2, "0");
    const m = parts[1].padStart(2, "0");
    return `${h}:${m}:00`;
  }
  if (parts.length >= 3) {
    const h = parts[0].padStart(2, "0");
    const m = parts[1].padStart(2, "0");
    const sec = (parts[2].split(".")[0] ?? "00").padStart(2, "0");
    return `${h}:${m}:${sec}`;
  }
  return s;
}

/** "14:00" → minuty od północy (do liczenia godziny zakończenia). */
function timeToMinutes(time: string): number | null {
  const m = /^(\d{1,2}):(\d{2})/.exec(time.trim());
  if (!m) return null;
  return Number(m[1]) * 60 + Number(m[2]);
}

/** Dodaje minuty do "HH:MM" i zwraca "HH:MM" (z zawijaniem przez północ odciętym do 23:59). */
export function addMinutesToTime(time: string, minutes: number): string {
  const base = timeToMinutes(time);
  if (base === null) return time;
  const total = Math.min(base + Math.max(0, minutes), 23 * 60 + 59);
  const h = Math.floor(total / 60);
  const m = total % 60;
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
}

/** "HH:MM"–"HH:MM" dla danego startu i czasu trwania; sam start gdy brak czasu trwania. */
export function formatSlotRange(
  start: string,
  durationMinutes?: number,
): string {
  if (!durationMinutes || durationMinutes <= 0) return start;
  return `${start}–${addMinutesToTime(start, durationMinutes)}`;
}

/** "wt, 20 maja" — krótka data po polsku. */
export function formatDatePl(iso: string): string {
  const d = parseISODate(iso);
  if (!d) return iso;
  return `${WEEKDAYS_SHORT_PL[d.getDay()]}, ${d.getDate()} ${MONTHS_GENITIVE_PL[d.getMonth()]}`;
}

/** "20 maja 2026" — pełna data po polsku. */
export function formatDateLongPl(iso: string): string {
  const d = parseISODate(iso);
  if (!d) return iso;
  return `${d.getDate()} ${MONTHS_GENITIVE_PL[d.getMonth()]} ${d.getFullYear()}`;
}

/** "M:SS" z liczby sekund (licznik blokady slotu). */
export function formatCountdown(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(s / 60);
  const sec = s % 60;
  return `${m}:${String(sec).padStart(2, "0")}`;
}
