<script lang="ts">
  /**
   * Błąd pojedynczej sekcji (godziny, dostępność miesiąca, lista usług/osób) — reszta kalendarza
   * działa dalej, więc zamiast pełnego ekranu awarii pokazujemy kafelek z ponowieniem TYLKO tego
   * fragmentu. Ton łagodny (bursztyn, nie czerwień): to zwykle chwilowa utrata połączenia,
   * a nie błąd klientki.
   */
  let {
    message,
    onretry,
    retryLabel = "Spróbuj ponownie",
    testid = "booking-inline-error",
  }: {
    message: string;
    /** Miękkie ponowienie tej sekcji — bez przeładowania strony i utraty wybranych opcji. */
    onretry?: () => void | Promise<void>;
    retryLabel?: string;
    testid?: string;
  } = $props();

  let busy = $state(false);

  async function retry(): Promise<void> {
    if (busy || !onretry) return;
    busy = true;
    try {
      await onretry();
    } finally {
      busy = false;
    }
  }
</script>

<div
  class="rounded-2xl border border-amber-200 dark:border-amber-400/30 bg-amber-50 dark:bg-amber-400/10 px-4 py-4 text-sm text-amber-900 dark:text-amber-200"
  role="alert"
  data-testid={testid}
>
  <p class="font-semibold">{message}</p>
  {#if onretry}
    <button
      type="button"
      data-testid={`${testid}-retry`}
      class="mt-3 inline-flex items-center gap-2 rounded-full border border-amber-300 dark:border-amber-400/40 bg-white/80 dark:bg-white/10 px-4 py-2 text-xs font-black uppercase tracking-wider text-amber-900 dark:text-amber-100 transition hover:bg-white dark:hover:bg-white/20 disabled:opacity-60"
      disabled={busy}
      onclick={retry}
    >
      {#if busy}
        <span
          class="size-3 animate-spin rounded-full border-2 border-current border-t-transparent"
          aria-hidden="true"
        ></span>
        Ponawiam…
      {:else}
        {retryLabel}
      {/if}
    </button>
  {/if}
</div>
