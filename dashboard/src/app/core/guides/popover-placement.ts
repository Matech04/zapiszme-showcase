/**
 * Decyzja o odsunięciu popovera od kotwicy — wydzielona z silnika, żeby dało się ją
 * przetestować bez DOM-u (jsdom zwraca z `getBoundingClientRect` same zera, więc w spec-u
 * silnika ta ścieżka nigdy by się nie wykonała).
 */

export interface Box {
  top: number;
  bottom: number;
  left: number;
  right: number;
  height: number;
}

/** Odstęp między popoverem a kotwicą po przesunięciu. */
export const POPOVER_GAP = 8;

export function overlaps(a: Box, b: Box): boolean {
  return a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;
}

/**
 * Nowa współrzędna `top` popovera albo `null`, gdy przesuwać nie trzeba lub nie ma dokąd.
 *
 * Preferujemy miejsce NAD kotwicą: na telefonie dolna krawędź to strefa kciuka i tam
 * najczęściej siedzi przycisk akcji, więc popover nad nim nie wchodzi w drogę. Gdy nad
 * kotwicą brakuje miejsca, schodzimy pod nią. Gdy nie mieści się nigdzie — zwracamy
 * `null`: przesuwanie w ciemno dałoby popover ucięty krawędzią ekranu, co jest gorsze
 * od kolizji, którą przynajmniej widać.
 */
export function resolvePopoverTop(anchor: Box, popover: Box, viewportHeight: number): number | null {
  if (!overlaps(anchor, popover)) return null;

  const above = anchor.top - POPOVER_GAP - popover.height;
  if (above >= POPOVER_GAP) return above;

  const below = anchor.bottom + POPOVER_GAP;
  if (below + popover.height <= viewportHeight - POPOVER_GAP) return below;

  return null;
}
