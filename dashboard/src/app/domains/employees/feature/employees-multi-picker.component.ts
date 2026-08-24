import { ChangeDetectionStrategy, Component, inject, model } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormValueControl } from '@angular/forms/signals';
import { EmployeesClient } from '@core/api/api-client';

/**
 * Multi-select pracowników — używany w formularzu Service do natychmiastowego
 * przypisania kogo świadczy usługę (bez konieczności wchodzenia w profil pracownika).
 *
 * Implementuje FormValueControl<string[]> — wartość to lista ID-ków zaznaczonych
 * pracowników, kolejność nieistotna.
 */
@Component({
  selector: 'app-employees-multi-picker',
  standalone: true,
  imports: [CommonModule],
  providers: [EmployeesClient],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-2 w-full" data-testid="employees-multi-picker">
      <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
        Pracownicy świadczący usługę
      </label>

      @if (employees.isLoading()) {
        <div class="grid grid-cols-2 md:grid-cols-3 gap-2">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-14 bg-surface-100 dark:bg-surface-100 animate-pulse rounded-xl"></div>
          }
        </div>
      } @else if (employees.value().length === 0) {
        <p class="text-sm text-surface-500 dark:text-surface-400 px-1">
          Brak pracowników. Najpierw dodaj pracownika w sekcji "Pracownicy".
        </p>
      } @else {
        <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-2">
          @for (employee of employees.value(); track employee.id) {
            <button
              type="button"
              data-testid="employees-multi-picker-item"
              [attr.data-employee-id]="employee.id"
              [attr.aria-pressed]="isSelected(employee.id!)"
              (click)="toggle(employee.id!)"
              [class.ring-2]="isSelected(employee.id!)"
              [class.ring-primary]="isSelected(employee.id!)"
              [class.bg-primary-50]="isSelected(employee.id!)"
              [class.dark:bg-primary-900/20]="isSelected(employee.id!)"
              [class.border-primary]="isSelected(employee.id!)"
              class="flex items-center gap-3 p-3 rounded-xl border border-surface-200 dark:border-surface-200 hover:border-primary transition-all duration-200 bg-surface-0 dark:bg-surface-50 text-left"
            >
              <span
                class="flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2"
                [class.border-primary]="isSelected(employee.id!)"
                [class.bg-primary]="isSelected(employee.id!)"
                [class.border-surface-300]="!isSelected(employee.id!)"
              >
                @if (isSelected(employee.id!)) {
                  <svg viewBox="0 0 20 20" fill="white" class="h-3.5 w-3.5">
                    <path fill-rule="evenodd" d="M16.7 5.3a1 1 0 010 1.4l-8 8a1 1 0 01-1.4 0l-4-4a1 1 0 011.4-1.4L8 12.6l7.3-7.3a1 1 0 011.4 0z" clip-rule="evenodd" />
                  </svg>
                }
              </span>
              <span class="flex flex-col">
                <span class="text-sm font-semibold text-surface-900">
                  {{ employee.firstName }} {{ employee.lastName }}
                </span>
                @if (employee.specialization) {
                  <span class="text-xs text-surface-500 dark:text-surface-400">
                    {{ employee.specialization }}
                  </span>
                }
              </span>
            </button>
          }
        </div>

        <div class="flex items-center justify-between px-1 mt-1">
          <small class="text-surface-500 dark:text-surface-400">
            Zaznaczono: {{ value().length }} / {{ employees.value().length }}
          </small>
          <div class="flex gap-3">
            <button
              type="button"
              class="text-xs text-primary hover:underline"
              (click)="selectAll()"
              data-testid="employees-multi-picker-all"
            >
              Zaznacz wszystkich
            </button>
            <button
              type="button"
              class="text-xs text-surface-500 hover:underline"
              (click)="clear()"
              data-testid="employees-multi-picker-clear"
            >
              Wyczyść
            </button>
          </div>
        </div>
      }

      @if (employees.error()) {
        <small class="text-red-500 px-1">Nie udało się pobrać listy pracowników.</small>
      }
    </div>
  `,
})
export class EmployeesMultiPickerComponent implements FormValueControl<string[]> {
  private employeesClient = inject(EmployeesClient);

  readonly value = model<string[]>([]);

  employees = rxResource({
    stream: () => this.employeesClient.getEmployees(),
    defaultValue: [],
  });

  protected isSelected(id: string): boolean {
    return this.value().includes(id);
  }

  protected toggle(id: string): void {
    const current = this.value();
    this.value.set(current.includes(id) ? current.filter((x) => x !== id) : [...current, id]);
  }

  protected selectAll(): void {
    const ids = this.employees.value()
      .map((e) => e.id)
      .filter((id): id is string => !!id);
    this.value.set(ids);
  }

  protected clear(): void {
    this.value.set([]);
  }
}
