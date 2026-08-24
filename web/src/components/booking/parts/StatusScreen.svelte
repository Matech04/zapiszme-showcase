<script lang="ts">
  let {
    variant,
    salonName,
    siteUrl,
    message,
  }: {
    variant: "not-found" | "limit" | "paused" | "maintenance";
    salonName?: string;
    siteUrl?: string;
    message?: string | null;
  } = $props();
</script>

<div
  class="relative flex min-h-dvh items-center justify-center overflow-hidden bg-[var(--booking-bg)] px-5"
>
  <div class="pointer-events-none fixed inset-0">
    <div
      class="absolute -left-28 top-24 h-72 w-72 rounded-full bg-amber-200/50 dark:bg-brand-500/20 blur-3xl"
    ></div>
    <div
      class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/10 blur-3xl"
    ></div>
    <div
      class="absolute bottom-0 left-1/3 h-80 w-80 rounded-full bg-orange-200/40 dark:bg-brand-600/25 blur-3xl"
    ></div>
  </div>
  <div class="relative z-10 flex max-w-md flex-col items-center text-center">
    {#if variant === "limit"}
      <div
        class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-amber-100 dark:bg-brand-500/15 text-4xl"
      >
        📅
      </div>
      <h1 class="text-3xl font-black tracking-tight text-slate-900 dark:text-slate-100 sm:text-4xl">
        Rezerwacje online niedostępne
      </h1>
      <p class="mt-4 text-base leading-7 text-slate-600 dark:text-slate-400">
        Salon wykorzystał limit rezerwacji online w tym miesiącu. Aby umówić
        wizytę, skontaktuj się bezpośrednio z salonem.
      </p>
      {#if salonName}
        <p class="mt-3 text-sm font-bold text-slate-800 dark:text-slate-200">{salonName}</p>
      {/if}
    {:else if variant === "paused"}
      <div
        class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-amber-100 dark:bg-brand-500/15 text-4xl"
      >
        ⏸️
      </div>
      <h1 class="text-3xl font-black tracking-tight text-slate-900 dark:text-slate-100 sm:text-4xl">
        Rezerwacje chwilowo wstrzymane
      </h1>
      <p class="mt-4 text-base leading-7 text-slate-600 dark:text-slate-400">
        {message?.trim()
          ? message
          : "Rezerwacje online są chwilowo wstrzymane. Aby umówić wizytę, skontaktuj się bezpośrednio z salonem."}
      </p>
      {#if salonName}
        <p class="mt-3 text-sm font-bold text-slate-800 dark:text-slate-200">{salonName}</p>
      {/if}
    {:else if variant === "maintenance"}
      <div
        class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-amber-100 dark:bg-brand-500/15 text-4xl"
      >
        🔧
      </div>
      <h1 class="text-3xl font-black tracking-tight text-slate-900 dark:text-slate-100 sm:text-4xl">
        Trwają prace serwisowe
      </h1>
      <p class="mt-4 text-base leading-7 text-slate-600 dark:text-slate-400">
        {message?.trim()
          ? message
          : "Rezerwacje online są chwilowo niedostępne — trwają prace techniczne. Aby umówić wizytę, skontaktuj się bezpośrednio z salonem."}
      </p>
      {#if salonName}
        <p class="mt-3 text-sm font-bold text-slate-800 dark:text-slate-200">{salonName}</p>
      {/if}
    {:else}
      <div
        class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-slate-100 dark:bg-white/10 text-4xl"
      >
        ✂️
      </div>
      <h1 class="text-3xl font-black tracking-tight text-slate-900 dark:text-slate-100 sm:text-4xl">
        Nie znaleziono salonu
      </h1>
      <p class="mt-4 text-base leading-7 text-slate-600 dark:text-slate-400">
        Salon o tym adresie nie istnieje lub nie jest już aktywny. Sprawdź adres
        w przeglądarce albo wróć na stronę główną.
      </p>
      {#if siteUrl}
        <a
          href={siteUrl}
          class="mt-8 inline-flex items-center gap-2 rounded-full bg-slate-950 px-6 py-3 text-sm font-bold text-white shadow-md transition hover:bg-slate-800"
        >
          ← Wróć na zapisz.me
        </a>
      {/if}
    {/if}
  </div>
</div>
