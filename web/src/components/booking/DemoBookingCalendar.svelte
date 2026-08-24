<script lang="ts">
  import { onMount } from "svelte";
  import BookingFlow from "./BookingFlow.svelte";
  import { mockBookingDataSource } from "../../lib/booking/mock-data-source";
  import {
    parseDatePickerSetting,
    type DatePickerSetting,
  } from "./parts/availability/date-picker";

  // Wariant układu i wyboru daty sterowane query param (?layout=wizard&date=grid&color=db2777).
  // Domyślnie otwieramy w konfiguracji produkcyjnej: kreator + responsywna data.
  let layout = $state<"single" | "wizard">("wizard");
  let dateVariant = $state<DatePickerSetting>("responsive");
  // Tryb osadzenia (iframe na landingu): chowa panel deweloperski, zostaje sam flow.
  let embed = $state(false);
  // Podgląd motywu kalendarza (jak ustawi salon w dashboardzie) — wstrzykiwany do salonInfo mocka.
  // Akcent steruje panel; tło/surface/cena dodatkowo z query: ?bg=fdf2f8&surface=ffffff&price=0d9488
  let colorPreview = $state<string | null>(null);
  let bgPreview = $state<string | null>(null);
  let surfacePreview = $state<string | null>(null);
  let pricePreview = $state<string | null>(null);

  function normalizeColor(v: string | null): string | null {
    if (!v) return null;
    const hex = v.startsWith("#") ? v : `#${v}`;
    return /^#[0-9a-fA-F]{6}$/.test(hex) ? hex : null;
  }

  // Demo używa TEGO SAMEGO BookingFlow co kalendarz klienta — różni je tylko źródło danych.
  // Doklejamy kolory motywu do salonInfo mocka, żeby zadziałał ten sam theming co u klienta.
  const dataSource = $derived.by(() => {
    const base = mockBookingDataSource();
    const accent = colorPreview;
    const bg = bgPreview;
    const surface = surfacePreview;
    const price = pricePreview;
    if (!accent && !bg && !surface && !price) return base;
    return {
      ...base,
      async loadSalon(signal: AbortSignal) {
        const bundle = await base.loadSalon(signal);
        return {
          ...bundle,
          salonInfo: bundle.salonInfo
            ? {
                ...bundle.salonInfo,
                bookingCalendarColorHex: accent ?? undefined,
                bookingCalendarBackgroundHex: bg ?? undefined,
                bookingCalendarSurfaceHex: surface ?? undefined,
                bookingCalendarPriceHex: price ?? undefined,
              }
            : bundle.salonInfo,
        };
      },
    };
  });

  onMount(() => {
    const p = new URLSearchParams(window.location.search);
    embed = p.get("embed") === "1";
    layout = p.get("layout") === "single" ? "single" : "wizard";
    dateVariant = parseDatePickerSetting(p.get("date"));
    colorPreview = normalizeColor(p.get("color"));
    bgPreview = normalizeColor(p.get("bg"));
    surfacePreview = normalizeColor(p.get("surface"));
    pricePreview = normalizeColor(p.get("price"));
  });

  function sync(): void {
    if (typeof window === "undefined") return;
    const url = new URL(window.location.href);
    url.searchParams.set("layout", layout);
    url.searchParams.set("date", dateVariant);
    if (colorPreview) url.searchParams.set("color", colorPreview.replace("#", ""));
    else url.searchParams.delete("color");
    window.history.replaceState({}, "", url.toString());
  }
  function setLayout(next: "single" | "wizard"): void {
    layout = next;
    sync();
  }
  function setDate(next: DatePickerSetting): void {
    dateVariant = next;
    sync();
  }
  function setColor(next: string | null): void {
    colorPreview = next;
    sync();
  }

  const LAYOUTS: Array<{ key: "single" | "wizard"; label: string }> = [
    { key: "single", label: "Jeden ekran" },
    { key: "wizard", label: "Kreator" },
  ];
  const DATES: Array<{ key: DatePickerSetting; label: string }> = [
    { key: "responsive", label: "Auto" },
    { key: "strip", label: "Pasek" },
    { key: "grid", label: "Siatka" },
    { key: "list", label: "Lista" },
  ];
  const COLORS: Array<{ hex: string | null; label: string }> = [
    { hex: null, label: "Domyślny" },
    { hex: "#DB2777", label: "Fuksja" },
    { hex: "#7C3AED", label: "Śliwka" },
    { hex: "#4F46E5", label: "Indygo" },
    { hex: "#0D9488", label: "Morski" },
    { hex: "#D97706", label: "Bursztyn" },
  ];

  function pillClass(active: boolean): string {
    return active
      ? "bg-slate-950 text-white dark:bg-brand-400 dark:text-slate-950"
      : "bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-white/10 dark:text-slate-300 dark:hover:bg-white/20";
  }
</script>

<!-- Panel deweloperski do porównania wariantów (tylko demo; ukryty w trybie embed). -->
{#if !embed}
<div
  class="fixed bottom-3 left-1/2 z-[60] flex -translate-x-1/2 flex-wrap items-center justify-center gap-x-3 gap-y-2 rounded-full border border-slate-200/80 bg-white/95 px-3 py-2 shadow-xl backdrop-blur dark:border-white/10 dark:bg-[#221c38]/95"
>
  <span class="pl-1 text-[10px] font-black uppercase tracking-wider text-slate-400 dark:text-slate-500">
    Układ
  </span>
  <div class="flex gap-1">
    {#each LAYOUTS as opt (opt.key)}
      <button
        type="button"
        class={`rounded-full px-3 py-1.5 text-xs font-black transition ${pillClass(layout === opt.key)}`}
        onclick={() => setLayout(opt.key)}
      >
        {opt.label}
      </button>
    {/each}
  </div>
  <span class="text-[10px] font-black uppercase tracking-wider text-slate-400 dark:text-slate-500">
    Data
  </span>
  <div class="flex gap-1">
    {#each DATES as opt (opt.key)}
      <button
        type="button"
        class={`rounded-full px-3 py-1.5 text-xs font-black transition ${pillClass(dateVariant === opt.key)}`}
        onclick={() => setDate(opt.key)}
      >
        {opt.label}
      </button>
    {/each}
  </div>
  <span class="text-[10px] font-black uppercase tracking-wider text-slate-400 dark:text-slate-500">
    Kolor
  </span>
  <div class="flex items-center gap-1.5">
    {#each COLORS as opt (opt.label)}
      <button
        type="button"
        title={opt.label}
        aria-label={opt.label}
        class={`size-6 rounded-full border transition hover:scale-110 ${
          colorPreview === opt.hex
            ? "border-slate-900 ring-2 ring-slate-900/30 dark:border-white dark:ring-white/30"
            : "border-slate-300 dark:border-white/20"
        }`}
        style={opt.hex
          ? `background-color: ${opt.hex}`
          : "background: repeating-conic-gradient(#cbd5e1 0% 25%, #fff 0% 50%) 50% / 8px 8px"}
        onclick={() => setColor(opt.hex)}
      ></button>
    {/each}
  </div>
</div>
{/if}

{#key `${layout}-${dateVariant}-${colorPreview}`}
  <BookingFlow
    {dataSource}
    salonSlug="demo"
    {layout}
    datePickerVariant={dateVariant}
  />
{/key}
