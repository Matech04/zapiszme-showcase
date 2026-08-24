import { Component, input, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { EmployeeDto, EmployeesClient } from '@core/api/api-client';
import { ButtonModule } from 'primeng/button';
import { rxResource } from '@angular/core/rxjs-interop';
import { EmployeeCardComponent } from "../../ui/employee-card/employee-card.component";
import { Router } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';

@Component({
  selector: 'app-employee-list',
  standalone: true,
  imports: [CommonModule, ButtonModule, EmployeeCardComponent, EmptyStateComponent],
  template: `
    <div class="admin-glass-card rounded-4xl p-4 sm:p-8">
      <div class="flex flex-col sm:flex-row items-start sm:items-end justify-between mb-8 sm:mb-12 gap-6">
        <div>
          <h2 class="text-4xl font-black tracking-tight text-surface-900 leading-none mb-2">Zespół</h2>
          <p class="text-surface-600 dark:text-surface-400 font-sans tracking-wide text-sm sm:text-base">Zarządzaj specjalistami i ich dostępnością</p>
        </div>

        <div class="flex flex-col sm:flex-row items-stretch sm:items-center gap-4 w-full sm:w-auto">
          <div class="flex items-center gap-3 bg-white/85 dark:bg-surface-50/70 px-6 py-3 rounded-full border border-surface-200/80 dark:border-surface-200/70">
            <span class="text-[10px] font-bold text-surface-500 uppercase tracking-[0.2em]">Wszyscy</span>
            <span class="w-px h-4 bg-surface-300 dark:bg-surface-200"></span>
            <span data-testid="employees-count" class="text-xl font-bold text-primary">{{ employeesCount() }}</span>
          </div>
          
          @if (canManage()) {
            <p-button
              data-testid="add-specialist-btn"
              data-tour="team-add"
              (onClick)="handleAddEmployee()"
              label="DODAJ SPECJALISTĘ"
              class="px-8 py-3 font-bold"
            />
          }
        </div>
      </div>

      <div data-testid="employees-grid" data-tour="team-grid" class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6 sm:gap-8">
        @for (employee of employees.value(); track employee.id) {
          <app-employee-card
            data-testid="employee-card-item"
            [employee]="employee"
            [canManage]="canManage()"
            (edit)="handleEditEmployee($event)"
            (delete)="handleDeleteEmployee($event)"
            (manage)="handleManage($event)"
            (openLeaveDashboard)="handleOpenLeaveDashboard($event)"
          />
        } @empty {
          <app-empty-state
            data-testid="empty-state"
            class="col-span-full"
            icon="pi pi-users"
            title="Brak zespołu"
            description="Nie masz jeszcze zdefiniowanych pracowników."
            [ctaLabel]="canManage() ? 'Dodaj pracownika' : ''"
            ctaIcon="pi pi-plus"
            (ctaClick)="handleAddEmployee()"
          />
        }
      </div>
    </div>
  `
})
export class EmployeeListComponent {

  private employeesService = inject(EmployeesClient);
  private router = inject(Router);
  private confirmationService = inject(ConfirmationService);
  private readonly auth = inject(AuthSessionService);

  /** Zarządzanie zespołem (dodaj/edytuj/usuń/grafiki) = StaffManagement → owner/manager. Pracownik: roster read-only. */
  readonly canManage = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'manager' || role === 'systemAdmin';
  });

  employees = rxResource({
    stream: () => this.employeesService.getEmployees(),
    defaultValue: []
  });

  employeesCount = computed(() => this.employees.value().length);

  /** Strona, z której otwarto formularz (Zespół lub Zasoby) — formularz tu wróci po zapisie/anulowaniu. */
  private returnUrl(): string {
    return this.router.url.split('?')[0];
  }

  handleAddEmployee() {
    this.router.navigate([`admin/resources/employees/new`], {
      queryParams: { returnUrl: this.returnUrl() },
    });
  }

  handleEditEmployee(id: string) {
    this.router.navigate([`admin/resources/employees/edit/${id}`], {
      queryParams: { returnUrl: this.returnUrl() },
    });
  }

  handleDeleteEmployee(id: string) {
    this.confirmationService.confirm({
      message: 'Czy na pewno chcesz usunąć pracownika?',
      header: 'Potwierdzenie usunięcia pracownika',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Tak',
      rejectLabel: 'Nie',
      accept: () => {
        if (id) {
          this.employeesService.deleteEmployee(id).subscribe({
            next: () => {
              this.employees.reload();
            },
            error: (err) => console.error('Błąd podczas usuwania usługi:', err)
          });
        }
      }
    });
  }

  handleManage(id: string | undefined) {

    if (id == undefined) {
      console.log("employee id undefined")
      return
    }

    console.log("Navigate to manage")
    console.log(id);
    this.router.navigate([`admin/resources/employees/${id}/availability`]);
  }

  handleOpenLeaveDashboard(id: string | undefined) {
    if (id == undefined) {
      return;
    }
    this.router.navigate([`admin/resources/employees/${id}/leave-dashboard`]);
  }
}