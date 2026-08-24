import { describe, expect, it } from "vitest";
import {
  groupSlotsByTimeOfDay,
  hasPreferredSlots,
  sortSlots,
  splitPreferred,
} from "./slots";

const slots = [
  { slot: "18:30", isPreferred: false },
  { slot: "09:00", isPreferred: true },
  { slot: "13:15", isPreferred: false },
  { slot: "11:00", isPreferred: true },
];

describe("sortSlots", () => {
  it("sorts numerically by time", () => {
    expect(sortSlots(slots).map((s) => s.slot)).toEqual([
      "09:00",
      "11:00",
      "13:15",
      "18:30",
    ]);
  });
});

describe("groupSlotsByTimeOfDay", () => {
  it("buckets into rano/popołudnie/wieczór and drops empties", () => {
    const groups = groupSlotsByTimeOfDay(sortSlots(slots));
    expect(groups.map((g) => g.key)).toEqual([
      "morning",
      "afternoon",
      "evening",
    ]);
    expect(groups[0].items.map((s) => s.slot)).toEqual(["09:00", "11:00"]);
    expect(groups[1].items.map((s) => s.slot)).toEqual(["13:15"]);
    expect(groups[2].items.map((s) => s.slot)).toEqual(["18:30"]);
  });
});

describe("preferred split", () => {
  it("detects and partitions preferred slots", () => {
    expect(hasPreferredSlots(slots)).toBe(true);
    const { preferred, other } = splitPreferred(sortSlots(slots));
    expect(preferred.map((s) => s.slot)).toEqual(["09:00", "11:00"]);
    expect(other.map((s) => s.slot)).toEqual(["13:15", "18:30"]);
  });
});
