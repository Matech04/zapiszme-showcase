import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { form, FormField, required, FormRoot, submit } from '@angular/forms/signals';
import { CreateServiceCategoryCommand, ServiceCategoriesClient, UpdateServiceCategoryCommand } from '@core/api/api-client';
import { createFormActions } from '@shared/utils/createFormActions';
import { HasUnsavedChanges } from '@core/guards/has-unsaved-changes';
import { FormLayoutComponent } from "@shared/ui/forms/form-layout.component";
import { FormFieldComponent } from "@shared/ui/forms/form-field-component";

interface serviceCategoryData {
  name: string;
}

@Component({
  selector: 'app-signal-form',
  imports: [FormLayoutComponent, FormFieldComponent, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
  <app-form-layout
  [title]="'Kategorie'"
  (submit)="FormActions.handleSubmit()"
  (cancel)="FormActions.handleCancel()"
  [isEdit]="isEdit()"
  [confirmOnCancel]="true"
  [hasUnsavedChanges]="FormActions.hasUnsavedChanges()"
  [testId]="'service-category-form'"
  >
  <app-form-field
    label="Nazwa"
    id="name"
    placeholder="Wpisz nazwę kategori"
    [formField]="serviceCategoryForm.name"
  />
  </app-form-layout>
  `
})
export class ServiceCategoryForm implements HasUnsavedChanges {

  id = input<string>();

  isEdit = computed(() => !!this.id());

  private serviceCategoriesService = inject(ServiceCategoriesClient);

  serviceCategoryModel = signal<serviceCategoryData>({
    name: '',
  });

  serviceCategoryForm = form(
    this.serviceCategoryModel,
    (schemaPath) => {
      required(schemaPath.name, { message: 'Pole nie może być puste' }
      )
    }

  );

  protected FormActions = createFormActions<serviceCategoryData, any>(
    {
      create: (data) => this.serviceCategoriesService.createServiceCategory(data as CreateServiceCategoryCommand),
      update: (id, data) => this.serviceCategoriesService.updateServiceCategory(id, data as UpdateServiceCategoryCommand),
      delete: (id) => this.serviceCategoriesService.deleteServiceCategory(id),
      get: (id) => this.serviceCategoriesService.getServiceCategory(id),
    },
    this.serviceCategoryForm as any,
    this.serviceCategoryModel,
    this.id,
    // Katalog usług to jedyne wejście do tego formularza — po zapisie/anulowaniu
    // wracamy tam, a nie na domyślne /admin/resources.
    { redirectUrl: '/admin/services' },
  );

  hasUnsavedChanges(): boolean {
    return this.FormActions.hasUnsavedChanges();
  }
}
