import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

export interface EmployeeStripOption {
  id: string;
  label: string;
}

/** Inicjały z „Jan Nowak" → „JN". Pojedyncze słowo → pierwsza litera. */
export function initialsOf(label: string): string {
  const parts = label.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length === 1) return parts[0]!.charAt(0).toUpperCase();
  return (parts[0]!.charAt(0) + parts[parts.length - 1]!.charAt(0)).toUpperCase();
}

/**
 * Pasek wyboru pracownika na SZCZYCIE kalendarza — nad przełącznikiem widoków, wspólny dla
 * widoku dziennego, tygodniowego i miesięcznego. JEDYNY selektor pracownika w kalendarzu:
 * zastąpił kartę „Pracownik" (mobile), `p-select` w nagłówku miesiąca (desktop) oraz multiselect
 * w pasku filtrów, który na telefonie chował się w szufladzie „więcej filtrów".
 *
 * Wybór jest POJEDYNCZY i odpowiada wyłącznie na pytanie „czyj kalendarz oglądamy" — to NIE jest
 * filtr wizyt. Stan trzyma rodzic; komponent jest bezstanowy.
 */
@Component({
  selector: 'app-employee-strip',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="admin-glass-card rounded-3xl p-2 mb-3 flex items-center gap-2 overflow-x-auto"
      role="group"
      aria-label="Wybór pracownika"
      data-testid="employee-strip"
    >
      @for (e of employees(); track e.id) {
        <button
          type="button"
          (click)="select.emit(e.id)"
          [attr.aria-pressed]="isSelected(e.id)"
          [attr.aria-label]="e.label"
          [title]="e.label"
          class="shrink-0 inline-flex items-center gap-2 rounded-full border py-1 pl-1 pr-3 transition-colors"
          [class]="chipClasses(isSelected(e.id))"
        >
          <span
            class="grid h-7 w-7 place-items-center rounded-full text-[10px] font-bold"
            [class]="avatarClasses(isSelected(e.id))"
            aria-hidden="true"
          >
            {{ initials(e.label) }}
          </span>
          <span class="text-xs font-semibold whitespace-nowrap">{{ e.label }}</span>
        </button>
      }
    </div>
  `,
})
export class EmployeeStripComponent {
  readonly employees = input.required<EmployeeStripOption[]>();
  /** Aktualnie wyświetlany pracownik. */
  readonly selectedId = input<string | null>(null);

  /** Wybiera pracownika, którego kalendarz oglądamy. */
  readonly select = output<string>();

  protected isSelected(id: string): boolean {
    return this.selectedId() === id;
  }

  protected initials(label: string): string {
    return initialsOf(label);
  }

  protected chipClasses(active: boolean): string {
    return active
      ? 'border-primary bg-primary/10 text-primary'
      : 'border-surface-300 dark:border-surface-600 text-surface-700 dark:text-surface-300 hover:border-primary/60';
  }

  protected avatarClasses(active: boolean): string {
    return active
      ? 'bg-primary text-primary-contrast'
      : 'bg-surface-200 dark:bg-surface-700 text-surface-700 dark:text-surface-300';
  }
}
