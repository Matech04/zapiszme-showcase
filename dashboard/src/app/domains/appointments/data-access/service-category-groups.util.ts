/**
 * Grupowanie opcji usług wg kategorii katalogu — współdzielone przez wszystkie ekrany
 * ręcznego doboru usługi (szuflada kalendarza, formularz /schedule/new, zmiana usługi).
 *
 * UWAGA: to NIE jest `Service.comboGroup` (grupa wariantów, wzajemne wykluczanie
 * „tego samego typu" — patrz `combo-select.util.ts`). Kategoria to podział katalogowy
 * służący wyłącznie prezentacji.
 */
import type { ServiceCategoryDto } from '@core/api/api-client';

/** Klucz sekcji zbierającej usługi bez (istniejącej) kategorii. */
export const UNCATEGORIZED_KEY = '__uncategorized__';

/** Sekcja listy usług: nagłówek kategorii + należące do niej opcje. */
export interface ServiceCategoryGroup<T> {
  key: string;
  label: string;
  options: T[];
}

/** Minimum, jakiego grupowanie wymaga od opcji usługi. */
export interface HasCategoryId {
  categoryId?: string | null;
}

/**
 * Dzieli płaską listę opcji usług na sekcje wg kategorii.
 *
 * - kolejność sekcji = `orderIndex` kategorii, przy remisie nazwa (locale `pl`),
 * - usługi bez kategorii — oraz wskazujące na kategorię nieaktywną/usuniętą, której
 *   nie ma w `categories` — trafiają do sekcji „Bez kategorii" na końcu,
 * - puste sekcje są pomijane, więc wynik pokazuje tylko kategorie realnie obsadzone
 *   przez danego pracownika,
 * - kolejność opcji wewnątrz sekcji = kolejność wejściowa, BEZ sortowania.
 *
 * Wołający odpowiada za kolejność wejściową: buduj opcje iterując katalog z
 * `getServices` (sortowany po `OrderIndex, Name`), a nie listę z `getEmployeeServices`
 * — ta ostatnia nie ma `OrderBy` po stronie backendu i zwraca wiersze w kolejności
 * dowolnej, która potrafi się zmienić po edycji przypisań.
 */
export function groupServicesByCategory<T extends HasCategoryId>(
  options: readonly T[],
  categories: readonly ServiceCategoryDto[],
): ServiceCategoryGroup<T>[] {
  const buckets = new Map<string, ServiceCategoryGroup<T>>();
  for (const category of [...categories].sort(byOrderThenName)) {
    if (category.id) {
      buckets.set(category.id, {
        key: category.id,
        label: category.name?.trim() || 'Kategoria',
        options: [],
      });
    }
  }

  const uncategorized: ServiceCategoryGroup<T> = {
    key: UNCATEGORIZED_KEY,
    label: 'Bez kategorii',
    options: [],
  };

  for (const option of options) {
    const bucket = (option.categoryId ? buckets.get(option.categoryId) : undefined) ?? uncategorized;
    bucket.options.push(option);
  }

  return [...buckets.values(), uncategorized].filter((group) => group.options.length > 0);
}

/**
 * Czy pokazywać nagłówki sekcji. Przy jednej sekcji podziału faktycznie nie ma
 * (typowy salon ma jedną kategorię „Usługi") — nagłówek byłby wtedy samym szumem
 * nad listą, więc renderujemy ją płasko.
 */
export function shouldShowCategoryHeaders(groups: readonly ServiceCategoryGroup<unknown>[]): boolean {
  return groups.length > 1;
}

function byOrderThenName(a: ServiceCategoryDto, b: ServiceCategoryDto): number {
  return (
    (a.orderIndex ?? 0) - (b.orderIndex ?? 0) ||
    (a.name ?? '').localeCompare(b.name ?? '', 'pl')
  );
}
