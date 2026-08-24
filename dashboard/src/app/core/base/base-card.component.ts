import { Component, inject, input, output } from "@angular/core";
import { ConfirmationService } from "primeng/api";

@Component({ template: '' })
export abstract class BaseCardComponent<T> {

  item = input.required<T>();

  edit = output<T>();
  delete = output<T>();

  protected confirmationService = inject(ConfirmationService);

  onEditClick() {
    this.edit.emit(this.item());
  };

  confirmDelete(event: Event) {
    this.confirmationService.confirm({
      target: event.target as EventTarget,
      message: `Czy na pewno chcesz to usunąć?`,
      header: 'Potwierdzenie',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Tak, usuń',
      rejectLabel: 'Anuluj',
      acceptButtonStyleClass: 'p-button-danger p-button-text',
      rejectButtonStyleClass: 'p-button-text p-button-secondary',

      accept: () => {
        // Tylko emitujemy event. To rodzic (Lista) usunie dane i pokaże Toast.
        this.delete.emit(this.item());
      },
      reject: () => {
        // Opcjonalnie: Tutaj można dodać toast "Anulowano"
      }
    });
  };
}