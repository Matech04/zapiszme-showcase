import { Component, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { EmployeesClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { EmployeeAssignedServicesComponent } from '../employee-form/employee-assigned-services.component';

@Component({
  selector: 'app-employee-my-services-page',
  standalone: true,
  imports: [CommonModule, RouterLink, EmployeeAssignedServicesComponent],
  template: `
    <div class="admin-page-shell admin-page-pad-for-bottom-nav">
      <div class="max-w-3xl mx-auto w-full">
        <header class="admin-glass-card admin-page-hero mb-6">
          <div class="flex items-center justify-between gap-3">
            <div>
              <p class="admin-section-label text-primary mb-2">Pracownik</p>
              <h1 class="admin-page-title text-surface-900 mb-3">Moje usługi</h1>
              <p class="admin-page-lead text-surface-700 dark:text-surface-300">
                Wybierz usługi, które wykonujesz. Ustaw własny czas i cenę dla pozycji z katalogu.
              </p>
            </div>
            <a
              routerLink="/admin/schedule"
              class="hidden sm:inline-flex rounded-full border border-surface-300 dark:border-surface-200 px-4 py-2 text-xs font-bold uppercase tracking-wider hover:border-primary/40 transition-colors"
            >
              Plan dnia
            </a>
          </div>
        </header>

        @if (!employeeId()) {
          <section class="admin-glass-card rounded-4xl p-6 text-sm text-surface-600 dark:text-surface-300">
            Twoje konto nie jest jeszcze powiązane z profilem pracownika. Skontaktuj się z managerem lub
            właścicielem salonu.
          </section>
        } @else {
          <section class="admin-glass-card rounded-4xl p-5 mb-5">
            <p class="text-xs font-bold uppercase tracking-wider text-surface-500 mb-1">Profil</p>
            <p class="text-lg font-black tracking-tight text-surface-900">
              {{ employeeDisplayName() }}
            </p>
            <p class="text-sm text-surface-500 dark:text-surface-400">{{ employeeSubtitle() }}</p>
          </section>

          <app-employee-assigned-services [employeeId]="employeeId()!" [showPanelTitle]="false" />
        }
      </div>
    </div>
  `,
})
export class EmployeeMyServicesPageComponent {
  private readonly auth = inject(AuthSessionService);
  private readonly employeesClient = inject(EmployeesClient);

  protected readonly employeeId = computed(() => this.auth.currentEmployeeId());

  private readonly employeeData = rxResource({
    params: () => this.employeeId(),
    stream: ({ params }) => {
      if (!params) {
        return of(undefined);
      }
      return this.employeesClient.getEmployee(params);
    },
  });

  protected readonly employeeDisplayName = computed(() => {
    const e = this.employeeData.value();
    if (!e) return 'Pracownik';
    const parts = [e.firstName, e.lastName].filter(Boolean);
    return parts.length ? parts.join(' ') : 'Pracownik';
  });

  protected readonly employeeSubtitle = computed(() => {
    const e = this.employeeData.value();
    return e?.email || 'Dostosuj swoje przypisania usług';
  });
}
