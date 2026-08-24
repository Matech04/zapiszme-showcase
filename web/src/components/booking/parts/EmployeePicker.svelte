<script lang="ts">
  import type { BookingEmployee } from "../../../lib/booking/data-source";
  import { initials } from "../../../lib/booking/format";

  let {
    employees,
    selectedEmployeeId,
    loading,
    hideHeading = false,
    onselect,
  }: {
    employees: BookingEmployee[];
    selectedEmployeeId: string;
    loading: boolean;
    /** Ukryj własny nagłówek „Pracownik" (kreator ma już tytuł kroku). */
    hideHeading?: boolean;
    onselect: (id: string) => void;
  } = $props();

  /**
   * Blokujemy kafelki TYLKO gdy jest z czego wybierać. Salon solo (jeden pracownik) ma wchodzić
   * w kalendarz normalnie, nawet pusty — komunikat „brak wolnych terminów" pokazuje wtedy sam
   * kalendarz. Zablokowanie jedynego pracownika zostawiłoby klientkę ze ślepą listą bez wyjaśnienia.
   */
  const blockingEnabled = $derived(employees.length > 1);

  const isBlocked = (employee: BookingEmployee): boolean =>
    blockingEnabled && employee.hasUpcomingSchedule === false;
</script>

<div class="min-w-0">
  {#if !hideHeading}
    <h3 class="text-lg font-black">Pracownik</h3>
  {/if}
  <div class="mt-3 grid gap-2">
    {#if loading}
      <div
        class="grid gap-2 animate-pulse"
        aria-busy="true"
        aria-label="Ładowanie pracowników"
      >
        {#each [0, 1, 2] as i (i)}
          <div
            class="flex items-center gap-3 rounded-2xl border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] p-3"
          >
            <span class="size-11 shrink-0 rounded-full bg-slate-100 dark:bg-white/10"></span>
            <span class="flex-1 space-y-2">
              <span class="block h-3 w-1/2 rounded-full bg-slate-100 dark:bg-white/10"></span>
              <span class="block h-2.5 w-1/3 rounded-full bg-slate-100 dark:bg-white/10"></span>
            </span>
          </div>
        {/each}
      </div>
    {:else if employees.length === 0}
      <p
        class="rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
      >
        Brak dostępnych pracowników.
      </p>
    {:else}
      {#each employees as employee (employee.id)}
        {#if employee.id}
          {@const blocked = isBlocked(employee)}
          <button
            type="button"
            data-testid={`booking-employee-${employee.id}`}
            aria-pressed={selectedEmployeeId === employee.id}
            disabled={blocked}
            class={`flex items-center gap-3 rounded-2xl border p-3 text-left transition ${
              blocked
                ? "cursor-not-allowed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 opacity-60"
                : selectedEmployeeId === employee.id
                  ? "border-brand-300 dark:border-brand-500/40 bg-brand-50 dark:bg-brand-500/10 hover:-translate-y-0.5"
                  : "border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] hover:border-slate-300 hover:-translate-y-0.5"
            }`}
            onclick={() => {
              // `disabled` nie wystarcza: Svelte 5 deleguje onclick do korzenia, więc zdarzenie
              // wysłane programowo na zablokowany przycisk i tak dobiegłoby do handlera.
              if (blocked) return;
              onselect(employee.id!);
            }}
          >
            <span
              class={`grid size-11 shrink-0 place-items-center rounded-full font-black text-white ${
                blocked ? "bg-slate-400 dark:bg-white/20" : "bg-slate-950"
              }`}
              >{initials(employee.firstName, employee.lastName)}</span
            >
            <span class="min-w-0">
              <span class="block break-words font-black"
                >{employee.firstName ?? ""} {employee.lastName ?? ""}</span
              >
              {#if blocked}
                <span
                  data-testid={`booking-employee-unavailable-${employee.id}`}
                  class="block text-sm font-medium text-slate-500 dark:text-slate-400"
                  >Brak wolnych terminów</span
                >
              {/if}
            </span>
          </button>
        {/if}
      {/each}
    {/if}
  </div>
</div>
