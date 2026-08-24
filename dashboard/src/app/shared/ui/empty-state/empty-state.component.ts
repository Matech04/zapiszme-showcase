import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="rounded-3xl border border-dashed border-surface-200 dark:border-surface-200 bg-white/45 dark:bg-surface-50/35 p-8 sm:p-10 text-center"
    >
      <span
        class="grid size-12 place-items-center rounded-2xl bg-amber-100/80 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300 mx-auto mb-4"
      >
        <i [class]="icon()" class="text-xl" aria-hidden="true"></i>
      </span>
      <h3 class="admin-h3 text-surface-900 mb-1.5">{{ title() }}</h3>
      @if (description()) {
        <p class="text-sm text-surface-600 dark:text-surface-400 max-w-md mx-auto">
          {{ description() }}
        </p>
      }
      @if (ctaLabel()) {
        <button
          type="button"
          (click)="ctaClick.emit()"
          class="mt-5 inline-flex items-center gap-2 rounded-full bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 px-5 py-2.5 font-bold text-sm"
        >
          @if (ctaIcon()) {
            <i [class]="ctaIcon()"></i>
          }
          {{ ctaLabel() }}
        </button>
      }
    </div>
  `,
})
export class EmptyStateComponent {
  readonly icon = input<string>('pi pi-inbox');
  readonly title = input.required<string>();
  readonly description = input<string>('');
  readonly ctaLabel = input<string>('');
  readonly ctaIcon = input<string>('');
  readonly ctaClick = output<void>();
}
