<script lang="ts">
  import type { Step } from "./types";

  let { steps }: { steps: Step[] } = $props();

  // Tonacja dla JASNEJ powierzchni (pasek kroków siedzi teraz na białej karcie, nie na ciemnym boxie).
  function stepClass(step: Step): string {
    if (step.state === "active")
      return "bg-[var(--accent)] text-[var(--accent-contrast)]";
    if (step.state === "done")
      return step.success
        ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300"
        : "bg-slate-200 text-slate-700 dark:bg-white/15 dark:text-slate-200";
    return "bg-slate-100 text-slate-400 dark:bg-white/5 dark:text-slate-500";
  }
</script>

<div
  class="grid gap-2 text-center text-xs font-black"
  style={`grid-template-columns: repeat(${steps.length}, minmax(0, 1fr))`}
>
  {#each steps as step (step.label)}
    <span class={`rounded-full px-2 py-2 ${stepClass(step)}`}>{step.label}</span>
  {/each}
</div>
