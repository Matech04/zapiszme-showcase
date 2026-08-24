<script lang="ts">
  import { tick } from "svelte";
  import {
    STATUS_DOT,
    STATUS_LABEL,
    type DayChip,
  } from "../../../../lib/booking/availability";
  import { formatDateLongPl } from "../../../../lib/booking/format";

  // Wariant „pasek dni" renderuje wyłącznie rząd dni — nagłówek miesiąca, nawigację
  // i legendę dostarcza wspólny `AvailabilitySection` (dzielone przez strip/grid).
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

  let slider = $state<HTMLDivElement | null>(null);

  // Drag-to-scroll myszą (trackpad/shift+wheel obsługuje natywny overflow-x-auto).
  // Świadomie BEZ przechwytywania pionowego wheela — to blokowało scroll strony nad paskiem.
  $effect(() => {
    const el = slider;
    if (!el) return;

    let startX = 0;
    let scrollLeft = 0;
    let dragging = false;
    let dragged = false;

    function onMouseDown(e: MouseEvent) {
      dragging = true;
      dragged = false;
      startX = e.pageX - el!.offsetLeft;
      scrollLeft = el!.scrollLeft;
      document.body.style.userSelect = "none";
    }
    function onMouseMove(e: MouseEvent) {
      if (!dragging) return;
      const delta = e.pageX - el!.offsetLeft - startX;
      if (Math.abs(delta) > 4) {
        dragged = true;
        el!.scrollLeft = scrollLeft - delta;
      }
    }
    function stopDrag() {
      dragging = false;
      document.body.style.userSelect = "";
    }
    function onClickCapture(e: MouseEvent) {
      if (dragged) {
        e.stopPropagation();
        dragged = false;
      }
    }

    el.addEventListener("mousedown", onMouseDown);
    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", stopDrag);
    el.addEventListener("click", onClickCapture, true);

    return () => {
      el.removeEventListener("mousedown", onMouseDown);
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", stopDrag);
      el.removeEventListener("click", onClickCapture, true);
    };
  });

  // Przewiń wybrany dzień do środka po zmianie wyboru / przeładowaniu miesiąca.
  $effect(() => {
    const iso = selectedDate;
    const el = slider;
    if (!el) return;
    void days;
    void tick().then(() => {
      el.querySelector<HTMLElement>(`[data-iso="${iso}"]`)?.scrollIntoView({
        inline: "center",
        block: "nearest",
        behavior: "smooth",
      });
    });
  });

  function dayAriaLabel(day: DayChip): string {
    const date = formatDateLongPl(day.iso);
    if (day.isPast) return `${date} — termin minął`;
    const status = STATUS_LABEL[day.status];
    return status ? `${date} — ${status}` : date;
  }
</script>

<div
  bind:this={slider}
  class="flex w-full min-w-0 snap-x gap-2 overflow-x-auto scroll-smooth px-0.5 py-3 [-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden {locked
    ? 'cursor-default'
    : 'cursor-grab active:cursor-grabbing'}"
  aria-label="Wybór dnia wizyty"
>
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
      class={`relative w-14 shrink-0 snap-start rounded-2xl px-2 pb-3.5 pt-5 text-center transition ${
        dayDisabled
          ? "pointer-events-none opacity-25"
          : selectedDate === day.iso
            ? "bg-[var(--accent)] text-[var(--accent-contrast)] hover:-translate-y-0.5"
            : day.isToday
              ? "bg-slate-100 dark:bg-white/10 text-slate-950 dark:text-slate-100 ring-2 ring-brand-500/70 dark:ring-brand-400/70 hover:-translate-y-0.5 hover:bg-white dark:hover:bg-white/20"
              : "bg-slate-100 dark:bg-white/10 text-slate-950 dark:text-slate-100 hover:-translate-y-0.5 hover:bg-white dark:hover:bg-white/20"
      }`}
      onclick={() => onselect(day.iso)}
    >
      {#if day.isToday}
        <span
          class={`absolute left-1/2 top-0.5 -translate-x-1/2 text-[9px] font-black uppercase tracking-wide ${selectedDate === day.iso ? "text-[var(--accent-contrast)] opacity-75" : "text-slate-500 dark:text-slate-400"}`}
          >dziś</span
        >
      {/if}
      <span class="block text-xs font-black uppercase opacity-55"
        >{day.weekdayShort}</span
      >
      <span class="block text-xl font-black">{day.dayNum}</span>
      {#if day.status === "free" || day.status === "limited" || day.status === "scarce"}
        <span
          class={`absolute bottom-1 left-1/2 size-1.5 -translate-x-1/2 rounded-full ${STATUS_DOT[day.status]}`}
        ></span>
      {/if}
    </button>
  {/each}
</div>
