import { ChangeDetectionStrategy, Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ServiceCatalogComponent } from '@domains/services/feature/service-catalog.component';
import { GuideLauncherComponent } from '@shared/ui/guide-launcher/guide-launcher.component';

@Component({
  selector: 'app-services-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, ServiceCatalogComponent, GuideLauncherComponent],
  template: `
    <div class="admin-page-shell admin-page-pad-for-bottom-nav">
      <div class="admin-page-container">
        <!-- Launcher sam sprawdza w rejestrze, czy dla tego ekranu i roli jest przewodnik. -->
        <div class="flex justify-end mb-3">
          <app-guide-launcher />
        </div>

        <section>
          <app-service-catalog />
        </section>
      </div>
    </div>
  `,
})
export class ServicesPageComponent {}
