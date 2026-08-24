/** Grupowanie usług wg kategorii (kolejność wg orderIndex, sieroty na końcu). */
import type {
  BookingServiceDto,
  ServiceCategoryDto,
} from "../booking-openapi-client";

export type ServiceCategoryBlock = {
  key: string;
  title: string;
  items: BookingServiceDto[];
};

/**
 * Stabilne sortowanie usług po `orderIndex` (rosnąco); przy równych wartościach
 * zachowuje kolejność wejściową (`Array.prototype.sort` w JS jest stabilny od ES2019).
 */
function sortByOrderIndex(items: BookingServiceDto[]): BookingServiceDto[] {
  return [...items].sort((a, b) => (a.orderIndex ?? 0) - (b.orderIndex ?? 0));
}

export function buildServiceBlocks(
  services: BookingServiceDto[],
  serviceCategories: ServiceCategoryDto[],
): ServiceCategoryBlock[] {
  const cats = [...serviceCategories].sort(
    (a, b) => (a.orderIndex ?? 0) - (b.orderIndex ?? 0),
  );
  const assigned = new Set<string>();
  const categoryBlocks: ServiceCategoryBlock[] = [];

  for (const c of cats) {
    if (!c.id) continue;
    const items = sortByOrderIndex(
      services.filter((s) => s.id && s.categoryId === c.id),
    );
    for (const s of items) assigned.add(s.id!);
    if (items.length)
      categoryBlocks.push({ key: c.id, title: c.name ?? "Kategoria", items });
  }

  const orphans = sortByOrderIndex(
    services.filter((s) => s.id && !assigned.has(s.id)),
  );

  // Brak realnych kategorii → wszystkie usługi w jednym bloku "_all", który UI
  // renderuje płasko (bez akordeonu — od razu widoczne, bez rozwijania).
  if (categoryBlocks.length === 0) {
    return orphans.length
      ? [{ key: "_all", title: "Usługi", items: orphans }]
      : [];
  }

  // Są kategorie: usługi bez kategorii lądują w zwijanym bloku "Inne usługi".
  const blocks = [...categoryBlocks];
  if (orphans.length)
    blocks.push({ key: "_other", title: "Inne usługi", items: orphans });
  return blocks;
}
