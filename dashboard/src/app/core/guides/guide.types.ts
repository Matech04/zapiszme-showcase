import { UserRole } from '@core/models/navigation.model';

/**
 * Typy przewodników zadaniowych („Ustawmy grafik powtarzalny"), które zastąpiły dawne toury
 * opisujące ekrany („Kalendarz wizyt").
 *
 * Różnica jest gatunkowa, nie kosmetyczna: opis ekranu starzeje się przy każdym redesignie
 * i nie zostawia użytkownikowi niczego. Przewodnik zadaniowy prowadzi przez PRAWDZIWY
 * formularz i kończy się zrobioną rzeczą — zapisanym grafikiem, dodaną usługą.
 */

export type TourSide = 'top' | 'right' | 'bottom' | 'left';
export type TourAlign = 'start' | 'center' | 'end';

/**
 * Rodzaj kroku decyduje o tym, CO przesuwa przewodnik dalej:
 *  - `explain` — narracja; przesuwa przycisk „Dalej".
 *  - `action`  — zadanie; przycisku „Dalej" NIE MA, krok czeka aż użytkownik faktycznie
 *                wykona akcję opisaną w `advanceOn`.
 *  - `outro`   — domknięcie; przycisk „Gotowe".
 */
export type GuideStepKind = 'explain' | 'action' | 'outro';

/** Zdarzenie, które zalicza krok `action`. */
export type GuideAdvanceTrigger =
  /** Kliknięcie w kotwicę kroku (domyślnie `element`, opcjonalnie inny selektor). */
  | { on: 'click'; selector?: string }
  /** Pojawienie się elementu — szuflada, formularz, panel. */
  | { on: 'appear'; selector: string }
  /**
   * Zniknięcie elementu. Sygnał „udało się" dla formularzy w szufladzie: panel zamyka się
   * dopiero po udanym zapisie, więc sam klik w „Zapisz" nie odróżniłby sukcesu od błędu
   * walidacji.
   */
  | { on: 'disappear'; selector: string }
  /** Router wylądował pod wskazaną trasą (może zawierać token `:me`). */
  | { on: 'route'; route: string };

export interface GuideStep {
  /** Domyślnie `explain`. */
  kind?: GuideStepKind;

  /**
   * Selektor CSS elementu do podświetlenia. Pomiń dla kroku narracyjnego —
   * popover pokaże się na środku ekranu bez highlightu.
   */
  element?: string;

  /**
   * Dokąd nawigować ZANIM pokażemy ten krok (np. '/admin/services'). Może zawierać token
   * `:me`, podmieniany na id pracownika zalogowanego użytkownika. Jeśli aplikacja jest już
   * pod tą trasą — nawigacja jest pomijana.
   */
  route?: string;

  /**
   * Co zrobić, gdy `element` jest nieobecny lub niewidoczny. Dotyczy WYŁĄCZNIE kroków
   * `explain`/`outro`:
   *  - brak/`false` (domyślnie) — krok jest pomijany,
   *  - `true` — krok zostaje, ale bez highlightu (popover na środku).
   *
   * Krok `action` nie korzysta z tej flagi: nie da się wykonać zadania na nieistniejącym
   * przycisku, więc brak kotwicy przerywa przewodnik jawnym komunikatem, zamiast po cichu
   * przeskoczyć zadanie. Ciche pomijanie było przyczyną, dla której poprzednie przewodniki
   * gniły niezauważone.
   */
  keepWhenMissing?: boolean;

  /** Wymagane dla `kind: 'action'` — bez tego krok nie miałby jak się zakończyć. */
  advanceOn?: GuideAdvanceTrigger;

  popover: {
    title: string;
    description: string;
    side?: TourSide;
    align?: TourAlign;
  };
}

/** Kategorie w katalogu `/admin/guides`. */
export type GuideCategory = 'booking' | 'availability' | 'offer' | 'clients' | 'money' | 'team';

/**
 * Kolejność kluczy jest kolejnością sekcji w katalogu — układa się w ścieżkę salonu PO kreatorze:
 * najpierw wpuść klientów (link, zasady), potem dopracuj dostępność i ofertę, dalej codzienna
 * obsługa, na końcu pieniądze i zespół.
 */
export const GUIDE_CATEGORY_LABELS: Record<GuideCategory, string> = {
  booking: 'Rezerwacje online',
  availability: 'Twoja dostępność',
  offer: 'Oferta i cennik',
  clients: 'Klienci i wizyty',
  money: 'Płatności i przypomnienia',
  team: 'Zespół',
};

export interface GuideDef {
  /**
   * Stabilny identyfikator (kebab-case). Trafia do bazy jako `GuideId` — zmiana po wydaniu
   * kasuje „ukończono" wszystkim użytkownikom. Format musi przejść walidator backendu
   * (`GuideIdRules.Pattern`).
   */
  id: string;

  /** Nagłówek kafelka w katalogu — zdanie w trybie „zróbmy to": „Ustawmy grafik powtarzalny". */
  title: string;

  /** Jedno zdanie: co użytkownik BĘDZIE MIAŁ po ukończeniu. */
  summary: string;

  category: GuideCategory;

  /**
   * Role, które widzą i mogą uruchomić ten przewodnik. Nie ozdobnik — pracownik nie ma dostępu
   * do katalogu usług, więc „Dodajmy usługę" zaprowadziłby go pod trasę, z której guard
   * i tak by go odbił.
   */
  roles: UserRole[];

  /** Ikona PrimeIcons na kafelku. */
  icon: string;

  /** Trasa, na której przewodnik się zaczyna. Może zawierać token `:me`. */
  entryRoute: string;

  steps: GuideStep[];
}

/** Token w trasach podmieniany na id pracownika zalogowanego użytkownika. */
export const GUIDE_ME_TOKEN = ':me';
