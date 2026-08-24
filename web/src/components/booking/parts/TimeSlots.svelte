<script lang="ts">
  import type { AppointmentSlotDto } from "../../../lib/booking-openapi-client";
  import { formatSlotRange } from "../../../lib/booking/format";
  import {
    groupSlotsByTimeOfDay,
    hasPreferredSlots,
    sortSlots,
    splitPreferred,
  } from "../../../lib/booking/slots";
  import InlineError from "./InlineError.svelte";

  let {
    prerequisitesMet,
    loading,
    error = null,
    slots,
    selectedSlot,
    syncing = false,
    durationMinutes,
    emptyMessage = "Brak wolnych godzin — wybierz inny dzień lub pracownika.",
    onpick,
    onretry,
  }: {
    prerequisitesMet: boolean;
    loading: boolean;
    error?: string | null;
    slots: AppointmentSlotDto[];
    selectedSlot: string | null;
    syncing?: boolean;
    durationMinutes?: number;
    /** Komunikat przy zerze godzin. Domyślny sugeruje zmianę pracownika — nie wszędzie
     * jest to możliwe (np. modal przekładania wizyty ma pracownika ustalonego). */
    emptyMessage?: string;
    onpick: (slot: string) => void;
    /** Ponowne pobranie godzin bez przeładowania strony (zachowuje wybór usługi/osoby/dnia). */
    onretry?: () => void | Promise<void>;
  } = $props();

  const sorted = $derived(sortSlots(slots));
  const preferred = $derived(hasPreferredSlots(slots));
  const split = $derived(splitPreferred(sorted));
  const groups = $derived(groupSlotsByTimeOfDay(sorted));

  function slotTitle(slot: string): string {
    return durationMinutes
      ? `Wolny termin ${formatSlotRange(slot, durationMinutes)}`
      : `Wolny termin ${slot}`;
  }
</script>

<div class="mt-4">
  {#if !prerequisitesMet}
    <p
      class="rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
    >
      Wybierz usługę i pracownika, aby zobaczyć godziny.
    </p>
  {:else if loading}
    <div
      class="space-y-4 animate-pulse"
      aria-busy="true"
      aria-label="Szukam wolnych terminów"
    >
      {#each [0, 1, 2] as group (group)}
        <div class="space-y-2">
          <div class="h-3 w-24 rounded-full bg-slate-100 dark:bg-white/10"></div>
          <div class="grid grid-cols-3 gap-2 sm:grid-cols-4">
            {#each [0, 1, 2, 3, 4, 5, 6, 7] as i (i)}
              <div class="h-10 rounded-full bg-slate-100 dark:bg-white/10"></div>
            {/each}
          </div>
        </div>
      {/each}
    </div>
  {:else if error}
    <InlineError message={error} {onretry} testid="booking-slots-error" />
  {:else if sorted.length === 0}
    <p
      class="rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
    >
      {emptyMessage}
    </p>
  {:else if preferred}
    {@const accent = "bg-brand-50 dark:bg-brand-500/10 text-slate-950 dark:text-slate-100 ring-1 ring-brand-300 dark:ring-brand-400 hover:ring-brand-400"}
    <div class="space-y-5">
      <div>
        <div class="mb-2 flex items-center gap-2">
          <p class="text-xs font-black uppercase tracking-wider text-brand-600 dark:text-brand-300">
            Polecane godziny
          </p>
          <span
            class="rounded-full bg-brand-100 dark:bg-brand-500/15 px-2 py-0.5 text-[10px] font-black text-brand-700 dark:text-brand-300"
            >{split.preferred.length}</span
          >
        </div>
        <div class="grid grid-cols-3 gap-2 sm:grid-cols-4">
          {#each split.preferred as s (s.slot)}
            <button
              type="button"
              title={slotTitle(s.slot ?? "")}
              aria-pressed={selectedSlot === s.slot}
              class={`rounded-full px-4 py-3 text-sm font-black transition hover:-translate-y-0.5 ${
                selectedSlot === s.slot
                  ? "bg-[var(--accent)] text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20"
                  : accent
              } ${syncing && selectedSlot === s.slot ? "pointer-events-none opacity-60" : ""}`}
              data-testid={`booking-slot-${s.slot}`}
              onclick={() => onpick(s.slot ?? "")}
            >
              {s.slot}
            </button>
          {/each}
        </div>
      </div>
      {#if split.other.length > 0}
        <div>
          <div class="mb-2 flex items-center gap-2">
            <p class="text-xs font-black uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Pozostałe terminy
            </p>
            <span
              class="rounded-full bg-slate-100 dark:bg-white/10 px-2 py-0.5 text-[10px] font-black text-slate-600 dark:text-slate-400"
              >{split.other.length}</span
            >
          </div>
          <div class="grid grid-cols-3 gap-2 sm:grid-cols-4">
            {#each split.other as s (s.slot)}
              <button
                type="button"
                title={slotTitle(s.slot ?? "")}
                aria-pressed={selectedSlot === s.slot}
                class={`rounded-full px-4 py-3 text-sm font-black transition hover:-translate-y-0.5 ${
                  selectedSlot === s.slot
                    ? "bg-[var(--accent)] text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20"
                    : "bg-[var(--booking-surface)] text-slate-950 dark:text-slate-100 ring-1 ring-slate-200 dark:ring-white/10 hover:ring-slate-300"
                } ${syncing && selectedSlot === s.slot ? "pointer-events-none opacity-60" : ""}`}
                data-testid={`booking-slot-${s.slot}`}
                onclick={() => onpick(s.slot ?? "")}
              >
                {s.slot}
              </button>
            {/each}
          </div>
        </div>
      {/if}
    </div>
  {:else}
    <div class="space-y-4">
      {#each groups as group (group.key)}
        <div>
          <div class="mb-2 flex items-center gap-2">
            <p class="text-xs font-black uppercase tracking-wider text-slate-500 dark:text-slate-400">
              {group.title}
            </p>
            <span
              class="rounded-full bg-slate-100 dark:bg-white/10 px-2 py-0.5 text-[10px] font-black text-slate-600 dark:text-slate-400"
              >{group.items.length}</span
            >
          </div>
          <div class="grid grid-cols-3 gap-2 sm:grid-cols-4">
            {#each group.items as s (s.slot)}
              <button
                type="button"
                title={slotTitle(s.slot ?? "")}
                aria-pressed={selectedSlot === s.slot}
                class={`rounded-full px-4 py-3 text-sm font-black transition hover:-translate-y-0.5 ${
                  selectedSlot === s.slot
                    ? "bg-[var(--accent)] text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20"
                    : "bg-[var(--booking-surface)] text-slate-950 dark:text-slate-100 ring-1 ring-slate-200 dark:ring-white/10 hover:ring-slate-300"
                } ${syncing && selectedSlot === s.slot ? "pointer-events-none opacity-60" : ""}`}
                data-testid={`booking-slot-${s.slot}`}
                onclick={() => onpick(s.slot ?? "")}
              >
                {s.slot}
              </button>
            {/each}
          </div>
        </div>
      {/each}
    </div>
  {/if}
</div>
