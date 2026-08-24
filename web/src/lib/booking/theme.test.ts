import { afterEach, describe, expect, it } from "vitest";
import {
  applyBookingTheme,
  hexToOklchHue,
  relativeLuminance,
} from "./theme";

// Hue OKLCH wyznaczamy z koloru salonu — paleta brand dziedziczy go w light i dark.
describe("hexToOklchHue", () => {
  it.each([
    ["#FF0000", 29], // czerwień
    ["#00FF00", 142], // zieleń
    ["#0000FF", 264], // błękit
    ["#FF00FF", 328], // magenta/fuksja
  ])("%s → hue ~%i", (hex, expected) => {
    const h = hexToOklchHue(hex);
    expect(h).not.toBeNull();
    expect(Math.abs((h as number) - expected)).toBeLessThan(6);
  });

  it("akceptuje bez # i wielkość liter", () => {
    expect(hexToOklchHue("ff0000")).toBeCloseTo(hexToOklchHue("#FF0000") as number, 1);
  });

  it.each(["#808080", "#000000", "#ffffff"])("kolor achromatyczny %s → null", (hex) => {
    expect(hexToOklchHue(hex)).toBeNull();
  });

  it.each(["", "#12", "niebieski", "#1234ZZ"])("nieprawidłowy %s → null", (hex) => {
    expect(hexToOklchHue(hex)).toBeNull();
  });
});

describe("relativeLuminance", () => {
  it("biały ~1, czarny ~0", () => {
    expect(relativeLuminance("#FFFFFF")).toBeCloseTo(1, 2);
    expect(relativeLuminance("#000000")).toBeCloseTo(0, 2);
  });
  it("jasny kolor > próg, ciemny < próg", () => {
    expect(relativeLuminance("#FDF2F8") as number).toBeGreaterThan(0.18); // pudrowy róż
    expect(relativeLuminance("#221C38") as number).toBeLessThan(0.18); // ciemny granat
  });
});

describe("applyBookingTheme", () => {
  afterEach(() => {
    const r = document.documentElement;
    for (const v of [
      "--brand-h",
      "--accent",
      "--accent-strong",
      "--accent-contrast",
      "--booking-bg",
      "--booking-surface",
      "--booking-price",
    ]) {
      r.style.removeProperty(v);
    }
    r.classList.remove("dark");
  });

  it("akcent ustawia dokładny kolor + auto-kontrast tekstu", () => {
    applyBookingTheme({ accent: "#D4AF37" }); // złoto (jasne) → ciemny tekst
    const s = document.documentElement.style;
    expect(s.getPropertyValue("--accent")).toBe("#D4AF37");
    expect(s.getPropertyValue("--accent-contrast")).toBe("#0a0a0a");

    applyBookingTheme({ accent: "#1e1b4b" }); // granat (ciemny) → biały tekst
    expect(document.documentElement.style.getPropertyValue("--accent-contrast")).toBe("#ffffff");
  });

  it("ustawia zmienne CSS z kolorów salonu", () => {
    applyBookingTheme({ background: "#fdf2f8", surface: "#ffffff", price: "#0d9488" });
    const s = document.documentElement.style;
    expect(s.getPropertyValue("--booking-bg")).toBe("#fdf2f8");
    expect(s.getPropertyValue("--booking-surface")).toBe("#ffffff");
    expect(s.getPropertyValue("--booking-price")).toBe("#0d9488");
  });

  it("ciemne tło karty włącza schemat dark, jasne go wyłącza", () => {
    applyBookingTheme({ surface: "#1e1b2e" });
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    applyBookingTheme({ surface: "#ffffff" });
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });

  it("brak wartości czyści override i nie rusza schematu", () => {
    document.documentElement.classList.add("dark");
    applyBookingTheme({});
    expect(document.documentElement.style.getPropertyValue("--booking-surface")).toBe("");
    // Brak surface → nie ruszamy klasy dark (szanujemy preferencję użytkownika).
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });
});
