import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';

import { FormDrawerShellComponent } from '@shared/ui/drawer/form-drawer-shell.component';
import { EmployeeLeaveForm } from './employee-leave-form.component';

/**
 * Drawer opakowujący formularz urlopu — otwierany z kalendarza miesiąca
 * ({@link EmployeeFullScheduleComponent}) zamiast nawigacji na podstronę.
 *
 * Kontrakt: rodzic steruje `isOpen`, podaje `id` pracownika i `initialDate`
 * (klikniętego dnia); drawer emituje `saved` (po zapisie) i `closed`.
 */
@Component({
  selector: 'app-employee-leave-form-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormDrawerShellComponent, EmployeeLeaveForm],
  template: `
    <app-form-drawer-shell
      [isOpen]="isOpen()"
      title="Nowy urlop / chorobowe"
      label="Dostępność"
      submitLabel="Dodaj urlop"
      [submitting]="formRef.submitting()"
      (submitClicked)="formRef.submit()"
      (closeRequested)="requestClose()"
    >
      <div drawer-body>
        <app-employee-leave-form
          #formRef
          [embedded]="true"
          [id]="id()"
          [initialDate]="initialDate()"
          (saved)="onSaved()"
          (cancelled)="requestClose()"
        />
      </div>
    </app-form-drawer-shell>
  `,
})
export class EmployeeLeaveFormDrawerComponent {
  readonly isOpen = input<boolean>(false);
  readonly id = input.required<string>();
  readonly initialDate = input<string | null>(null);

  readonly closed = output<void>();
  readonly saved = output<void>();

  protected onSaved(): void {
    this.saved.emit();
    this.closed.emit();
  }

  protected requestClose(): void {
    this.closed.emit();
  }
}
