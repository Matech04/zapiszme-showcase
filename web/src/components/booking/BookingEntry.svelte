<script lang="ts">
  import { onMount } from 'svelte';
  import BookingPanel from './BookingPanel.svelte';
  import ManageAppointment from './ManageAppointment.svelte';
  import ErrorScreen from './parts/ErrorScreen.svelte';
  import {
    createBookingApiClient,
    getCustomBookingHomeUrl,
    isCustomBookingHost,
  } from '../../lib/booking-api-browser';
  import { classifyBookingError } from '../../lib/booking-api-errors';
  import { reportBookingError, shortCorrelationId } from '../../lib/booking/error-report';
  import { stripRetryParam } from '../../lib/booking/hard-reset';

  /** Dla ewentualnej przejściówki ze starego SSG (param szablonu). */
  const RESERVED = new Set(['_app', 'favicon.ico', '_astro']);

  type Mode = 'book' | 'manage';

  let slug = $state<string | null>(null);
  let mode = $state<Mode>('book');
  let isSalonNotFound = $state(false);
  // White-label: na customowej domenie (`rezerwacja.<domena>`) link powrotu do strony głównej klienta.
  let homeUrl = $state<string | null>(null);
  // Trwa ustalanie slugu (na customowej domenie to async resolve host→tenant). Dopóki true, NIE
  // pokazujemy komunikatu „brak adresu salonu" — inaczej migałby przed rozwiązaniem (zły UX).
  let resolving = $state(true);
  /** Awaria już na wejściu (np. nie da się rozwiązać hosta white-label) — pełny ekran błędu. */
  let entryError = $state<{ title: string; message: string } | null>(null);

  /**
   * Ostatnia siatka bezpieczeństwa: cokolwiek wybuchnie poza obsłużonym `catch` (błąd w handlerze
   * zdarzenia, odrzucona obietnica bez `.catch`), i tak trafi do logu. Bez tego takie awarie kończą
   * się wyłącznie czerwoną linią w konsoli telefonu, której nikt nigdy nie zobaczy.
   */
  function installGlobalErrorLogging(): () => void {
    const onError = (event: ErrorEvent): void => {
      reportBookingError(event.error ?? event.message, {
        operation: 'window.onerror',
        salonSlug: slug,
        extra: { source: event.filename, line: event.lineno },
      });
    };
    const onRejection = (event: PromiseRejectionEvent): void => {
      reportBookingError(event.reason, {
        operation: 'unhandledrejection',
        salonSlug: slug,
      });
    };
    window.addEventListener('error', onError);
    window.addEventListener('unhandledrejection', onRejection);
    return () => {
      window.removeEventListener('error', onError);
      window.removeEventListener('unhandledrejection', onRejection);
    };
  }

  onMount(installGlobalErrorLogging);

  onMount(async () => {
    try {
      // Ślad po „Spróbuj ponownie" (cache-bust) — po udanym starcie znika z paska adresu.
      stripRetryParam();
      const params = new URLSearchParams(window.location.search);

      // White-label: link powrotu do strony głównej klienta — TYLKO na customowej domenie
      // (`rezerwacja.<domena>`). Na zwykłym kalendarzu (zapisz.me/<slug>) pozostaje ukryty.
      homeUrl = getCustomBookingHomeUrl(window.location.hostname);

      // Tryb "zarządzaj wizytą" działa identycznie w obu wariantach (ścieżka vs customowa domena).
      if (params.get('manage') === '1') {
        mode = 'manage';
      }

      // White-label: pod `rezerwacja.<domena-klienta>` nie ma slugu w ścieżce — rozwiązujemy go
      // z hosta przez backend (host→tenant). API żyje pod `api.<domena>` (patrz getBookingApiBase).
      if (isCustomBookingHost(window.location.hostname)) {
        try {
          const info = await createBookingApiClient().publicBookingDomain_Resolve(window.location.hostname);
          slug = info.slug ?? null;
        } catch (e: unknown) {
          // Wcześniej KAŻDY błąd (także brak sieci czy 500) kończył się ekranem „nie znaleziono
          // salonu" — czyli komunikatem, że salon nie istnieje, w sytuacji gdy istnieje, tylko
          // padło łącze. Rozróżniamy: 404 = naprawdę nieznana domena, reszta = awaria.
          if (classifyBookingError(e) === 'not-found') {
            isSalonNotFound = true;
            return;
          }
          const described = reportBookingError(e, {
            operation: 'resolveCustomDomain',
            extra: { host: window.location.hostname },
          });
          entryError = { title: described.title, message: described.message };
        }
        return;
      }

      // Standardowo (zapisz.me/<slug>): slug = pierwszy segment ścieżki.
      const part = window.location.pathname.split('/').filter(Boolean)[0] ?? null;
      if (part == null || RESERVED.has(part)) {
        slug = null;
        return;
      }
      slug = decodeURIComponent(part);
    } finally {
      resolving = false;
    }
  });

  function setMode(next: Mode): void {
    mode = next;
    if (typeof window === 'undefined') return;
    const url = new URL(window.location.href);
    if (next === 'manage') {
      url.searchParams.set('manage', '1');
    } else {
      url.searchParams.delete('manage');
    }
    window.history.replaceState({}, '', url.toString());
  }
