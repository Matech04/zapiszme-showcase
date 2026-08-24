<script lang="ts">
  import {
    STATUS_DOT,
    STATUS_LABEL,
    type DayChip,
  } from "../../../../lib/booking/availability";
  import { formatDateLongPl, parseISODate } from "../../../../lib/booking/format";

  // Wariant „siatka miesiąca" — klasyczny kalendarz 7×N. Nagłówek miesiąca, nawigację
  // i legendę dostarcza wspólny `AvailabilitySection`; tu renderujemy tylko siatkę.
  let {
    days,
    selectedDate,
    locked = false,
    monthLoading = false,
    onselect,
  }: {
    days: DayChip[];
    selectedDate: string;
    locked?: boolean;
    /** Dostępność miesiąca jeszcze leci — dni „unknown" blokujemy, żeby nie dało się wejść
     * na dzień, który za chwilę okaże się zajęty (wyścig: `locked` puszcza przed danymi). */
    monthLoading?: boolean;
    onselect: (iso: string) => void;
  } = $props();

  // Nagłówki kolumn (poniedziałek-pierwszy, po polsku).
  const WEEKDAY_HEADERS = ["Pn", "Wt", "Śr", "Cz", "Pt", "So", "Nd"];

  // Puste komórki wiodące = kolumna pierwszego dnia miesiąca (Monday-first: (getDay()+6)%7).
  const leading = $derived.by((): number => {
    const first = days[0];
    const d = first ? parseISODate(first.iso) : null;
    return d ? (d.getDay() + 6) % 7 : 0;
  });

  function dayAriaLabel(day: DayChip): string {
    const date = formatDateLongPl(day.iso);
    if (day.isPast) return `${date} — termin minął`;
    const status = STATUS_LABEL[day.status];
    return status ? `${date} — ${status}` : date;
  }
</script>

<div class="select-none">
  <div
    class="grid grid-cols-7 gap-1 pb-2 text-center text-[11px] font-black uppercase tracking-wide text-slate-400 dark:text-slate-500"
    aria-hidden="true"
  >
    {#each WEEKDAY_HEADERS as wd (wd)}
      <span>{wd}</span>
    {/each}
  </div>

  <div class="grid grid-cols-7 gap-1" aria-label="Wybór dnia wizyty">
    {#each Array(leading) as _, i (`pad-${i}`)}
      <span></span>
    {/each}
    {#each days as day (day.iso)}
      {@const dayDisabled =
        day.isPast ||
        locked ||
        day.status === "none" ||
        (monthLoading && day.status === "unknown")}
      <button
        type="button"
        data-iso={day.iso}
        data-testid={`booking-day-${day.iso}`}
        disabled={dayDisabled}
        aria-pressed={selectedDate === day.iso}
        aria-label={dayAriaLabel(day)}
        class={`relative grid aspect-square place-items-center rounded-2xl text-base font-black transition ${
          dayDisabled
            ? "pointer-events-none text-slate-300 dark:text-white/20 line-through decoration-1"
            : selectedDate === day.iso
              ? "bg-[var(--accent)] text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20"
              : day.isToday
                ? "bg-slate-100 dark:bg-white/10 text-slate-950 dark:text-slate-100 ring-2 ring-brand-500/70 dark:ring-brand-400/70 hover:-translate-y-0.5 hover:bg-white dark:hover:bg-white/20"
                : "bg-slate-100 dark:bg-white/10 text-slate-950 dark:text-slate-100 hover:-translate-y-0.5 hover:bg-white dark:hover:bg-white/20"
        }`}
        onclick={() => onselect(day.iso)}
      >
        {day.dayNum}
        {#if day.status === "free" || day.status === "limited" || day.status === "scarce"}
          <span
            class={`absolute bottom-1.5 left-1/2 size-1.5 -translate-x-1/2 rounded-full ${
              selectedDate === day.iso ? "bg-white/80" : STATUS_DOT[day.status]
            }`}
          ></span>
        {/if}
      </button>
    {/each}
  </div>
</div>
