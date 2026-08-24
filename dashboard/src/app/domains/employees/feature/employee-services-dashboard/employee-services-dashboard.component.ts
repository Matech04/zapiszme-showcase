import { Component, computed, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { EmployeesClient } from '@core/api/api-client';
import { rxResource } from '@angular/core/rxjs-interop';
import { EmployeeAssignedServicesComponent } from '../employee-form/employee-assigned-services.component';

@Component({
  selector: 'app-employee-services-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, EmployeeAssignedServicesComponent],
  template: `
    <div class="admin-page-shell">
      <div class="max-w-3xl mx-auto w-full">
        <nav
          class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1"
        >
          <a routerLink="/admin/resources" class="hover:text-primary transition-colors">Zarządzanie</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <a
            [routerLink]="['/admin/resources/employees', id(), 'availability']"
            class="hover:text-primary transition-colors"
          >
            Pracownik
          </a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <span class="text-surface-700 dark:text-surface-300">Usługi</span>
        </nav>

        <div class="mb-8">
          <h1 class="text-2xl sm:text-3xl font-black text-surface-900 tracking-tight mb-2">
            Usługi pracownika
          </h1>
          <p class="text-surface-600 dark:text-surface-400 text-sm sm:text-base max-w-xl leading-relaxed">
            Przypisz usługi z katalogu i ustaw dla nich czas oraz cenę obowiązującą u tego specjalisty.
          </p>
        </div>

        <div
          class="admin-glass-card rounded-4xl p-4 sm:p-5 shadow-sm mb-8"
        >
          <div class="flex items-center gap-4">
            <div
              class="shrink-0 w-14 h-14 rounded-xl border-2 border-primary/30 bg-primary/5 flex items-center justify-center text-lg font-bold text-primary"
              aria-hidden="true"
            >
              {{ employeeInitials() }}
            </div>
            <div class="min-w-0 flex-1">
              <p class="font-bold text-surface-900 text-base sm:text-lg truncate">
                {{ employeeDisplayName() }}
              </p>
              <p class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 truncate">
                {{ employeeSubtitle() }}
              </p>
            </div>
          </div>
        </div>

        <app-employee-assigned-services [employeeId]="id()" [showPanelTitle]="false" />
      </div>
    </div>
  `,
})
export class EmployeeServicesDashboardComponent {
  private employeesService = inject(EmployeesClient);

  id = input.required<string>();

  employeeData = rxResource({
    stream: () => this.employeesService.getEmployee(this.id()),
  });

  employeeDisplayName = computed(() => {
    const e = this.employeeData.value();
    if (!e) return 'Pracownik';
    const parts = [e.firstName, e.lastName].filter(Boolean);
    return parts.length ? parts.join(' ') : 'Pracownik';
  });

  employeeSubtitle = computed(() => {
    const e = this.employeeData.value();
    if (!e?.email) return 'Przypisania z katalogu';
    return e.email;
  });

  employeeInitials = computed(() => {
    const e = this.employeeData.value();
    if (!e) return '?';
    const a = (e.firstName?.trim()?.[0] ?? '').toUpperCase();
    const b = (e.lastName?.trim()?.[0] ?? '').toUpperCase();
    const initials = `${a}${b}`.trim();
    return initials || '?';
  });
}
