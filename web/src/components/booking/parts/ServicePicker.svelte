<script lang="ts">
  import type { BookingServiceDto } from "../../../lib/booking-openapi-client";
  import {
    formatMoneyRange,
    formatDurationRange,
  } from "../../../lib/booking/format";
  import {
    buildServiceBlocks,
    type ServiceCategoryBlock,
  } from "../../../lib/booking/service-blocks";
  import { allowedAddonIds } from "../../../lib/booking/combo";
  import type { ServiceCategoryDto } from "../../../lib/booking-openapi-client";

  let {
    services,
    serviceCategories,
    selectedServiceIds,
    employeeSelected,
    loading = false,
    pickedDuration,
    maxServices = 5,
    hideHeading = false,
    ontoggle,
  }: {
    services: BookingServiceDto[];
    serviceCategories: ServiceCategoryDto[];
    selectedServiceIds: string[];
    /** Czy wybrano pracownika — usługi pokazujemy dopiero po jego wyborze (cena/czas per-pracownik). */
    employeeSelected: boolean;
    /** Trwa ładowanie usług wybranego pracownika. */
    loading?: boolean;
    pickedDuration?: number;
    maxServices?: number;
    /** Ukryj własny nagłówek „Wybierz usługi" (kreator ma już tytuł kroku). */
    hideHeading?: boolean;
    ontoggle: (id: string) => void;
  } = $props();

  // Usługi główne (nie-dodatki) trafiają do katalogu kategorii; dodatki pokazujemy osobno,
  // dopiero gdy wybrana usługa główna na nie pozwala.
  const mainServices = $derived(services.filter((s) => s.isAddon !== true));
  const blocks = $derived(buildServiceBlocks(mainServices, serviceCategories));
  // Brak kategorii → jeden blok "_all" pokazywany płasko (bez akordeonu).
  const isFlat = $derived(blocks.length === 1 && blocks[0].key === "_all");

  const selected = $derived(new Set(selectedServiceIds));
  const atLimit = $derived(selectedServiceIds.length >= maxServices);

  // Dodatki dopuszczone przez aktualnie wybrane usługi główne.
  const availableAddons = $derived(
    (() => {
      const allowed = allowedAddonIds(selectedServiceIds, services);
      return services.filter((s) => s.isAddon === true && s.id && allowed.has(s.id));
    })(),
  );

  function durationLabel(s: BookingServiceDto): string {
    return formatDurationRange(
      s.durationInMinutes,
      s.durationMinMinutes,
      s.durationMaxMinutes,
    );
  }

  function groupKey(s: BookingServiceDto): string {
    return (s.comboGroup ?? "").trim().toLowerCase();
  }
  // Grupy wariantów już zajęte przez wybrane usługi (do oznaczania alternatyw z tej samej grupy).
  const takenGroups = $derived(
    new Set(
      services
        .filter((s) => s.id && selected.has(s.id) && groupKey(s))
        .map((s) => groupKey(s)),
    ),
  );

  function isSelected(s: BookingServiceDto): boolean {
    return !!s.id && selected.has(s.id);
  }
  // Usługa z tej samej grupy co już wybrana (ale nie ta sama) — klik ją PODMIENI.
  function isGroupAlternative(s: BookingServiceDto): boolean {
    const g = groupKey(s);
    return !!g && !isSelected(s) && takenGroups.has(g);
  }
  // Zablokowana: osiągnięto limit i usługa nie jest ani wybrana, ani podmianą w grupie.
  function isDisabled(s: BookingServiceDto): boolean {
    return atLimit && !isSelected(s) && !isGroupAlternative(s);
  }

  // Cover = pierwsze zdjęcie galerii (najmniejszy OrderIndex). Brak zdjęć → null (karta jak dotąd).
  function coverThumb(s: BookingServiceDto): string | null {
    const imgs = s.images;
    if (!imgs || imgs.length === 0) return null;
    const first = imgs
      .slice()
      .sort((a, b) => (a.orderIndex ?? 0) - (b.orderIndex ?? 0))[0];
    return first?.thumbnailUrl || first?.url || null;
  }

  // Pełna galeria (posortowana) — pokazywana większą po zaznaczeniu usługi. Preferuje pełny
  // obraz (url) nad miniaturą, bo wyświetlamy go w większym rozmiarze niż okładka na karcie.
  function galleryImages(s: BookingServiceDto) {
    const imgs = s.images;
    if (!imgs || imgs.length === 0) return [];
    return imgs.slice().sort((a, b) => (a.orderIndex ?? 0) - (b.orderIndex ?? 0));
  }

  // Które usługi mają rozwinięte szczegóły (zdjęcia + opis razem) — per id.
  let openDetails = $state<Record<string, boolean>>({});
  function hasDescription(s: BookingServiceDto): boolean {
    return !!(s.description && s.description.trim());
  }
  function isDetailsOpen(id: string): boolean {
    return openDetails[id] ?? false;
  }
  function toggleDetails(id: string): void {
    openDetails = { ...openDetails, [id]: !isDetailsOpen(id) };
  }

  let openCategories = $state<Record<string, boolean>>({});
  function isOpen(block: ServiceCategoryBlock, index: number): boolean {
    return openCategories[block.key] ?? index === 0;
  }
  function toggle(block: ServiceCategoryBlock, index: number): void {
    openCategories = {
      ...openCategories,
      [block.key]: !isOpen(block, index),
    };
  }
