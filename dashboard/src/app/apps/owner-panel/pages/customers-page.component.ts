import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CustomerListComponent } from '@domains/customers/feature/customer-list/customer-list.component';
import { GuideLauncherComponent } from '@shared/ui/guide-launcher/guide-launcher.component';

@Component({
  selector: 'app-customers-page',
  standalone: true,
  imports: [CommonModule, CustomerListComponent, GuideLauncherComponent],
  template: `
    <div class="admin-page-shell">
      <div class="admin-page-container">
        <!-- Launcher jest samosterowny: dopóki w rejestrze nie ma przewodnika dla tej trasy,
             nie renderuje niczego. Zostawiamy go, żeby przewodnik dla klientek zapalił się
             sam, gdy powstanie. -->
        <div class="flex justify-end mb-3">
          <app-guide-launcher />
        </div>

        <div>
          <app-customer-list />
        </div>
      </div>
    </div>
  `,
})
export class CustomersPageComponent {}
