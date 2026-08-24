import { describe, expect, it } from 'vitest';
import { POPOVER_GAP, resolvePopoverTop } from './popover-placement';

/**
 * Regresja z przeglądarki: na szerokości 500 px popover kroku „Zapisz kartę" w przewodniku
 * „Dodajmy klientkę" lądował DOKŁADNIE na przycisku DODAJ. Krok zadaniowy nie ma przycisku
 * „Dalej", więc jedyną drogą naprzód było kliknięcie w przycisk — a to trafiało w popover.
 * Przewodnik był nieukończalny na telefonie, przy poprawnym zachowaniu na desktopie.
 */
describe('resolvePopoverTop', () => {
  const box = (top: number, height: number, left = 0, right = 320) => ({
    top,
    bottom: top + height,
    height,
    left,
    right,
  });

  it('nie rusza popovera, który nie dotyka kotwicy', () => {
    const anchor = box(600, 50, 149, 235);
    const popover = box(400, 140);

    expect(resolvePopoverTop(anchor, popover, 820)).toBeNull();
  });

  it('przenosi NAD kotwicę popover położony na niej (realne wymiary z błędu)', () => {
    const anchor = box(667, 50, 149, 235);
    const popover = box(593, 140, 14, 334);

    // 667 − 8 odstępu − 140 wysokości = 519.
    expect(resolvePopoverTop(anchor, popover, 820)).toBe(519);
  });

  it('schodzi POD kotwicę, gdy nad nią brakuje miejsca', () => {
    const anchor = box(20, 50, 0, 200);
    const popover = box(30, 140);

    expect(resolvePopoverTop(anchor, popover, 820)).toBe(20 + 50 + POPOVER_GAP);
  });

  it('zostawia popover w spokoju, gdy nie mieści się ani nad, ani pod kotwicą', () => {
    // Kotwica prawie na całą wysokość ekranu — każde przesunięcie ucięłoby popover.
    const anchor = box(10, 300, 0, 200);
    const popover = box(100, 140);

    expect(resolvePopoverTop(anchor, popover, 360)).toBeNull();
  });
});