</script>

{#snippet serviceCard(service: BookingServiceDto)}
  {@const sel = isSelected(service)}
  {@const disabled = isDisabled(service)}
  {@const dur = durationLabel(service)}
  {@const cover = coverThumb(service)}
  {@const hasDesc = hasDescription(service)}
  {@const gallery = galleryImages(service)}
  {@const hasDetails = hasDesc || gallery.length > 0}
  {@const detailsOpen = !!service.id && isDetailsOpen(service.id)}
  <div
    class={`min-w-0 rounded-2xl border transition ${
      sel
        ? "border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-contrast)] shadow-lg shadow-brand-900/25"
        : "border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 text-slate-950 dark:text-slate-100"
    } ${disabled ? "opacity-40" : ""}`}
  >
    <button
      type="button"
      data-testid={`booking-service-${service.id}`}
      aria-pressed={sel}
      {disabled}
      class={`flex w-full min-w-0 items-center justify-between gap-3 px-4 py-3.5 text-left transition ${
        sel
          ? ""
          : "hover:-translate-y-0.5 hover:border-brand-400 hover:bg-white dark:hover:bg-white/10"
      } ${disabled ? "cursor-not-allowed hover:translate-y-0" : ""} rounded-2xl`}
      onclick={() => service.id && ontoggle(service.id)}
    >
      {#if cover}
        <img
          src={cover}
          alt=""
          loading="lazy"
          data-testid={`booking-service-cover-${service.id}`}
          class="size-14 shrink-0 rounded-xl object-cover"
        />
      {/if}
      <span class="min-w-0 flex-1">
        <span class="block min-w-0 break-words text-base font-black leading-tight"
          >{service.name ?? "Usługa"}</span
        >
        <!-- Drugi wiersz: czas trwania · cena. Cena zeszła z prawej strony pod nazwę, żeby
             przy zdjęciu + długiej nazwie + widełkach nic się nie rozjeżdżało. -->
        {#if dur || service.hidePrice !== true}
          <span class="mt-1 flex flex-wrap items-baseline gap-x-1.5 text-sm">
            {#if dur}
              <span class={`font-semibold ${sel ? "opacity-70" : "text-slate-500 dark:text-slate-400"}`}
                >{dur}</span
              >
            {/if}
            {#if dur && service.hidePrice !== true}
              <span class={sel ? "opacity-40" : "text-slate-300 dark:text-slate-600"} aria-hidden="true"
                >·</span
              >
            {/if}
            {#if service.hidePrice !== true}
              <span class={`whitespace-nowrap font-black ${sel ? "text-[var(--accent-contrast)]" : "text-[var(--booking-price)]"}`}
                >{formatMoneyRange(service.price, service.maxAmount)}</span
              >
            {/if}
          </span>
        {/if}
      </span>
      <span
        class={`grid size-6 shrink-0 place-items-center rounded-full border-2 text-xs font-black transition ${
          sel
            ? "border-white bg-white text-slate-950"
            : "border-slate-300 text-transparent dark:border-white/25"
        }`}
        aria-hidden="true">✓</span
      >
    </button>

    {#if hasDetails}
      <div class="px-4 pb-3">
        <button
          type="button"
          data-testid={`booking-service-details-toggle-${service.id}`}
          aria-expanded={detailsOpen}
          class={`text-xs font-bold underline-offset-2 hover:underline ${sel ? "opacity-80" : "text-slate-500 dark:text-slate-400"}`}
          onclick={() => service.id && toggleDetails(service.id)}
        >
          {detailsOpen ? "Ukryj szczegóły" : "Pokaż szczegóły"}
        </button>
        {#if detailsOpen}
          {#if gallery.length > 0}
            <div
              data-testid={`booking-service-gallery-${service.id}`}
              class="-mx-1 mt-2 flex snap-x gap-2 overflow-x-auto px-1 pb-1"
            >
              {#each gallery as img, i (`${img.url ?? img.thumbnailUrl}-${i}`)}
                <img
                  src={img.url || img.thumbnailUrl}
                  alt=""
                  loading="lazy"
                  class="h-32 w-32 shrink-0 snap-start rounded-xl object-cover ring-1 ring-black/10 sm:h-36 sm:w-36"
                />
              {/each}
            </div>
          {/if}
          {#if hasDesc}
            <p
              data-testid={`booking-service-desc-${service.id}`}
              class={`mt-2 whitespace-pre-line text-sm leading-relaxed ${sel ? "opacity-80" : "text-slate-600 dark:text-slate-300"}`}
            >
              {service.description}
            </p>
          {/if}
        {/if}
      </div>
    {/if}
  </div>
{/snippet}

<section>
  {#if !hideHeading || pickedDuration != null}
    <div class="flex items-center justify-between gap-4">
      {#if !hideHeading}
        <h3 class="text-lg font-black">Wybierz usługi</h3>
      {/if}
      {#if pickedDuration != null}
        <span class="ml-auto text-sm font-bold text-slate-400 dark:text-slate-500"
          >{selectedServiceIds.length} · {pickedDuration} min</span
        >
      {/if}
    </div>
  {/if}
  {#if selectedServiceIds.length > 0 && !atLimit}
    <p class="mt-1 text-xs font-semibold text-slate-400 dark:text-slate-500">
      Możesz dodać kolejne (max {maxServices}).
    </p>
  {/if}

  {#if !employeeSelected}
    <p
      data-testid="booking-service-pick-employee-first"
      class="mt-3 rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
    >
      Najpierw wybierz pracownika.
    </p>
  {:else if loading}
    <div class="mt-3 grid gap-2.5 animate-pulse" aria-busy="true" aria-label="Ładowanie usług">
      {#each [0, 1, 2] as i (i)}
        <div class="h-16 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-100 dark:bg-white/10"></div>
      {/each}
    </div>
  {:else if mainServices.length === 0}
    <p
      class="mt-3 rounded-3xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-5 text-center text-sm text-slate-600 dark:text-slate-400"
    >
      Brak usług w tym salonie.
    </p>
  {:else if isFlat}
    <!-- Salon bez kategorii: kafelki usług od razu, bez nagłówka/akordeonu. -->
    <div class="mt-3 grid gap-2.5">
      {#each blocks[0].items as service (service.id)}
        {#if service.id}
          {@render serviceCard(service)}
        {/if}
      {/each}
    </div>
  {:else}
    <!-- Kategorie spłaszczone: nagłówek + kafelki na pełną szerokość (bez zagnieżdżonego boksu). -->
    <div class="mt-2">
      {#each blocks as block, index (block.key)}
        <div class="border-t border-slate-100 first:border-t-0 dark:border-white/10">
          <button
            type="button"
            data-testid={`booking-category-toggle-${block.key}`}
            class="flex w-full items-center justify-between gap-3 py-3 text-left"
            aria-expanded={isOpen(block, index)}
            onclick={() => toggle(block, index)}
          >
            <span class="flex items-center gap-2">
              <span class="text-base font-black text-slate-950 dark:text-slate-100">{block.title}</span>
              <span
                class="rounded-full bg-slate-100 dark:bg-white/10 px-2 py-0.5 text-[11px] font-black text-slate-500 dark:text-slate-400"
                >{block.items.length}</span
              >
            </span>
            <span
              class={`grid size-7 shrink-0 place-items-center rounded-full bg-slate-100 dark:bg-white/10 text-base font-black text-slate-500 dark:text-slate-400 transition ${
                isOpen(block, index) ? "rotate-180" : ""
              }`}
              aria-hidden="true">⌄</span
            >
          </button>

          {#if isOpen(block, index)}
            <div class="grid gap-2.5 pb-3">
              {#each block.items as service (service.id)}
                {#if service.id}
                  {@render serviceCard(service)}
                {/if}
              {/each}
            </div>
          {/if}
        </div>
      {/each}
    </div>
  {/if}

  {#if availableAddons.length > 0}
    <div class="mt-4" data-testid="booking-addons">
      <h4 class="text-sm font-black uppercase tracking-wide text-slate-500 dark:text-slate-400">
        Dodatki
      </h4>
      <div class="mt-2 grid gap-2.5">
        {#each availableAddons as service (service.id)}
          {#if service.id}
            {@render serviceCard(service)}
          {/if}
        {/each}
      </div>
    </div>
  {/if}
</section>
