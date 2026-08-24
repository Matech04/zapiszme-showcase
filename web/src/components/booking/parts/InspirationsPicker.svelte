<script lang="ts">
  import {
    MAX_INSPIRATION_IMAGES,
    type PendingInspirationImage,
  } from "../../../lib/booking/data-source";

  // Sekcja „Inspiracje (opcjonalnie)": klientka dorzuca zdjęcia (fryzura/paznokcie). Deferred-upload:
  // pliki trzymamy LOKALNIE w przeglądarce (podgląd z `URL.createObjectURL`) i wgrywamy dopiero PO
  // potwierdzeniu rezerwacji (robi to orkiestrator). Tu tylko zarządzamy listą + podglądem; limit
  // liczby (≤3) i typ pliku walidujemy w UI (parytet z backendem).
  let {
    pending = $bindable<PendingInspirationImage[]>([]),
    disabled = false,
  }: {
    pending?: PendingInspirationImage[];
    disabled?: boolean;
  } = $props();

  let fileInput = $state<HTMLInputElement | null>(null);
  let error = $state<string | null>(null);

  const slotsLeft = $derived(MAX_INSPIRATION_IMAGES - pending.length);
  const canAddMore = $derived(slotsLeft > 0 && !disabled);

  function onFilesSelected(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    // Reset natychmiast, żeby ten sam plik można było wybrać ponownie po usunięciu.
    input.value = "";
    if (files.length === 0) return;

    error = null;
    const added: PendingInspirationImage[] = [];
    for (const file of files) {
      if (pending.length + added.length >= MAX_INSPIRATION_IMAGES) {
        error = `Można dodać maksymalnie ${MAX_INSPIRATION_IMAGES} zdjęć.`;
        break;
      }
      if (!file.type.startsWith("image/")) {
        error = "Można dodać tylko zdjęcia.";
        continue;
      }
      added.push({
        id: crypto.randomUUID(),
        file,
        previewUrl: URL.createObjectURL(file),
      });
    }
    if (added.length > 0) {
      pending = [...pending, ...added];
    }
  }

  function removeById(id: string) {
    const target = pending.find((p) => p.id === id);
    if (target) {
      // Zwolnij blob podglądu — zanim zniknie z listy (inaczej wyciek pamięci).
      URL.revokeObjectURL(target.previewUrl);
    }
    pending = pending.filter((p) => p.id !== id);
    error = null;
  }
</script>

<div class="flex flex-col gap-2">
  <div class="flex items-baseline justify-between">
    <span class="text-sm font-medium">Inspiracje (opcjonalnie)</span>
    <span class="text-xs opacity-60">{pending.length}/{MAX_INSPIRATION_IMAGES}</span>
  </div>
  <p class="text-xs opacity-70">
    Dodaj zdjęcia, które pokażą stylistce, na czym Ci zależy (np. fryzura, paznokcie).
  </p>

  <div class="flex flex-wrap gap-2">
    {#each pending as image, index (image.id)}
      <div class="relative h-20 w-20 overflow-hidden rounded-lg border border-black/10">
        <img
          src={image.previewUrl}
          alt="Inspiracja {index + 1}"
          class="h-full w-full object-cover"
        />
        <button
          type="button"
          class="absolute right-1 top-1 flex h-5 w-5 items-center justify-center rounded-full bg-black/60 text-xs leading-none text-white"
          aria-label="Usuń zdjęcie"
          onclick={() => removeById(image.id)}
          {disabled}
        >
          ×
        </button>
      </div>
    {/each}

    {#if canAddMore}
      <button
        type="button"
        class="flex h-20 w-20 flex-col items-center justify-center rounded-lg border border-dashed border-black/25 text-xs opacity-70 hover:opacity-100 disabled:opacity-40"
        onclick={() => fileInput?.click()}
        {disabled}
      >
        <span class="text-xl leading-none">＋</span>
        <span>Dodaj</span>
      </button>
    {/if}
  </div>

  {#if error}
    <p class="text-xs text-red-600">{error}</p>
  {/if}

  <input
    bind:this={fileInput}
    type="file"
    accept="image/*"
    multiple
    class="hidden"
    onchange={onFilesSelected}
  />
</div>
