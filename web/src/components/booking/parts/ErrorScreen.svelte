<script lang="ts">
  /**
   * Ekran awarii dla klientki salonu. Zastępuje surowe komunikaty typu „Load failed" — zamiast
   * technicznego tekstu daje jedno zdanie wyjaśnienia i jedno wyjście: „Spróbuj ponownie",
   * które czyści stan/cache i przeładowuje kalendarz (patrz `hard-reset.ts`).
   *
   * `correlationId` to ten sam identyfikator, który poleciał w raporcie do Seq — klientka może
   * go podać przez telefon, a salon/my odnajdujemy dokładnie to zdarzenie w logach.
   */
  import { hardResetBookingApp } from "../../../lib/booking/hard-reset";

  let {
    title = "Wystąpił błąd",
    message,
    correlationId = null,
    variant = "page",
    retryLabel = "Spróbuj ponownie",
    onretry,
  }: {
    title?: string;
    message: string;
    correlationId?: string | null;
    /** `page` — pełny ekran (fatalna awaria), `panel` — kafelek wewnątrz karty rezerwacji. */
    variant?: "page" | "panel";
    retryLabel?: string;
    /** Domyślnie twardy reset + przeładowanie. Można podmienić na miękki retry sekcji. */
    onretry?: () => void | Promise<void>;
  } = $props();

  let busy = $state(false);

  async function retry(): Promise<void> {
    if (busy) return;
    busy = true;
    try {
      if (onretry) {
        await onretry();
      } else {
        await hardResetBookingApp();
      }
    } finally {
      // Przy domyślnej ścieżce strona i tak się przeładowuje — `busy` zdejmujemy dla wariantu
      // z własnym `onretry` (miękki retry sekcji), gdzie komponent zostaje na ekranie.
      busy = false;
    }
  }
</script>

{#snippet body()}
  <div
    class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-amber-100 dark:bg-brand-500/15 text-4xl"
    aria-hidden="true"
  >
    ⚠️
  </div>
  <h2
    class="text-2xl font-black tracking-tight text-slate-900 dark:text-slate-100 sm:text-3xl"
  >
    {title}
  </h2>
  <p class="mt-4 text-base leading-7 text-slate-600 dark:text-slate-400">
    {message}
  </p>
  <button
    type="button"
    data-testid="booking-error-retry"
    class="mt-8 inline-flex items-center gap-2 rounded-full bg-[var(--accent)] px-6 py-3.5 text-sm font-black text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20 transition hover:-translate-y-0.5 hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-60 disabled:hover:translate-y-0"
    disabled={busy}
    onclick={retry}
  >
    {#if busy}
      <span
        class="size-4 animate-spin rounded-full border-2 border-current border-t-transparent"
        aria-hidden="true"
      ></span>
      Odświeżam…
    {:else}
      {retryLabel}
    {/if}
  </button>
  {#if correlationId}
    <p class="mt-5 text-xs font-semibold text-slate-400 dark:text-slate-500">
      Kod zgłoszenia: <span class="font-mono">{correlationId}</span>
    </p>
  {/if}
{/snippet}

{#if variant === "panel"}
  <div
    class="flex flex-col items-center rounded-3xl border border-slate-200/80 dark:border-white/10 bg-[var(--booking-surface)] px-5 py-10 text-center"
    role="alert"
    data-testid="booking-error-screen"
  >
    {@render body()}
  </div>
{:else}
  <div
    class="relative flex min-h-dvh items-center justify-center overflow-hidden bg-[var(--booking-bg)] px-5"
    role="alert"
    data-testid="booking-error-screen"
  >
    <div class="pointer-events-none fixed inset-0">
      <div
        class="absolute -left-28 top-24 h-72 w-72 rounded-full bg-amber-200/50 dark:bg-brand-500/20 blur-3xl"
      ></div>
      <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/10 blur-3xl"></div>
      <div
        class="absolute bottom-0 left-1/3 h-80 w-80 rounded-full bg-orange-200/40 dark:bg-brand-600/25 blur-3xl"
      ></div>
    </div>
    <div class="relative z-10 flex max-w-md flex-col items-center text-center">
      {@render body()}
    </div>
  </div>
{/if}
