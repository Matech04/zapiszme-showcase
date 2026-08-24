import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { form, required, FormField } from '@angular/forms/signals';
import { ConfirmationService } from 'primeng/api';
import {
  CreateServiceCategoryCommand,
  ServiceCategoriesClient,
  UpdateServiceCategoryCommand,
} from '@core/api/api-client';
import { createFormActions } from '@shared/utils/createFormActions';
import { FormDrawerShellComponent } from '@shared/ui/drawer/form-drawer-shell.component';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';

interface CategoryData {
  name: string;
}

/**
 * Drawer dodania / edycji kategorii — otwierany z {@link ServiceCatalogComponent} zamiast
 * pełnostronicowego route'a `resources/categories/new|edit`. Chrome + stopka („Anuluj / Zapisz")
 * z {@link FormDrawerShellComponent}; zapis/edycja przez {@link createFormActions} w trybie
 * drawer (`onSuccess` zamiast nawigacji).
 *
 * Kontrakt (jak w `service-form-drawer`):
 *  - rodzic steruje `isOpen` i `categoryId` (undefined = tworzenie),
 *  - drawer emituje `saved` (po udanym zapisie) i `closed` (po zamknięciu z dowolnego powodu),
 *  - przy brudnym formularzu pyta o potwierdzenie przed zamknięciem.
 */
@Component({
  selector: 'app-category-form-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormDrawerShellComponent, FormFieldComponent, FormField],
  template: `
    <app-form-drawer-shell
      [isOpen]="isOpen()"
      [title]="isEdit() ? 'Edytuj kategorię' : 'Nowa kategoria'"
      label="Katalog usług"
      [submitLabel]="isEdit() ? 'Zapisz zmiany' : 'Dodaj kategorię'"
      [submitting]="FormActions.isPending()"
      [submitDisabled]="categoryForm().invalid() || FormActions.isPending()"
      (submitClicked)="FormActions.handleSubmit()"
      (closeRequested)="requestClose()"
    >
      <div drawer-body>
        <app-form-field
          label="Nazwa"
          id="category-name"
          testId="category-form-name"
          placeholder="Wpisz nazwę kategorii"
          [formField]="categoryForm.name"
        />
      </div>
    </app-form-drawer-shell>
  `,
})
export class CategoryFormDrawerComponent {
  readonly isOpen = input<boolean>(false);
  readonly categoryId = input<string | undefined>(undefined);

  readonly closed = output<void>();
  readonly saved = output<void>();

  protected readonly isEdit = computed(() => !!this.categoryId());

  private readonly categoriesClient = inject(ServiceCategoriesClient);
  private readonly confirm = inject(ConfirmationService);

  protected readonly model = signal<CategoryData>({ name: '' });

  protected readonly categoryForm = form(this.model, (path) => {
    required(path.name, { message: 'Pole nie może być puste' });
  });

  protected readonly FormActions = createFormActions<CategoryData>(
    {
      create: (data) =>
        this.categoriesClient.createServiceCategory(data as CreateServiceCategoryCommand),
      update: (id, data) =>
        this.categoriesClient.updateServiceCategory(id, data as UpdateServiceCategoryCommand),
      delete: (id) => this.categoriesClient.deleteServiceCategory(id),
      get: (id) => this.categoriesClient.getServiceCategory(id),
    },
    this.categoryForm as any,
    this.model,
    this.categoryId,
    {
      successMessage: (_d, isUpdate) => (isUpdate ? 'Zaktualizowano kategorię' : 'Dodano kategorię'),
      // Tryb drawer: po zapisie zamiast nawigacji zgłoś zapis i zamknij.
      onSuccess: () => {
        this.saved.emit();
        this.closed.emit();
      },
    },
  );

  constructor() {
    // Reset pól przy otwarciu w trybie dodawania (edycję ładuje createFormActions przez get(id)).
    effect(() => {
      if (this.isOpen() && !this.categoryId()) {
        this.model.set({ name: '' });
      }
    });
  }

  protected requestClose(): void {
    if (!this.FormActions.hasUnsavedChanges()) {
      this.closed.emit();
      return;
    }
    this.confirm.confirm({
      header: 'Niezapisane zmiany',
      message: 'Masz niezapisane zmiany. Czy na pewno chcesz zamknąć?',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Zamknij',
      rejectLabel: 'Pozostań',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.closed.emit(),
    });
  }
}
