import { describe, expect, it } from "vitest";
import {
  availabilityStatus,
  buildMonthDays,
  computeAvailabilityScale,
  monthAvailabilityNotice,
} from "./availability";

describe("computeAvailabilityScale", () => {
  it("falls back to fixed thresholds when there is no data", () => {
    expect(computeAvailabilityScale(new Map())).toEqual({ free: 6, limited: 3 });
  });

  it("derives thresholds from the salon's typical day (median)", () => {
    // Salon SOLO: typowy dzień ~3 sloty. Bez relatywnych progów wszystko byłoby „mało".
    const avail = new Map([
      ["2026-05-01", 3],
      ["2026-05-02", 3],
      ["2026-05-03", 4],
      ["2026-05-04", 2],
      ["2026-05-05", 0],
    ]);
    const scale = computeAvailabilityScale(avail);
    expect(scale.free).toBe(3); // typowy dzień = „dużo"
    expect(scale.limited).toBe(2);
    expect(availabilityStatus(3, scale)).toBe("free");
    expect(availabilityStatus(2, scale)).toBe("limited");
    expect(availabilityStatus(1, scale)).toBe("scarce");
    expect(availabilityStatus(0, scale)).toBe("none");
  });

  it("ignores zero-days when computing the reference", () => {
    const avail = new Map([
      ["a", 0],
      ["b", 0],
      ["c", 8],
    ]);
    expect(computeAvailabilityScale(avail).free).toBe(8);
  });
});

describe("availabilityStatus", () => {
  it("returns unknown when count is undefined", () => {
    expect(availabilityStatus(undefined)).toBe("unknown");
  });
});

describe("buildMonthDays", () => {
  const reference = new Date(2026, 4, 20); // 20 maja 2026

  it("flags past, today and marks availability", () => {
    const avail = new Map([
      ["2026-05-20", 4],
      ["2026-05-21", 0],
    ]);
    const days = buildMonthDays(2026, 5, avail, undefined, reference);
    expect(days).toHaveLength(31);

    const d19 = days.find((d) => d.iso === "2026-05-19")!;
    expect(d19.isPast).toBe(true);
    expect(d19.status).toBe("none");

    const today = days.find((d) => d.iso === "2026-05-20")!;
    expect(today.isToday).toBe(true);
    expect(today.isPast).toBe(false);
    expect(today.status).toBe("free");

    const d21 = days.find((d) => d.iso === "2026-05-21")!;
    expect(d21.status).toBe("none"); // 0 slotów, ale nie przeszłość
    expect(d21.isPast).toBe(false);
  });
});

describe("monthAvailabilityNotice — zamknięty miesiąc", () => {
  const closedMonth = {
    loading: false,
    error: false,
    hasAnyFreeDay: false,
    hasWorkingDay: false,
    closed: true,
  };

  it("podaje datę otwarcia, gdy miesiąc otworzy się sam", () => {
    const msg = monthAvailabilityNotice({ ...closedMonth, opensOn: "2026-09-01" });
    expect(msg).toContain("1 września");
  });

  it("nie obiecuje daty, gdy salon otworzy ręcznie", () => {
    const msg = monthAvailabilityNotice({ ...closedMonth, opensOn: null });
    expect(msg).toBe("Ten miesiąc nie jest jeszcze otwarty na rezerwacje.");
  });

  // Sedno rozróżnienia: zamknięty miesiąc wygląda w danych identycznie jak brak grafiku
  // (zero wolnych dni, zero dni roboczych), ale to zupełnie inna wiadomość dla klientki.
  it("nie myli zamknięcia z brakiem grafiku", () => {
    const closed = monthAvailabilityNotice({ ...closedMonth, opensOn: "2026-09-01" });
    const noSchedule = monthAvailabilityNotice({ ...closedMonth, closed: false });
    expect(noSchedule).toContain("nie przygotowano jeszcze grafiku");
    expect(closed).not.toBe(noSchedule);
  });

  it("milczy, dopóki dostępność się ładuje", () => {
    expect(
      monthAvailabilityNotice({ ...closedMonth, loading: true, opensOn: "2026-09-01" }),
    ).toBeNull();
  });
});
