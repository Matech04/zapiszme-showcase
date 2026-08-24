import {
  ChangeDetectionStrategy,
  Component,
  computed,
  HostListener,
  ViewChild,
  inject,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConfirmationService } from 'primeng/api';

import { FormDrawerShellComponent } from '@shared/ui/drawer/form-drawer-shell.component';
import { ServiceForm } from './service-form.component';

/**
 * Drawer opakowujący formularz usługi — eliminuje pełnostronicowy route
 * dla nowej / edytowanej usługi. Otwierany z {@link ServiceCatalogComponent}.
 *
 * Chrome (drawer + responsywność + stopka) zapewnia {@link FormDrawerShellComponent}.
 *
 * Kontrakt:
 *  - rodzic steruje `isOpen` (i tym kiedy edytowane id / hint kategorii się zmieniają)
 *  - drawer emituje `closed` (po zamknięciu z dowolnego powodu) i `saved`
 *  - przed zamknięciem z brudnym formularzem pyta przez {@link ConfirmationService}
 */
@Component({
  selector: 'app-service-form-drawer',
  standalone: true,
  imports: [CommonModule, FormDrawerShellComponent, ServiceForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-form-drawer-shell
      [isOpen]="isOpen()"
      [title]="title()"
      label="Katalog usług"
      [submitLabel]="isEdit() ? 'Zapisz zmiany' : 'Dodaj usługę'"
      (submitClicked)="formRef.onSubmit()"
      (closeRequested)="requestClose()"
    >
      <div drawer-body>
        <app-service-form
          #formRef
          [id]="serviceId()"
          [categoryHint]="categoryHint()"
          [active]="isOpen()"
          (saved)="onSaved($event)"
          (cancelled)="requestClose()"
        />
      </div>
    </app-form-drawer-shell>
  `,
})
export class ServiceFormDrawerComponent {
  readonly isOpen = input<boolean>(false);
  readonly serviceId = input<string | undefined>(undefined);
  readonly categoryHint = input<string | null | undefined>(undefined);

  readonly closed = output<void>();
  readonly saved = output<{ id: string | undefined; isUpdate: boolean }>();

  @ViewChild('formRef') private formRef?: ServiceForm;

  protected readonly isEdit = computed(() => !!this.serviceId());
  protected readonly title = computed(() => (this.isEdit() ? 'Edytuj usługę' : 'Nowa usługa'));

  private confirm = inject(ConfirmationService);

  protected onSaved(payload: { id: string | undefined; isUpdate: boolean }) {
    this.saved.emit(payload);
    this.closed.emit();
  }

  protected requestClose() {
    const dirty = this.formRef?.hasUnsavedChanges() ?? false;
    if (!dirty) {
      this.closed.emit();
      return;
    }
    this.confirm.confirm({
      header: 'Niezapisane zmiany',
      message: 'Masz niezapisane zmiany w formularzu. Czy na pewno chcesz zamknąć?',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Zamknij',
      rejectLabel: 'Pozostań',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.closed.emit(),
    });
  }

  @HostListener('window:beforeunload', ['$event'])
  protected onBeforeUnload(ev: BeforeUnloadEvent) {
    if (!this.isOpen()) return;
    if (!(this.formRef?.hasUnsavedChanges() ?? false)) return;
    ev.preventDefault();
    ev.returnValue = '';
  }
}
