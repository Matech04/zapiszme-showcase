<script lang="ts">
  import type {
    BookingEmployeeDto,
    BookingServiceDto,
  } from "../../../lib/booking-openapi-client";
  import {
    formatDatePl,
    formatMoneyRange,
    formatSlotRange,
  } from "../../../lib/booking/format";
  import { comboPriceRange } from "../../../lib/booking/combo";

  let {
    pickedServices,
    comboDurationMinutes,
    pickedEmployee,
    selectedDate,
    selectedSlot,
    primaryDisabled,
    primaryBusy = false,
    verifiedContactLabel = null,
    hideHeading = false,
    onconfirm,
    onchange = undefined,
  }: {
    pickedServices: BookingServiceDto[];
    comboDurationMinutes: number;
    pickedEmployee: BookingEmployeeDto | undefined;
    selectedDate: string;
    selectedSlot: string | null;
    primaryDisabled: boolean;
    primaryBusy?: boolean;
    /** Gdy ustawione: pokaż „Zarezerwuj jako …" (pominięcie OTP dla zweryfikowanego kontaktu). */
    verifiedContactLabel?: string | null;
    /** Ukryj własny nagłówek „Podsumowanie" (kreator ma już tytuł kroku). */
    hideHeading?: boolean;
    onconfirm: () => void;
    /** „Zmień" — zrezygnuj z pominięcia OTP i przejdź zwykłą ścieżką weryfikacji. */
    onchange?: (() => void) | undefined;
  } = $props();

  // Suma cen combo (od–do gdy któraś usługa ma widełki) — wspólny, przetestowany helper.
  const price = $derived(comboPriceRange(pickedServices));
  // Gdy któraś usługa ukrywa cenę, łącznej kwoty nie da się sensownie pokazać.
  const priceHidden = $derived(price.priceHidden);
  const hasPriceRange = $derived(price.hasRange && !price.priceHidden);
  const priceLabel = $derived(
    pickedServices.length === 0 || priceHidden
      ? "—"
      : formatMoneyRange(
          { amount: price.min, currency: price.currency },
          price.hasRange ? price.max : undefined,
        ),
  );

  const servicesLabel = $derived(
    pickedServices.length === 0
      ? "Wybierz usługę"
      : pickedServices.map((s) => s.name ?? "Usługa").join(" + "),
  );

  const termLabel = $derived(
    selectedSlot
      ? `${formatDatePl(selectedDate)} · ${formatSlotRange(selectedSlot, comboDurationMinutes || undefined)}`
      : formatDatePl(selectedDate),
  );
</script>

<aside class="rounded-3xl bg-[var(--booking-bg)] p-5 2xl:p-6">
  {#if !hideHeading}
    <p class="text-sm font-black uppercase tracking-[0.2em] text-brand-700 dark:text-brand-300">
      Podsumowanie
    </p>
  {/if}
  <div class="mt-4 space-y-4">
    <div class="min-w-0">
      {#if !priceHidden}
        <p class="text-3xl font-black 2xl:text-4xl">
          {priceLabel}
        </p>
      {/if}
      <p class="mt-2 min-w-0 break-words font-black 2xl:text-lg">
        {servicesLabel}
      </p>
      <p class="text-sm font-semibold text-slate-500 dark:text-slate-400">
        {pickedServices.length > 0 ? `${comboDurationMinutes} min` : "—"}
      </p>
      {#if hasPriceRange}
        <p class="mt-1 text-xs font-semibold text-slate-400 dark:text-slate-500">
          Ostateczna cena ustalana na miejscu.
        </p>
      {/if}
    </div>

    <div class="grid gap-2 text-sm sm:grid-cols-2">
      <p class="rounded-2xl bg-[var(--booking-surface)] p-4">
        <span
          class="block text-xs font-black uppercase tracking-wide text-slate-400 dark:text-slate-500"
          >Pracownik</span
        >
        <span class="block min-w-0 break-words font-bold"
          >{pickedEmployee
            ? `${pickedEmployee.firstName ?? ""} ${pickedEmployee.lastName ?? ""}`
            : "Wybierz pracownika"}</span
        >
      </p>
      <p class="rounded-2xl bg-[var(--booking-surface)] p-4">
        <span
          class="block text-xs font-black uppercase tracking-wide text-slate-400 dark:text-slate-500"
          >Termin</span
        >
        <span class="block min-w-0 break-words font-bold">{termLabel}</span>
      </p>
    </div>

    <div>
      <button
        type="button"
        data-testid="booking-footer-primary"
        class="w-full rounded-full bg-[var(--accent)] px-6 py-4 text-base font-black text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20 transition hover:-translate-y-0.5 hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:translate-y-0"
        disabled={primaryDisabled}
        onclick={onconfirm}
      >
        {primaryBusy
          ? "Rezerwuję slot…"
          : verifiedContactLabel
            ? "Zarezerwuj wizytę"
            : "Potwierdź wizytę"}
      </button>
      {#if verifiedContactLabel}
        <p class="mt-3 text-xs leading-5 text-slate-500 dark:text-slate-400">
          Zarezerwujesz jako <span class="font-bold text-slate-700 dark:text-slate-300">{verifiedContactLabel}</span> — bez kodu.
          {#if onchange}
            <button
              type="button"
              data-testid="booking-footer-change-contact"
              class="font-bold text-slate-700 underline dark:text-slate-300"
              onclick={onchange}
            >Zmień</button>
          {/if}
        </p>
      {:else}
        <p class="mt-3 text-xs leading-5 text-slate-500 dark:text-slate-400">
          Wizytę potwierdzisz kodem.
        </p>
      {/if}
    </div>
  </div>
</aside>
