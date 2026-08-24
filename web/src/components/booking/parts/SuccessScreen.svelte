<script lang="ts">
  import { formatDatePl, formatSlotRange } from "../../../lib/booking/format";

  let {
    serviceName,
    employeeName,
    selectedDate,
    selectedSlot,
    durationMinutes,
    requiresManualConfirmation = false,
  }: {
    serviceName?: string;
    employeeName?: string;
    selectedDate: string;
    selectedSlot: string | null;
    durationMinutes?: number;
    /** true → salon potwierdza ręcznie: pokaż „rezerwacja przyjęta, czeka na potwierdzenie". */
    requiresManualConfirmation?: boolean;
  } = $props();

  const SITE_URL = import.meta.env.PUBLIC_SITE_URL ?? "https://zapisz.me";
</script>

<div
  class="fixed inset-0 z-40 grid place-items-center bg-[var(--booking-bg)] px-5 py-8"
  role="status"
  aria-live="polite"
>
  <div class="pointer-events-none fixed inset-0 overflow-hidden">
    <div
      class="absolute -left-24 top-20 h-72 w-72 rounded-full bg-brand-200/50 dark:bg-brand-500/20 blur-3xl"
    ></div>
    <div
      class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/10 blur-3xl"
    ></div>
    <div
      class="absolute bottom-0 left-1/3 h-80 w-80 rounded-full bg-brand-300/40 dark:bg-brand-600/25 blur-3xl"
    ></div>
  </div>

  <div
    class="relative w-full max-w-2xl rounded-4xl border border-white/80 dark:border-white/10 bg-white/85 dark:bg-white/5 p-7 text-center shadow-2xl shadow-slate-900/10 backdrop-blur sm:p-10"
  >
    {#if requiresManualConfirmation}
      <div
        class="mx-auto grid size-16 place-items-center rounded-full bg-amber-100 dark:bg-amber-500/15 text-3xl font-black text-amber-700 dark:text-amber-300"
      >
        ⏳
      </div>
      <p class="mt-6 text-sm font-black uppercase tracking-[0.2em] text-brand-700 dark:text-brand-300">
        Rezerwacja przyjęta
      </p>
      <h2 class="mt-3 text-4xl font-black tracking-tight text-slate-950 dark:text-slate-100 sm:text-5xl">
        Dziękujemy za rezerwację.
      </h2>
      <p class="mx-auto mt-5 max-w-xl text-base leading-7 text-slate-600 dark:text-slate-400">
        Po potwierdzeniu wizyty przez salon dostaniesz powiadomienie SMS.
      </p>
    {:else}
      <div
        class="mx-auto grid size-16 place-items-center rounded-full bg-emerald-100 dark:bg-emerald-500/15 text-3xl font-black text-emerald-800 dark:text-emerald-300"
      >
        ✓
      </div>
      <p class="mt-6 text-sm font-black uppercase tracking-[0.2em] text-brand-700 dark:text-brand-300">
        Rezerwacja potwierdzona
      </p>
      <h2 class="mt-3 text-4xl font-black tracking-tight text-slate-950 dark:text-slate-100 sm:text-5xl">
        Wizyta została umówiona.
      </h2>
    {/if}
    <p class="mx-auto mt-5 max-w-xl text-base leading-7 text-slate-600 dark:text-slate-400">
      {serviceName} u {employeeName ?? "pracownika"} · {formatDatePl(
        selectedDate,
      )} o {selectedSlot
        ? formatSlotRange(selectedSlot, durationMinutes)
        : ""}.
    </p>

    <div class="mt-8 border-t border-slate-900/10 dark:border-white/10 pt-5">
      <a
        href={`${SITE_URL}/?utm_source=booking&utm_medium=success&utm_campaign=powered_by`}
        target="_blank"
        rel="noopener"
        class="inline-flex items-center gap-1.5 text-sm font-semibold text-slate-500 dark:text-slate-400 transition hover:text-slate-900 dark:hover:text-slate-200"
      >
        Masz salon? Przyjmuj zapisy 24/7 z
        <span class="font-black text-slate-700 dark:text-slate-200">✦ Zapisz.me</span>
        →
      </a>
    </div>
  </div>
</div>
