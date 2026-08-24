<script lang="ts">
  import { formatCountdown } from "../../../lib/booking/format";

  let {
    verifyChannel,
    requireName = false,
    collectInstagram = false,
    termsOfService = null,
    contact = $bindable(""),
    firstName = $bindable(""),
    lastName = $bindable(""),
    instagramNick = $bindable(""),
    code = $bindable(""),
    returning = $bindable(false),
    busy = false,
    sent = false,
    error = null,
    cooldownSec = 0,
    holdRemainingSec = null,
    onsend,
    onverify,
    onedit,
    onclose,
  }: {
    verifyChannel: number;
    /** Salon wymaga imienia i nazwiska — pokaż i waliduj dodatkowe pola. */
    requireName?: boolean;
    /** Salon zbiera opcjonalny nick na Instagramie — pokaż dodatkowe pole (zawsze opcjonalne). */
    collectInstagram?: boolean;
    /** Treść regulaminu salonu; null/puste = brak własnego (pokazujemy domyślny link do /regulamin). */
    termsOfService?: string | null;
    contact?: string;
    firstName?: string;
    lastName?: string;
    instagramNick?: string;
    code?: string;
    /** „Umawiam ponownie" — stały klient: chowamy pola imienia, identyfikacja po kontakcie + OTP. */
    returning?: boolean;
    busy?: boolean;
    sent?: boolean;
    error?: string | null;
    cooldownSec?: number;
    /** Sekundy do wygaśnięcia blokady slotu; null = brak licznika. */
    holdRemainingSec?: number | null;
    onsend: () => void;
    onverify: () => void;
    /** Powrót z trybu „wpisz kod" do edycji danych (np. zmiana numeru). */
    onedit: () => void;
    onclose: () => void;
  } = $props();

  const isPhone = $derived(verifyChannel === 0);
  const expired = $derived(holdRemainingSec !== null && holdRemainingSec <= 0);
  // Salon ma własny regulamin → pokazujemy jego treść (rozwijaną) zamiast linku do /regulamin.
  const hasSalonTerms = $derived(!!termsOfService && termsOfService.trim().length > 0);

  // Czy treść regulaminu salonu jest rozwinięta (domyślnie zwinięta, by nie zdominować dialogu).
  let termsExpanded = $state(false);

  // RODO: świadoma akceptacja (checkbox) zamiast dorozumianej zgody — blokuje wysłanie kodu.
  let consentAccepted = $state(false);

  // Po przejściu do trybu kodu ustaw kursor od razu w polu kodu (mniej tarcia).
  function focusOnMount(node: HTMLInputElement) {
    node.focus();
  }
</script>

<div
  class="fixed inset-0 z-50 grid place-items-center bg-slate-950/55 px-3 py-6 backdrop-blur-sm sm:px-5 sm:py-8"
  role="dialog"
  aria-modal="true"
  aria-labelledby="booking-otp-title"
