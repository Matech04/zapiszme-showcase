import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { EmployeeListComponent } from '@domains/employees/feature/employee-list/employee-list.component';
import { ServiceCatalogComponent } from '@domains/services/feature/service-catalog.component';
import { EmployeesClient, ServiceCategoriesClient } from '@core/api/api-client';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';

@Component({
  selector: 'app-resources-page',
  standalone: true,
  imports: [
    CommonModule,
    EmployeeListComponent,
    ServiceCatalogComponent,
  ],
  template: `
    <div class="admin-page-shell">
      <div class="admin-page-container">
        
        <header class="admin-glass-card admin-page-hero">
          <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-6">
            <div>
              <span class="admin-section-label text-primary mb-3 block">
                Panel Zarządzania
              </span>
              
              <h1 class="admin-page-title text-surface-900 mb-4">
                Zasoby Salonu
              </h1>
              
              <p class="admin-page-lead text-surface-700 dark:text-surface-300 font-sans">
                W tym miejscu skonfigurujesz fundamenty swojego biznesu. Zarządzaj specjalistami, 
                definiuj ofertę usług i kontroluj dostępność zespołu.
              </p>
            </div>
            <div class="admin-hero-meta">
              <span class="admin-meta-pill text-slate-700 dark:text-slate-100"><i class="pi pi-users text-xs"></i>Zespół</span>
              <span class="admin-meta-pill text-slate-700 dark:text-slate-100"><i class="pi pi-briefcase text-xs"></i>Usługi</span>
              <span class="admin-meta-pill text-slate-700 dark:text-slate-100"><i class="pi pi-calendar text-xs"></i>Dostępność</span>
            </div>
          </div>
        </header>

        <div class="space-y-4 sm:space-y-8">
          <section id="team">
            <app-employee-list/>
          </section>

          <section id="catalog">
            <app-service-catalog/>
          </section>
        </div>
      </div>
    </div>
  `
})
export class ResourcesPageComponent {

}
