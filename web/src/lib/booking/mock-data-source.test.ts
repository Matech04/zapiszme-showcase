import { describe, expect, it } from "vitest";
import { _mockInternals, mockBookingDataSource } from "./mock-data-source";

const { generateSlots } = _mockInternals;
const reference = new Date(2026, 4, 20); // środa 20 maja 2026

describe("generateSlots", () => {
  it("is deterministic for the same key", () => {
    const a = generateSlots("2026-05-21", "emp-ania", "svc-mani-hybryda", reference);
    const b = generateSlots("2026-05-21", "emp-ania", "svc-mani-hybryda", reference);
    expect(a).toEqual(b);
  });

  it("returns nothing for Sundays and past days", () => {
    expect(generateSlots("2026-05-24", "emp-ania", "svc-henna", reference)).toEqual([]); // niedziela
    expect(generateSlots("2026-05-10", "emp-ania", "svc-henna", reference)).toEqual([]); // przeszłość
  });

  it("keeps slots within working hours and respects duration", () => {
    const slots = generateSlots("2026-05-21", "emp-marta", "svc-zel", reference); // 150 min
    for (const s of slots) {
      const [h, m] = (s.slot ?? "").split(":").map(Number);
      const start = h * 60 + m;
      expect(start).toBeGreaterThanOrEqual(9 * 60);
      expect(start + 150).toBeLessThanOrEqual(17 * 60); // mieści się przed zamknięciem
    }
  });
});

describe("mockBookingDataSource", () => {
  it("month availability matches the day's slot count", async () => {
    const ds = mockBookingDataSource();
    const rows = await ds.loadMonthAvailability(
      2026,
      5,
      "emp-ania",
      ["svc-mani-klasyczny"],
      new AbortController().signal,
    );
    const row = rows.days!.find((r) => r.date === "2026-05-21")!;
    const slots = await ds.loadSlots(
      "2026-05-21",
      "emp-ania",
      ["svc-mani-klasyczny"],
      new AbortController().signal,
    );
    expect(row.availableCount).toBe(slots.length);
  });

  it("createHold returns an id and a future lease", async () => {
    const ds = mockBookingDataSource({ latencyMs: 0 });
    const hold = await ds.createHold(
      {
        employeeId: "emp-ania",
        serviceIds: ["svc-henna"],
        date: "2026-05-21",
        startTime: "10:00:00",
      },
      new AbortController().signal,
    );
    expect(hold.appointmentId).toBeTruthy();
    expect(new Date(hold.lease!.expiryTimeUtc!).getTime()).toBeGreaterThan(
      Date.now(),
    );
  });

  it("verifyOtp accepts 4+ digits and rejects junk", async () => {
    const ds = mockBookingDataSource({ latencyMs: 0 });
    const ok = await ds.verifyOtp("demo-1", { token: "t", otp: "1234" });
    expect(ok.requiresManualConfirmation).toBe(false);
    // Demo wystawia „sesję", by zaprezentować pominięcie OTP przy kolejnej rezerwacji.
    expect(ok.sessionToken).toBeTruthy();
    await expect(
      ds.verifyOtp("demo-1", { token: "t", otp: "ab" }),
    ).rejects.toThrow();
  });

  it("aborts in-flight loads", async () => {
    const ds = mockBookingDataSource({ latencyMs: 50 });
    const ctrl = new AbortController();
    const p = ds.loadSalon(ctrl.signal);
    ctrl.abort();
    await expect(p).rejects.toMatchObject({ name: "AbortError" });
  });
});
