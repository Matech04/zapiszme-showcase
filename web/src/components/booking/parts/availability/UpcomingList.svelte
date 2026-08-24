<script lang="ts">
  import type { AppointmentSlotDto } from "../../../../lib/booking-openapi-client";
  import {
    STATUS_DOT,
    monthAvailabilityNotice,
    type DayChip,
  } from "../../../../lib/booking/availability";
  import { formatDatePl } from "../../../../lib/booking/format";
  import TimeSlots from "../TimeSlots.svelte";
  import InlineError from "../InlineError.svelte";

  // Wariant „najbliższe terminy" — łączy wybór dnia i godziny. Lista wolnych dni miesiąca;
  // klik dnia ładuje (w rodzicu) jego godziny i rozwija je inline pod wierszem.
  let {
    days,
    selectedDate,
    locked = false,
    slots,
    slotsLoading,
    slotsError = null,
    selectedSlot,
    syncing = false,
    durationMinutes,
    monthHasWorkingDay = true,
    monthLoading = false,
    monthError = false,
    monthClosed = false,
    monthOpensOn = null,
    canGoNext = true,
    onselectDay,
    onpickSlot,
    onretryMonth,
    onretrySlots,
  }: {
    days: DayChip[];
    selectedDate: string;
    locked?: boolean;
    slots: AppointmentSlotDto[];
    slotsLoading: boolean;
    slotsError?: string | null;
    selectedSlot: string | null;
    syncing?: boolean;
    durationMinutes?: number;
    /** Czy w podglądanym miesiącu pracownik ma JAKIKOLWIEK dzień roboczy (z `isWorkingDay`). */
    monthHasWorkingDay?: boolean;
    monthLoading?: boolean;
    monthError?: boolean;
    /** Salon jawnie zamknął ten miesiąc na zapisy. */
    monthClosed?: boolean;
    /** ISO dnia otwarcia zapisów; null = brak obiecanej daty. */
    monthOpensOn?: string | null;
    /** Czy istnieje kolejny miesiąc w oknie rezerwacji — steruje treścią komunikatu. */
    canGoNext?: boolean;
    onselectDay: (iso: string) => void;
    onpickSlot: (slot: string) => void;
    /** Ponowne pobranie dostępności miesiąca po błędzie. */
    onretryMonth?: () => void | Promise<void>;
    /** Ponowne pobranie godzin wybranego dnia po błędzie. */
    onretrySlots?: () => void | Promise<void>;
  } = $props();

  // Tylko dni z wolnymi miejscami (pomijamy przeszłość i „brak miejsc"). Dni „unknown"
  // (dane miesiąca jeszcze w locie) NIE trafiają na listę — inaczej reklamowalibyśmy
  // jako wolne dni, które za moment okażą się zajęte.
  const freeDays = $derived(
    days.filter(
      (d) =>
        !d.isPast &&
        d.status !== "none" &&
        !(monthLoading && d.status === "unknown"),
    ),
  );
  const notice = $derived(
    monthAvailabilityNotice({
      loading: monthLoading,
      error: monthError,
      hasAnyFreeDay: freeDays.length > 0,
      hasWorkingDay: monthHasWorkingDay,
      canGoNext,
      closed: monthClosed,
      opensOn: monthOpensOn,
    }),
  );

  function countLabel(day: DayChip): string {
    if (day.count == null || day.count <= 0) return "wolne terminy";
    return day.count === 1 ? "1 wolny termin" : `${day.count} wolnych terminów`;
  }
</script>

{#if locked}
  <p
    class="rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
  >
    Wybierz usługę i pracownika, aby zobaczyć najbliższe terminy.
  </p>
{:else if monthError}
  <!-- Awaria pobierania dostępności — ponawiamy samą sekcję, bez gubienia wyborów klientki. -->
  <InlineError
    message={notice ?? "Nie udało się sprawdzić dostępności w tym miesiącu."}
    onretry={onretryMonth}
    testid="booking-month-error"
  />
{:else if notice}
  <p
    class="rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
    role="status"
  >
    {notice}
  </p>
{:else if freeDays.length === 0}
  <div class="space-y-2 animate-pulse" aria-busy="true" aria-label="Szukam wolnych terminów">
    {#each [0, 1, 2] as i (i)}
      <div class="h-16 rounded-2xl bg-slate-100 dark:bg-white/10"></div>
    {/each}
  </div>
{:else}
  <div class="space-y-2">
    {#each freeDays as day (day.iso)}
      {@const isOpen = selectedDate === day.iso}
      <div
        class={`overflow-hidden rounded-2xl border transition ${
          isOpen
            ? "border-[var(--accent)] dark:border-brand-400/50"
            : "border-slate-200 dark:border-white/10"
        }`}
      >
        <button
          type="button"
          data-testid={`booking-day-${day.iso}`}
          aria-pressed={isOpen}
          aria-expanded={isOpen}
          class={`flex w-full items-center justify-between gap-3 px-4 py-3.5 text-left transition ${
            isOpen
              ? "bg-[var(--accent)] text-[var(--accent-contrast)]"
              : "bg-[var(--booking-surface)] text-slate-950 dark:text-slate-100 hover:bg-slate-50 dark:hover:bg-white/10"
          }`}
          onclick={() => onselectDay(day.iso)}
        >
          <span class="min-w-0">
            <span class="block font-black capitalize">{formatDatePl(day.iso)}</span>
            <span
              class={`mt-0.5 flex items-center gap-1.5 text-xs font-semibold ${isOpen ? "opacity-70" : "text-slate-500 dark:text-slate-400"}`}
            >
              {#if day.status === "free" || day.status === "limited" || day.status === "scarce"}
                <span
                  class={`size-1.5 rounded-full ${isOpen ? "bg-current opacity-80" : STATUS_DOT[day.status]}`}
                ></span>
              {/if}
              {countLabel(day)}
            </span>
          </span>
          <span
            class={`grid size-7 shrink-0 place-items-center rounded-full text-base font-black transition ${
              isOpen ? "rotate-180 bg-black/10 text-[var(--accent-contrast)]" : "bg-slate-100 dark:bg-white/10 text-slate-500 dark:text-slate-400"
            }`}
            aria-hidden="true">⌄</span
          >
        </button>

        {#if isOpen}
          <div class="border-t border-slate-200 dark:border-white/10 bg-white/60 dark:bg-white/5 px-3 pb-3">
            <TimeSlots
              prerequisitesMet={true}
              loading={slotsLoading}
              error={slotsError}
              {slots}
              {selectedSlot}
              {syncing}
              {durationMinutes}
              onpick={onpickSlot}
              onretry={onretrySlots}
            />
          </div>
        {/if}
      </div>
    {/each}
  </div>
{/if}
