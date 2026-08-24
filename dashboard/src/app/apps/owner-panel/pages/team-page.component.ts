import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmployeeListComponent } from '@domains/employees/feature/employee-list/employee-list.component';

@Component({
  selector: 'app-team-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmployeeListComponent],
  template: `
    <div class="admin-page-shell admin-page-pad-for-bottom-nav">
      <div class="admin-page-container">
        <section>
          <app-employee-list />
        </section>
      </div>
    </div>
  `,
})
export class TeamPageComponent {}