>
  <div
    class="max-h-[calc(100dvh-1.5rem)] w-full max-w-lg overflow-y-auto rounded-3xl bg-[var(--booking-surface)] p-5 shadow-2xl shadow-slate-950/25 sm:max-h-[calc(100dvh-4rem)] sm:rounded-4xl sm:p-7"
  >
    <div class="flex items-start justify-between gap-4">
      <div>
        <p class="text-sm font-black uppercase tracking-[0.2em] text-brand-700 dark:text-brand-300">
          Potwierdzenie
        </p>
        <h2
          id="booking-otp-title"
          class="mt-2 text-3xl font-black tracking-tight text-slate-950 dark:text-slate-100"
        >
          Potwierdź wizytę
        </h2>
      </div>
      <button
        type="button"
        class="grid size-9 shrink-0 place-items-center rounded-full bg-slate-100 dark:bg-white/10 text-xl font-black text-slate-500 dark:text-slate-400 transition hover:bg-slate-200 hover:text-slate-950 dark:hover:bg-white/20 dark:hover:text-white sm:size-10"
        aria-label="Zamknij okno potwierdzenia"
        onclick={onclose}
      >
        ×
      </button>
    </div>

    {#if holdRemainingSec !== null}
      {#if expired}
        <p
          class="mt-4 rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm font-bold text-red-800"
          role="status"
        >
          Czas na potwierdzenie minął — slot został zwolniony. Zamknij okno i
          wybierz godzinę ponownie.
        </p>
      {:else}
        <p
          class="mt-4 inline-flex items-center gap-2 rounded-full bg-brand-50 dark:bg-brand-500/10 px-3 py-1.5 text-sm font-bold text-brand-800 dark:text-brand-300"
          role="status"
          aria-live="polite"
        >
          <span class="size-1.5 rounded-full bg-brand-500"></span>
          Slot zarezerwowany dla Ciebie jeszcze przez {formatCountdown(
            holdRemainingSec,
          )}
        </p>
      {/if}
    {/if}

    {#if !sent}
      <p class="mt-4 leading-7 text-slate-600 dark:text-slate-400">
        {#if isPhone}
          Podaj numer telefonu. Wyślemy na niego kod potwierdzający rezerwację.
        {:else}
          Podaj adres e-mail. Wyślemy na niego kod potwierdzający rezerwację.
        {/if}
      </p>

      {#if requireName || collectInstagram}
        <label
          class="mt-5 flex items-start gap-2.5 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-3 text-sm font-bold text-slate-700 dark:text-slate-300"
        >
          <input
            type="checkbox"
            data-testid="booking-otp-returning"
            class="mt-0.5 size-4 shrink-0 rounded border-slate-300 text-slate-950 focus:ring-slate-950"
            bind:checked={returning}
          />
          <span>
            Umawiam się ponownie
            <span class="block font-medium text-slate-500 dark:text-slate-400">
              Rozpoznamy Cię po {isPhone ? "numerze telefonu" : "adresie e-mail"} — nie musisz podawać {requireName
                ? "imienia"
                : "dodatkowych danych"}.
            </span>
          </span>
        </label>
      {/if}

      {#if requireName && !returning}
        <div class="mt-5 grid gap-4 sm:grid-cols-2">
          <label
            class="grid gap-2 text-sm font-bold text-slate-700 dark:text-slate-300"
            for="otp-first-name"
          >
            Imię
            <input
              id="otp-first-name"
              data-testid="booking-otp-first-name"
              type="text"
              class="w-full min-w-0 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-3 text-base outline-none transition focus:border-slate-950 focus:bg-white"
              bind:value={firstName}
              placeholder="Anna"
              autocomplete="given-name"
              maxlength="100"
            />
          </label>
          <label
            class="grid gap-2 text-sm font-bold text-slate-700 dark:text-slate-300"
            for="otp-last-name"
          >
            Nazwisko
            <input
              id="otp-last-name"
              data-testid="booking-otp-last-name"
              type="text"
              class="w-full min-w-0 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-3 text-base outline-none transition focus:border-slate-950 focus:bg-white"
              bind:value={lastName}
              placeholder="Kowalska"
              autocomplete="family-name"
              maxlength="100"
            />
          </label>
        </div>
      {/if}

      <label
        class="mt-6 grid gap-2 text-sm font-bold text-slate-700 dark:text-slate-300"
        for="otp-contact"
      >
        {isPhone ? "Telefon" : "E-mail"}
        <input
          id="otp-contact"
          data-testid="booking-otp-contact"
          type={isPhone ? "tel" : "email"}
          class="w-full min-w-0 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-3 text-base outline-none transition focus:border-slate-950 focus:bg-white"
          bind:value={contact}
          placeholder={isPhone ? "500 600 700" : "ja@przyklad.pl"}
          autocomplete={isPhone ? "tel" : "email"}
        />
      </label>

      {#if collectInstagram && !returning}
        <label
          class="mt-6 grid gap-2 text-sm font-bold text-slate-700 dark:text-slate-300"
          for="otp-instagram"
        >
          Instagram <span class="font-medium text-slate-400">(opcjonalnie)</span>
          <div class="relative">
            <span
              class="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-base font-bold text-slate-400"
              >@</span
            >
            <input
              id="otp-instagram"
              data-testid="booking-otp-instagram"
              type="text"
              class="w-full min-w-0 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 py-3 pl-9 pr-4 text-base outline-none transition focus:border-slate-950 focus:bg-white"
              bind:value={instagramNick}
              placeholder="twojnick"
              autocapitalize="none"
              autocomplete="off"
              spellcheck="false"
              maxlength="30"
            />
          </div>
        </label>
      {/if}

      {#if hasSalonTerms}
        <!-- Salon ma własny regulamin: pokazujemy jego treść (rozwijaną, scrollowalną) zamiast linku. -->
        <div
          class="mt-5 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5"
        >
          <button
            type="button"
            data-testid="booking-otp-terms-toggle"
            class="flex w-full items-center justify-between gap-3 px-4 py-3 text-left text-sm font-bold text-slate-700 dark:text-slate-300"
            aria-expanded={termsExpanded}
            onclick={() => (termsExpanded = !termsExpanded)}
          >
            <span>Regulamin salonu</span>
            <span class="text-xs font-bold text-slate-500 dark:text-slate-400">
              {termsExpanded ? "Zwiń" : "Rozwiń"}
            </span>
          </button>
          {#if termsExpanded}
            <div
              data-testid="booking-otp-terms-content"
              class="max-h-56 overflow-y-auto whitespace-pre-wrap border-t border-slate-200 dark:border-white/10 px-4 py-3 text-sm leading-6 text-slate-600 dark:text-slate-400"
            >
              {termsOfService}
            </div>
          {/if}
        </div>
      {/if}

      <label
        class="mt-3 flex items-start gap-2.5 text-xs leading-5 text-slate-500 dark:text-slate-400"
      >
        <input
          type="checkbox"
          data-testid="booking-otp-consent"
          class="mt-0.5 size-4 shrink-0 rounded border-slate-300 text-slate-950 focus:ring-slate-950"
          bind:checked={consentAccepted}
        />
        <span>
          Akceptuję
          <a
            href="/polityka-prywatnosci"
            target="_blank"
            rel="noopener"
            class="font-bold text-slate-700 dark:text-slate-300 underline underline-offset-2 hover:text-slate-950 dark:hover:text-white"
            >Politykę prywatności</a
          >
          {#if hasSalonTerms}
            oraz regulamin salonu (powyżej).
          {:else}
            i
            <a
              href="/regulamin"
              target="_blank"
              rel="noopener"
              class="font-bold text-slate-700 dark:text-slate-300 underline underline-offset-2 hover:text-slate-950 dark:hover:text-white"
              >Regulamin</a
            >.
          {/if}
        </span>
      </label>

      <div class="mt-4">
        <button
          type="button"
          data-testid="booking-otp-send"
          class="rounded-full border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-5 py-3 text-sm font-bold text-slate-700 dark:text-slate-300 shadow-sm transition hover:bg-slate-100 dark:hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-60"
          disabled={busy || cooldownSec > 0 || expired || !consentAccepted}
          onclick={onsend}
        >
          {busy ? "Wysyłanie…" : cooldownSec > 0 ? `Odczekaj ${cooldownSec} s` : "Wyślij kod"}
        </button>
      </div>

      {#if cooldownSec > 0}
        <p class="mt-2 text-xs text-slate-500 dark:text-slate-400" role="status">
          Przed kolejną próbą odczekaj <strong>{cooldownSec}</strong> s.
        </p>
      {/if}
    {:else}
      <!-- Tryb „kod wysłany": dane są zablokowane, edytowalne jest tylko pole kodu.
           Zmiana numeru wymaga świadomego kliknięcia „Zmień" (powrót do edycji). -->
      <div
        class="mt-4 flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-3"
      >
        <p class="min-w-0 text-sm text-slate-600 dark:text-slate-400">
          Kod wysłany na
          <span class="break-all font-bold text-slate-900 dark:text-slate-100">{contact}</span>
        </p>
        <button
          type="button"
          data-testid="booking-otp-edit-contact"
          class="shrink-0 text-sm font-bold text-slate-700 underline underline-offset-2 hover:text-slate-950 disabled:opacity-50 dark:text-slate-300 dark:hover:text-white"
          disabled={busy}
          onclick={onedit}
        >
          Zmień {isPhone ? "numer" : "e-mail"}
        </button>
      </div>

      <label
        class="mt-5 grid gap-2 text-sm font-bold text-slate-700 dark:text-slate-300"
        for="otp-code"
      >
        Wpisz kod z {isPhone ? "SMS-a" : "e-maila"}
        <input
          id="otp-code"
          data-testid="booking-otp-code"
          type="text"
          inputmode="numeric"
          maxlength="8"
          class="w-full min-w-0 rounded-2xl border border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-3 text-center text-lg font-black tracking-[0.2em] outline-none transition focus:border-slate-950 focus:bg-white sm:text-xl sm:tracking-[0.28em]"
          bind:value={code}
          autocomplete="one-time-code"
          use:focusOnMount
        />
      </label>

      <button
        type="button"
        data-testid="booking-otp-verify"
        class="mt-5 w-full rounded-full bg-[var(--accent)] px-6 py-4 text-base font-black text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20 transition hover:-translate-y-0.5 hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-40"
        disabled={busy || expired}
        onclick={onverify}
      >
        {busy ? "Sprawdzanie…" : "Zatwierdź kod"}
      </button>

      <div class="mt-3 text-center">
        <button
          type="button"
          data-testid="booking-otp-send"
          class="text-sm font-bold text-slate-600 underline underline-offset-2 transition hover:text-slate-900 disabled:cursor-not-allowed disabled:no-underline disabled:opacity-50 dark:text-slate-400 dark:hover:text-slate-200"
          disabled={busy || cooldownSec > 0 || expired}
          onclick={onsend}
        >
          {busy
            ? "Wysyłanie…"
            : cooldownSec > 0
              ? `Wyślij ponownie za ${cooldownSec} s`
              : "Wyślij kod ponownie"}
        </button>
      </div>
    {/if}

    {#if error}
      <p class="mt-4 text-sm font-medium text-red-700" role="alert">
        {error}
      </p>
    {/if}
  </div>
</div>
