import { describe, expect, it } from "vitest";
import type { BookingServiceDto } from "../booking-openapi-client";
import {
  allowedAddonIds,
  comboDurationMinutes,
  comboPriceRange,
  makeGroupResolver,
  removeOrphanedAddons,
  serviceGroupKey,
  toggleServiceSelection,
} from "./combo";

describe("serviceGroupKey", () => {
  it("normalizuje (trim + lowercase)", () => {
    expect(serviceGroupKey({ comboGroup: "  Przedłużanie " })).toBe("przedłużanie");
  });
  it("pusta/null grupa → pusty string", () => {
    expect(serviceGroupKey({ comboGroup: "" })).toBe("");
    expect(serviceGroupKey({ comboGroup: null })).toBe("");
    expect(serviceGroupKey({})).toBe("");
    expect(serviceGroupKey(undefined)).toBe("");
  });
});

describe("toggleServiceSelection", () => {
  // Mapa grup: a,b → grupa "g1"; c → grupa "g2"; x,y → bez grupy.
  const groups: Record<string, string> = { a: "g1", b: "g1", c: "g2", x: "", y: "" };
  const groupOf = (id: string) => groups[id] ?? "";
  const MAX = 5;

  it("dodaje do pustego wyboru", () => {
    expect(toggleServiceSelection([], "x", groupOf, MAX)).toEqual(["x"]);
  });

  it("dodaje kolejne usługi bez grupy (zachowuje kolejność)", () => {
    expect(toggleServiceSelection(["x"], "y", groupOf, MAX)).toEqual(["x", "y"]);
  });

  it("usuwa już wybraną usługę", () => {
    expect(toggleServiceSelection(["x", "y"], "x", groupOf, MAX)).toEqual(["y"]);
  });

  it("PODMIENIA usługę z tej samej grupy (radio w grupie)", () => {
    // 'a' z grupy g1 wybrane; klik 'b' (też g1) → 'a' znika, 'b' na końcu.
    expect(toggleServiceSelection(["a", "x"], "b", groupOf, MAX)).toEqual(["x", "b"]);
  });

  it("usługi z różnych grup współistnieją", () => {
    expect(toggleServiceSelection(["a"], "c", groupOf, MAX)).toEqual(["a", "c"]);
  });

  it("nie przekracza limitu (zwraca bez zmiany zawartości)", () => {
    const sel = ["x", "y", "a", "c"];
    const result = toggleServiceSelection(sel, "z-bez-grupy", groupOf, 4);
    expect(result).toEqual(sel); // dodanie ponad limit = no-op
  });

  it("podmiana w grupie działa nawet przy limicie (nie zwiększa liczby)", () => {
    // limit 2, mamy [a (g1), x]; klik b (g1) → podmiana, nadal 2 elementy.
    expect(toggleServiceSelection(["a", "x"], "b", groupOf, 2)).toEqual(["x", "b"]);
  });

  it("usuwanie działa zawsze, nawet przy osiągniętym limicie", () => {
    const sel = ["x", "y"];
    expect(toggleServiceSelection(sel, "x", groupOf, 2)).toEqual(["y"]);
  });

  it("zwraca nową tablicę (nie mutuje wejścia)", () => {
    const sel = ["x"];
    const out = toggleServiceSelection(sel, "y", groupOf, MAX);
    expect(out).not.toBe(sel);
    expect(sel).toEqual(["x"]);
  });
});

describe("makeGroupResolver", () => {
  it("rozwiązuje grupę po id z listy usług (znormalizowaną)", () => {
    const services = [
      { id: "s1", comboGroup: "Przedłużanie" },
      { id: "s2", comboGroup: null },
    ] as BookingServiceDto[];
    const groupOf = makeGroupResolver(services);
    expect(groupOf("s1")).toBe("przedłużanie");
    expect(groupOf("s2")).toBe("");
    expect(groupOf("nieznane")).toBe("");
  });
});

describe("comboDurationMinutes", () => {
  it("sumuje czasy", () => {
    expect(comboDurationMinutes([{ durationInMinutes: 60 }, { durationInMinutes: 30 }, {}])).toBe(90);
  });
  it("pusta lista → 0", () => {
    expect(comboDurationMinutes([])).toBe(0);
  });
});

describe("comboPriceRange", () => {
  it("sumuje ceny stałe (bez widełek)", () => {
    const r = comboPriceRange([
      { price: { amount: 100, currency: "PLN" } },
      { price: { amount: 40, currency: "PLN" } },
    ]);
    expect(r).toEqual({
      min: 140,
      max: 140,
      hasRange: false,
      priceHidden: false,
      currency: "PLN",
    });
  });

  it("gdy którakolwiek usługa ma hidePrice → priceHidden = true", () => {
    const r = comboPriceRange([
      { price: { amount: 100, currency: "PLN" } },
      { price: { amount: 40, currency: "PLN" }, hidePrice: true },
    ]);
    expect(r.priceHidden).toBe(true);
    // sumy nadal liczone (UI je ignoruje), ale flaga steruje ukryciem
    expect(r.min).toBe(140);
  });

  it("bez hidePrice → priceHidden = false", () => {
    const r = comboPriceRange([{ price: { amount: 100, currency: "PLN" }, hidePrice: false }]);
    expect(r.priceHidden).toBe(false);
  });

  it("gdy któraś usługa ma widełki → łączna cena to zakres", () => {
    const r = comboPriceRange([
      { price: { amount: 100, currency: "PLN" }, maxAmount: 150 }, // widełki
      { price: { amount: 40, currency: "PLN" } }, // stała
    ]);
    expect(r.min).toBe(140);
    expect(r.max).toBe(190);
    expect(r.hasRange).toBe(true);
    expect(r.currency).toBe("PLN");
  });

  it("pusta lista → 0/0, brak zakresu, domyślna waluta", () => {
    expect(comboPriceRange([])).toEqual({
      min: 0,
      max: 0,
      hasRange: false,
      priceHidden: false,
      currency: "PLN",
    });
  });
});

describe("dodatki (add-ony)", () => {
  const svc = (
    id: string,
    isAddon: boolean,
    addonServiceIds: string[] = [],
  ): BookingServiceDto =>
    ({ id, isAddon, addonServiceIds, durationInMinutes: isAddon ? 0 : 60 }) as BookingServiceDto;

  const services = [
    svc("main", false, ["addon1"]),
    svc("addon1", true),
    svc("addon2", true),
  ];

  it("allowedAddonIds zwraca dodatki dozwolone przez wybrane usługi główne", () => {
    const allowed = allowedAddonIds(["main"], services);
    expect(allowed.has("addon1")).toBe(true);
    expect(allowed.has("addon2")).toBe(false);
  });

  it("allowedAddonIds puste, gdy nie wybrano usługi głównej", () => {
    expect(allowedAddonIds([], services).size).toBe(0);
    expect(allowedAddonIds(["addon1"], services).size).toBe(0);
  });

  it("removeOrphanedAddons usuwa dodatek po odznaczeniu usługi głównej", () => {
    expect(removeOrphanedAddons(["addon1"], services)).toEqual([]);
    expect(removeOrphanedAddons(["main", "addon1"], services)).toEqual(["main", "addon1"]);
  });

  it("removeOrphanedAddons nie rusza dozwolonych dodatków i usług głównych", () => {
    expect(removeOrphanedAddons(["main", "addon1"], services)).toEqual(["main", "addon1"]);
  });
});
