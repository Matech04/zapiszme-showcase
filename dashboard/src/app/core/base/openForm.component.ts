import { Component, inject, Type } from "@angular/core";
import { DialogService, DynamicDialogRef } from "primeng/dynamicdialog";

@Component({ template: '' })
export abstract class OpenFormComponent<T extends { id: string }> {

  protected dialogService = inject(DialogService);
  protected ref: DynamicDialogRef | undefined | null;

  protected abstract handleCreate(item: T): void;
  protected abstract handleUpdate(id: string, item: T): void;

  protected openForm(componentClass: Type<any>, item?: Partial<T>) {
    this.ref = this.dialogService.open(componentClass, {
      header: item?.id ? 'Editing' : 'Creating',
      data: { formData: item }
    });

    if (this.ref != null) {
      this.ref.onClose.subscribe((result: T) => {
        if (result) {
          item?.id ? this.handleUpdate(item.id, result) : this.handleCreate(result);
        }
      });
    }
  }

}