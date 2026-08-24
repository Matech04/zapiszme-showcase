<script lang="ts">
  import { tick } from "svelte";
  import {
    STATUS_DOT,
    STATUS_LABEL,
    type DayChip,
  } from "../../../lib/booking/availability";
  import { formatDateLongPl } from "../../../lib/booking/format";

  let {
    monthTitle,
    days,
    selectedDate,
    locked = false,
    showLegend = true,
    monthLoading = false,
    canGoPrev = true,
    canGoNext = true,
    onprev,
    onnext,
    onselect,
  }: {
    monthTitle: string;
    days: DayChip[];
    selectedDate: string;
    locked?: boolean;
    showLegend?: boolean;
    /** Dostępność miesiąca jeszcze leci — dni „unknown" blokujemy zamiast podawać jako wolne. */
    monthLoading?: boolean;
    canGoPrev?: boolean;
    canGoNext?: boolean;
    onprev: () => void;
    onnext: () => void;
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

<div class="min-w-0">
  <div class="flex items-center justify-between gap-3">
    <div>
      <h3 class="text-lg font-black">Termin</h3>
      <p class="text-sm font-semibold capitalize text-slate-500 dark:text-slate-400">
        {monthTitle}
      </p>
    </div>
    <div class="flex gap-1">
      <button
        type="button"
        class="grid size-9 place-items-center rounded-full border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] font-black text-slate-600 dark:text-slate-400 shadow-sm transition hover:bg-slate-50 dark:hover:bg-white/10 disabled:opacity-40"
        disabled={locked || !canGoPrev}
        aria-label="Poprzedni miesiąc"
        onclick={onprev}
      >
        ‹
      </button>
      <button
        type="button"
        class="grid size-9 place-items-center rounded-full border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] font-black text-slate-600 dark:text-slate-400 shadow-sm transition hover:bg-slate-50 dark:hover:bg-white/10 disabled:opacity-40"
        disabled={locked || !canGoNext}
        aria-label="Następny miesiąc"
        onclick={onnext}
      >
        ›
      </button>
    </div>
  </div>

  <div
    bind:this={slider}
    class="mt-3 flex w-full min-w-0 snap-x gap-2 overflow-x-auto scroll-smooth px-0.5 py-3 [-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden {locked
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

  {#if showLegend}
    <div
      class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] font-semibold text-slate-500 dark:text-slate-400"
    >
      <span class="flex items-center gap-1">
        <span class="size-1.5 rounded-full bg-emerald-500"></span>Dużo miejsc
      </span>
      <span class="flex items-center gap-1">
        <span class="size-1.5 rounded-full bg-amber-400"></span>Mało
      </span>
      <span class="flex items-center gap-1">
        <span class="size-1.5 rounded-full bg-red-500"></span>Bardzo mało
      </span>
      <span class="flex items-center gap-1">
        <span class="size-1.5 rounded-full bg-slate-300 dark:bg-white/15"></span>Brak miejsc
      </span>
    </div>
  {/if}
</div>
