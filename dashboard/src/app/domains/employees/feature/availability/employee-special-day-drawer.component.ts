import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';

import { FormDrawerShellComponent } from '@shared/ui/drawer/form-drawer-shell.component';
import { EmployeeSpecialDaysComponent } from './employee-special-days.component';

/**
 * Drawer opakowujący edytor godzin pojedynczego dnia (tryb osadzony
 * {@link EmployeeSpecialDaysComponent}) — otwierany z kalendarza miesiąca
 * zamiast nawigacji na podstronę. Pokazuje tylko edytor (bez listy/nagłówka).
 *
 * Kontrakt: rodzic steruje `isOpen`, podaje `id` i `initialDate` (klikniętego
 * dnia); drawer emituje `saved` (po zapisie) i `closed`.
 */
@Component({
  selector: 'app-employee-special-day-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormDrawerShellComponent, EmployeeSpecialDaysComponent],
  template: `
    <app-form-drawer-shell
      [isOpen]="isOpen()"
      title="Godziny na ten dzień"
      label="Dostępność"
      submitLabel="Zapisz godziny dnia"
      [submitting]="editorRef.saving()"
      [submitDisabled]="editorRef.overridesData.isLoading()"
      (submitClicked)="editorRef.saveDraft()"
      (closeRequested)="requestClose()"
    >
      <div drawer-body>
        <app-employee-special-days
          #editorRef
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
export class EmployeeSpecialDayDrawerComponent {
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