</script>

<!--
  Siatka bezpieczeństwa dla renderowania: gdy któryś komponent kalendarza wybuchnie (błąd
  w efekcie, nieoczekiwany kształt danych), Svelte zamiast pustego/rozsypanego ekranu pokazuje
  ekran błędu z „Spróbuj ponownie". Awaria idzie do konsoli i do logu backendu.
-->
<svelte:boundary
  onerror={(error: unknown) => {
    reportBookingError(error, { operation: 'render', salonSlug: slug });
  }}
>
{#if entryError}
  <ErrorScreen
    title={entryError.title}
    message={entryError.message}
    correlationId={shortCorrelationId()}
  />
{:else if slug}
  <div class="min-h-dvh">
    {#if !isSalonNotFound}
      <div class="mx-auto flex w-full max-w-7xl items-center justify-between gap-2 px-5 pt-6 sm:px-8 lg:px-10" data-testid="booking-entry-mode-toggle">
        {#if homeUrl}
          <a
            href={homeUrl}
            rel="noopener noreferrer"
            data-testid="booking-entry-home-link"
            class="inline-flex items-center gap-1.5 rounded-full border border-slate-200 dark:border-white/10 bg-white/85 dark:bg-white/5 px-4 py-2 text-xs font-bold text-slate-700 dark:text-slate-300 shadow-sm transition hover:bg-white dark:hover:bg-white/10"
          >
            <span aria-hidden="true">←</span> Strona główna
          </a>
        {:else}
          <span></span>
        {/if}
        <div class="inline-flex rounded-full border border-slate-200 dark:border-white/10 bg-white/85 dark:bg-white/5 p-1 shadow-sm">
          <button
            type="button"
            data-testid="booking-entry-mode-book"
            class={`rounded-full px-4 py-2 text-xs font-bold uppercase tracking-wider transition ${
              mode === 'book' ? 'bg-[var(--accent)] text-[var(--accent-contrast)]' : 'text-slate-700 dark:text-slate-300'
            }`}
            onclick={() => setMode('book')}
          >
            Rezerwuj
          </button>
          <button
            type="button"
            data-testid="booking-entry-mode-manage"
            class={`rounded-full px-4 py-2 text-xs font-bold uppercase tracking-wider transition ${
              mode === 'manage' ? 'bg-[var(--accent)] text-[var(--accent-contrast)]' : 'text-slate-700 dark:text-slate-300'
            }`}
            onclick={() => setMode('manage')}
          >
            Zarządzaj wizytą
          </button>
        </div>
      </div>
    {/if}

    {#if mode === 'book'}
      <BookingPanel salonSlug={slug} onsalonNotFound={() => { isSalonNotFound = true; }} />
    {:else}
      <div class="px-5 py-10 sm:px-8 lg:px-10">
        <ManageAppointment salonSlug={slug} />
      </div>
    {/if}
  </div>
{:else if resolving}
  <!-- Krótki stan ustalania slugu (zwłaszcza async resolve customowej domeny) — bez komunikatu błędu. -->
  <div class="grid min-h-dvh place-items-center" aria-busy="true" aria-label="Wczytywanie">
    <div
      class="size-8 animate-spin rounded-full border-2 border-slate-300 border-t-slate-600 dark:border-white/20 dark:border-t-white/70"
    ></div>
  </div>
{:else}
  <p class="p-4 text-slate-600 dark:text-slate-400">Brak prawidłowego adresu salonu. Użyj linku w formacie <code class="text-sm">/nazwa-salonu</code>.</p>
{/if}

  {#snippet failed()}
    <!-- Bez `reset()`: skoro render się wywrócił, ten sam stan wywróci go ponownie. Przycisk robi
         twardy reset — czyści stan rezerwacji i cache przeglądarki, po czym ładuje stronę od zera. -->
    <ErrorScreen
      title="Coś się popsuło"
      message="Nie udało się wyświetlić kalendarza. Odśwież stronę — jeśli błąd wróci, skontaktuj się z salonem."
      correlationId={shortCorrelationId()}
    />
  {/snippet}
</svelte:boundary>
