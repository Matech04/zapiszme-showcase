/** Sortowanie i grupowanie slotów godzinowych. Wydzielone z BookingPanel. */
import type { AppointmentSlotDto } from "../booking-openapi-client";

export function sortSlots(slots: AppointmentSlotDto[]): AppointmentSlotDto[] {
  return [...slots].sort((a, b) =>
    (a.slot ?? "").localeCompare(b.slot ?? "", undefined, { numeric: true }),
  );
}

export function slotHour(slot: string): number {
  const m = /^(\d{1,2})/.exec(slot.trim());
  return m ? Number(m[1]) : 0;
}

export type SlotGroupKey = "morning" | "afternoon" | "evening";

export type SlotGroup = {
  key: SlotGroupKey;
  title: string;
  items: AppointmentSlotDto[];
};

const GROUP_TITLES: Record<SlotGroupKey, string> = {
  morning: "Rano",
  afternoon: "Popołudnie",
  evening: "Wieczór",
};

/** Dzieli posortowane sloty na Rano / Popołudnie / Wieczór (puste grupy pomijane). */
export function groupSlotsByTimeOfDay(
  sortedSlots: AppointmentSlotDto[],
): SlotGroup[] {
  const buckets: Record<SlotGroupKey, AppointmentSlotDto[]> = {
    morning: [],
    afternoon: [],
    evening: [],
  };
  for (const s of sortedSlots) {
    const h = slotHour(s.slot ?? "");
    if (h < 12) buckets.morning.push(s);
    else if (h < 17) buckets.afternoon.push(s);
    else buckets.evening.push(s);
  }
  return (Object.keys(buckets) as SlotGroupKey[])
    .filter((k) => buckets[k].length > 0)
    .map((k) => ({ key: k, title: GROUP_TITLES[k], items: buckets[k] }));
}

export function hasPreferredSlots(slots: AppointmentSlotDto[]): boolean {
  return slots.some((s) => s.isPreferred);
}

export function splitPreferred(sortedSlots: AppointmentSlotDto[]): {
  preferred: AppointmentSlotDto[];
  other: AppointmentSlotDto[];
} {
  return {
    preferred: sortedSlots.filter((s) => s.isPreferred),
    other: sortedSlots.filter((s) => !s.isPreferred),
  };
}
