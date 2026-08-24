import { describe, expect, it } from 'vitest';
import type { ServiceCategoryDto } from '@core/api/api-client';
import {
  UNCATEGORIZED_KEY,
  groupServicesByCategory,
  shouldShowCategoryHeaders,
} from './service-category-groups.util';

const category = (id: string, name: string, orderIndex: number) =>
  ({ id, name, orderIndex }) as ServiceCategoryDto;

const option = (value: string, categoryId: string | null) => ({ value, categoryId });

describe('groupServicesByCategory', () => {
  it('układa sekcje wg orderIndex kategorii, nie wg kolejności usług', () => {
    const groups = groupServicesByCategory(
      [option('brwi', 'c-brwi'), option('mani', 'c-pazn')],
      [category('c-brwi', 'Brwi', 2), category('c-pazn', 'Paznokcie', 1)],
    );

    expect(groups.map((g) => g.label)).toEqual(['Paznokcie', 'Brwi']);
    expect(groups[0].options.map((o) => o.value)).toEqual(['mani']);
  });

  it('przy równym orderIndex rozstrzyga nazwa (locale pl)', () => {
    const groups = groupServicesByCategory(
      [option('a', 'c-z'), option('b', 'c-l')],
      [category('c-z', 'Zabiegi', 0), category('c-l', 'Ładowanie', 0)],
    );

    expect(groups.map((g) => g.label)).toEqual(['Ładowanie', 'Zabiegi']);
  });

  it('zachowuje kolejność wejściową usług wewnątrz sekcji', () => {
    const groups = groupServicesByCategory(
      [option('pierwsza', 'c-1'), option('druga', 'c-1'), option('trzecia', 'c-1')],
      [category('c-1', 'Paznokcie', 0)],
    );

    expect(groups[0].options.map((o) => o.value)).toEqual(['pierwsza', 'druga', 'trzecia']);
  });

  it('zsypuje usługi bez kategorii do sekcji na końcu', () => {
    const groups = groupServicesByCategory(
      [option('luzna', null), option('mani', 'c-pazn')],
      [category('c-pazn', 'Paznokcie', 0)],
    );

    expect(groups.map((g) => g.key)).toEqual(['c-pazn', UNCATEGORIZED_KEY]);
    expect(groups[1].label).toBe('Bez kategorii');
  });

  it('traktuje kategorię nieaktywną/usuniętą jak brak kategorii zamiast gubić usługę', () => {
    const groups = groupServicesByCategory([option('sierota', 'c-skasowana')], []);

    expect(groups).toHaveLength(1);
    expect(groups[0].key).toBe(UNCATEGORIZED_KEY);
    expect(groups[0].options.map((o) => o.value)).toEqual(['sierota']);
  });

  it('pomija kategorie, do których pracownik nie ma przypisanej usługi', () => {
    const groups = groupServicesByCategory(
      [option('mani', 'c-pazn')],
      [category('c-pazn', 'Paznokcie', 0), category('c-brwi', 'Brwi', 1)],
    );

    expect(groups.map((g) => g.label)).toEqual(['Paznokcie']);
  });

  it('zwraca pustą listę dla braku usług', () => {
    expect(groupServicesByCategory([], [category('c-1', 'Paznokcie', 0)])).toEqual([]);
  });

  it('podmienia pustą nazwę kategorii na etykietę zastępczą', () => {
    const groups = groupServicesByCategory([option('a', 'c-1')], [category('c-1', '   ', 0)]);

    expect(groups[0].label).toBe('Kategoria');
  });
});

describe('shouldShowCategoryHeaders', () => {
  it('ukrywa nagłówki przy jednej sekcji — nie ma czego rozdzielać', () => {
    const groups = groupServicesByCategory(
      [option('a', 'c-1'), option('b', 'c-1')],
      [category('c-1', 'Usługi', 0)],
    );

    expect(shouldShowCategoryHeaders(groups)).toBe(false);
  });

  it('pokazuje nagłówki od dwóch sekcji wzwyż', () => {
    const groups = groupServicesByCategory(
      [option('a', 'c-1'), option('b', null)],
      [category('c-1', 'Usługi', 0)],
    );

    expect(shouldShowCategoryHeaders(groups)).toBe(true);
  });
});
