<script lang="ts">
  import type { AppointmentSlotDto } from "../../../../lib/booking-openapi-client";
  import {
    monthAvailabilityNotice,
    type DayChip,
  } from "../../../../lib/booking/availability";
  import TimeSlots from "../TimeSlots.svelte";
  import InlineError from "../InlineError.svelte";
  import StripDatePicker from "./StripDatePicker.svelte";
  import MonthGridDatePicker from "./MonthGridDatePicker.svelte";
  import UpcomingList from "./UpcomingList.svelte";
  import type { DatePickerVariant } from "./date-picker";

  // Wspólny punkt podmiany wariantów wyboru daty. Nagłówek miesiąca, nawigacja ‹ › i legenda
  // są dzielone; `strip`/`grid` dokładają pod spodem siatkę godzin (`TimeSlots`), a `list`
  // łączy datę i godzinę we własnym komponencie.
  let {
    variant,
    monthTitle,
    days,
    selectedDate,
    selectedSlot,
    locked = false,
    showLegend = true,
    hideHeading = false,
    canGoPrev = true,
    canGoNext = true,
    slots,
    slotsLoading,
    slotsError = null,
    syncing = false,
    durationMinutes,
    prerequisitesMet,
    monthHasWorkingDay = true,
    monthLoading = false,
    monthError = false,
    monthClosed = false,
    monthOpensOn = null,
    onprev,
    onnext,
    onselectDay,
    onpickSlot,
    onretryMonth,
    onretrySlots,
  }: {
    variant: DatePickerVariant;
    monthTitle: string;
    days: DayChip[];
    selectedDate: string;
    selectedSlot: string | null;
    locked?: boolean;
    showLegend?: boolean;
    /** Ukryj własny nagłówek „Termin" (kreator ma już tytuł kroku); tytuł miesiąca i nawigacja zostają. */
    hideHeading?: boolean;
    canGoPrev?: boolean;
    canGoNext?: boolean;
    slots: AppointmentSlotDto[];
    slotsLoading: boolean;
    slotsError?: string | null;
    syncing?: boolean;
    durationMinutes?: number;
    prerequisitesMet: boolean;
    /** Czy w podglądanym miesiącu pracownik ma JAKIKOLWIEK dzień roboczy (z `isWorkingDay`). */
    monthHasWorkingDay?: boolean;
    /** Dostępność miesiąca jeszcze leci — dni są „unknown", nie wolno ich podawać jako wolne. */
    monthLoading?: boolean;
    monthError?: boolean;
    /** Salon jawnie zamknął ten miesiąc na zapisy — inny komunikat niż „brak wolnych terminów". */
    monthClosed?: boolean;
    /** ISO dnia otwarcia zapisów; null = salon otworzy ręcznie, nie obiecujemy daty. */
    monthOpensOn?: string | null;
    onprev: () => void;
    onnext: () => void;
    onselectDay: (iso: string) => void;
    onpickSlot: (slot: string) => void;
    /** Ponowne pobranie dostępności miesiąca po błędzie — bez przeładowania strony. */
    onretryMonth?: () => void | Promise<void>;
    /** Ponowne pobranie godzin wybranego dnia po błędzie. */
    onretrySlots?: () => void | Promise<void>;
  } = $props();

  // Czy w podglądanym miesiącu jest JAKIKOLWIEK wolny dzień (pomijamy przeszłość i „brak miejsc").
  // Gdy nie ma (np. pracownik bez grafiku albo miesiąc w całości zajęty) — zamiast mylącego
  // „Brak wolnych godzin — wybierz inny dzień" pokazujemy jasny komunikat na poziomie miesiąca.
  const hasAnyFreeDay = $derived(
    days.some((d) => !d.isPast && d.status !== "none"),
  );
  const monthNotice = $derived(
    prerequisitesMet && !locked
      ? monthAvailabilityNotice({
          loading: monthLoading,
          error: monthError,
          hasAnyFreeDay,
          hasWorkingDay: monthHasWorkingDay,
          canGoNext,
          closed: monthClosed,
          opensOn: monthOpensOn,
        })
      : null,
  );
</script>

<div class="min-w-0">
  <div class="flex items-center justify-between gap-3">
    <div>
      {#if !hideHeading}
        <h3 class="text-lg font-black">Termin</h3>
      {/if}
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

  {#if variant === "list"}
    <div class="mt-3">
      <UpcomingList
        {days}
        {selectedDate}
        {locked}
        {slots}
        {slotsLoading}
        {slotsError}
        {selectedSlot}
        {syncing}
        {durationMinutes}
        {monthHasWorkingDay}
        {monthLoading}
        {monthError}
        {monthClosed}
        {monthOpensOn}
        {canGoNext}
        {onselectDay}
        {onpickSlot}
        {onretryMonth}
        {onretrySlots}
      />
    </div>
  {:else}
    <div class="mt-3">
      {#if variant === "grid"}
        <MonthGridDatePicker {days} {selectedDate} {locked} {monthLoading} onselect={onselectDay} />
      {:else}
        <StripDatePicker {days} {selectedDate} {locked} {monthLoading} onselect={onselectDay} />
      {/if}
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

    {#if monthError}
      <!-- Awaria dostępności miesiąca to nie „brak terminów" — dajemy ponowienie samej sekcji,
           żeby chwilowy brak zasięgu nie kasował wybranej usługi i osoby. -->
      <div class="mt-3">
        <InlineError
          message={monthNotice ?? "Nie udało się sprawdzić dostępności w tym miesiącu."}
          onretry={onretryMonth}
          testid="booking-month-error"
        />
      </div>
    {:else if monthNotice}
      <p
        class="mt-3 rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
        role="status"
      >
        {monthNotice}
      </p>
    {:else}
      <TimeSlots
        {prerequisitesMet}
        loading={slotsLoading}
        error={slotsError}
        {slots}
        {selectedSlot}
        {syncing}
        {durationMinutes}
        onpick={onpickSlot}
        onretry={onretrySlots}
      />
    {/if}
  {/if}
</div>
