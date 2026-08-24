/**
 * Kontrakt i rejestr przełączalnych wariantów WYBORU DATY.
 *
 * Wybór daty jest komponentowy — salon będzie mógł w przyszłości wybrać typ kalendarza
 * w dashboardzie (pole w `PublicBookingSalonInfoDto`, zmapowane na `DatePickerVariant`).
 * Dodanie kolejnego wariantu = nowy komponent + jeden wpis w `AvailabilitySection`.
 */
import type { DayChip } from "../../../../lib/booking/availability";

/** Konkretny wariant renderowany przez `AvailabilitySection`. */
export type DatePickerVariant = "strip" | "grid" | "list";

/**
 * Ustawienie wyboru daty (prop/DTO/query). `responsive` to meta-wariant: rozwiązuje się
 * po szerokości ekranu — pasek na mobile, siatka na desktopie.
 */
export type DatePickerSetting = DatePickerVariant | "responsive";

/** Domyślne ustawienie: pasek (mobile) / siatka (desktop). */
export const DEFAULT_DATE_PICKER_SETTING: DatePickerSetting = "responsive";

/** Próg „desktop" dla wariantu responsywnego (Tailwind md). */
export const DATE_PICKER_DESKTOP_QUERY = "(min-width: 768px)";

/** Bezpieczne sparsowanie wartości z query/DTO do dozwolonego ustawienia. */
export function parseDatePickerSetting(
  value: string | null | undefined,
): DatePickerSetting {
  return value === "grid" ||
    value === "list" ||
    value === "strip" ||
    value === "responsive"
    ? value
    : DEFAULT_DATE_PICKER_SETTING;
}

/** Rozwiązuje ustawienie do konkretnego wariantu (responsive → strip/grid wg szerokości). */
export function resolveDatePickerVariant(
  setting: DatePickerSetting,
  isDesktop: boolean,
): DatePickerVariant {
  if (setting === "responsive") return isDesktop ? "grid" : "strip";
  return setting;
}

/**
 * Wspólne propsy wariantów wyboru samej DATY (`strip`, `grid`). Wariant `list`
 * łączy datę i godzinę, więc ma szerszy kontrakt obsługiwany w `AvailabilitySection`.
 */
export interface DatePickerProps {
  monthTitle: string;
  days: DayChip[];
  selectedDate: string;
  locked?: boolean;
  showLegend?: boolean;
  canGoPrev?: boolean;
  canGoNext?: boolean;
  onprev: () => void;
  onnext: () => void;
  onselect: (iso: string) => void;
}
