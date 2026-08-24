import { Component, inject, Type } from "@angular/core";
import { BaseReadComponent } from "./base-read.component";
import { DialogService, DynamicDialogRef } from "primeng/dynamicdialog";
import { Observable } from "rxjs";

// base-write.component.ts
@Component({ template: '' })
export abstract class BaseWriteComponent<T extends { id: string }> extends BaseReadComponent<T> {
  protected dialogService = inject(DialogService);
  protected ref: DynamicDialogRef | undefined | null;

  // Tutaj wymagamy tylko metod zapisu/edycji/usuwania
  protected abstract service: {
    create(item: Partial<T>): Observable<T>;
    update(id: string, item: Partial<T>): Observable<T>;
    delete(id: string): Observable<any>;
  };

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

  protected handleCreate(newItem: T) {
    this.service.create(newItem).subscribe({
      next: () => { this.showToast("Dodano"); this.loadData(); }
    });
  }

  protected handleUpdate(id: string, updatedItem: Partial<T>) {
    this.service.update(id, updatedItem).subscribe({
      next: () => { this.showToast("Zaktualizowano"); this.loadData(); }
    });
  }
}