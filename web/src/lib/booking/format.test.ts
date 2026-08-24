import { describe, expect, it } from "vitest";
import {
  addMinutesToTime,
  formatCountdown,
  formatDatePl,
  formatDurationRange,
  formatMoney,
  formatMoneyRange,
  formatSlotRange,
  initials,
  normalizeTimeOnly,
  parseISODate,
  titleFromSlug,
  toISODate,
} from "./format";

describe("toISODate / parseISODate", () => {
  it("round-trips a local date without timezone drift", () => {
    const d = new Date(2026, 4, 20); // 20 maja 2026, lokalnie
    expect(toISODate(d)).toBe("2026-05-20");
    const parsed = parseISODate("2026-05-20")!;
    expect(parsed.getFullYear()).toBe(2026);
    expect(parsed.getMonth()).toBe(4);
    expect(parsed.getDate()).toBe(20);
  });

  it("rejects malformed input", () => {
    expect(parseISODate("2026/05/20")).toBeNull();
    expect(parseISODate("nonsense")).toBeNull();
  });
});

describe("addMinutesToTime / formatSlotRange", () => {
  it("adds duration to a start time", () => {
    expect(addMinutesToTime("14:00", 90)).toBe("15:30");
    expect(addMinutesToTime("23:30", 90)).toBe("23:59"); // klamruje do końca dnia
  });

  it("formats a range only when duration is given", () => {
    expect(formatSlotRange("14:00", 90)).toBe("14:00–15:30");
    expect(formatSlotRange("14:00")).toBe("14:00");
    expect(formatSlotRange("14:00", 0)).toBe("14:00");
  });
});

describe("formatMoneyRange", () => {
  it("shows a range when maxAmount exceeds the base price", () => {
    const r = formatMoneyRange({ amount: 100, currency: "PLN" }, 150);
    expect(r).toContain("–");
    expect(r.replace(/\s| /g, "")).toContain("100–");
    expect(r.replace(/\s| /g, "")).toContain("150zł");
  });

  it("shows a single price when there is no range", () => {
    const single = formatMoneyRange({ amount: 100, currency: "PLN" }, undefined);
    expect(single).not.toContain("–");
    // maxAmount equal or below base → no range
    expect(formatMoneyRange({ amount: 100, currency: "PLN" }, 100)).not.toContain("–");
    expect(formatMoneyRange({ amount: 100, currency: "PLN" }, 80)).not.toContain("–");
  });

  it("single free price (amount 0) → 'Bezpłatnie'", () => {
    expect(formatMoneyRange({ amount: 0, currency: "PLN" })).toBe("Bezpłatnie");
    expect(formatMoneyRange({ amount: 0, currency: "PLN" }, 0)).toBe("Bezpłatnie");
    // brak granicy/poniżej bazy też daje pojedynczą cenę
    expect(formatMoneyRange({ amount: 0, currency: "PLN" }, undefined)).toBe("Bezpłatnie");
  });

  it("range with 0 lower bound keeps raw '0 zł' (not 'Bezpłatnie')", () => {
    const r = formatMoneyRange({ amount: 0, currency: "PLN" }, 50);
    expect(r).toContain("–");
    expect(r).not.toContain("Bezpłatnie");
    expect(r.replace(/\s| /g, "")).toContain("0–");
    expect(r.replace(/\s| /g, "")).toContain("50zł");
  });
});

describe("formatMoney", () => {
  it("amount 0 → 'Bezpłatnie'", () => {
    expect(formatMoney({ amount: 0, currency: "PLN" })).toBe("Bezpłatnie");
  });
  it("undefined → '—'", () => {
    expect(formatMoney(undefined)).toBe("—");
  });
  it("positive amount → formatted currency", () => {
    expect(formatMoney({ amount: 100, currency: "PLN" }).replace(/\s| /g, "")).toContain(
      "100zł",
    );
  });
});

describe("formatDurationRange", () => {
  it("renders a range when both bounds are present", () => {
    expect(formatDurationRange(75, 60, 90)).toBe("60–90 min");
    expect(formatDurationRange(60, 60, 60)).toBe("60 min");
  });

  it("falls back to the planned value when no range", () => {
    expect(formatDurationRange(60)).toBe("60 min");
    expect(formatDurationRange(undefined)).toBe("—");
  });
});

describe("formatDatePl", () => {
  it("renders short Polish weekday + genitive month", () => {
    expect(formatDatePl("2026-05-20")).toBe("Śr, 20 maja");
    expect(formatDatePl("2026-01-01")).toBe("Cz, 1 stycznia");
  });
});

describe("formatCountdown", () => {
  it("formats seconds as M:SS", () => {
    expect(formatCountdown(180)).toBe("3:00");
    expect(formatCountdown(65)).toBe("1:05");
    expect(formatCountdown(-5)).toBe("0:00");
  });
});

describe("normalizeTimeOnly", () => {
  it("normalizes to HH:MM:SS", () => {
    expect(normalizeTimeOnly("9:5")).toBe("09:05:00");
    expect(normalizeTimeOnly("14:30:00")).toBe("14:30:00");
  });
});

describe("misc", () => {
  it("titleFromSlug", () => {
    expect(titleFromSlug("studio-urody-ania")).toBe("Studio Urody Ania");
  });
  it("initials", () => {
    expect(initials("Anna", "Kowalska")).toBe("AK");
    expect(initials()).toBe("?");
  });
});
