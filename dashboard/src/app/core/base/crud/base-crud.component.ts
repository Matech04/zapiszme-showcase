import { Component, inject, OnInit, signal, Type } from "@angular/core";
import { BaseApiService } from "@core/api/base-api.service";
import { MessageService } from "primeng/api";
import { DialogService, DynamicDialogRef } from "primeng/dynamicdialog";

@Component({ template: '' })
export abstract class BaseCrudComponent<T extends { id: string }> implements OnInit {

  items: T[] = [];
  isLoading = signal(false);

  protected dialogService = inject(DialogService);
  protected messageService = inject(MessageService);
  protected ref: DynamicDialogRef | undefined | null;

  protected abstract service: BaseApiService<T>

  ngOnInit(): void {
    this.loadData();
  }

  loadData() {
    this.isLoading.set(true);
    this.service.getAll().subscribe({
      next: (data) => {
        this.items = data;
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error(err);
        this.showToast("Error while fetching data", 'error')
        this.isLoading.set(false);
      }
    }
    )
  }

  /**
   * Generyczna metoda otwierania edycji/dodawania
   * @param componentClass - Komponent formularza (np. ServiceFormComponent)
   * @param item - Obiekt do edycji (opcjonalny, brak = dodawanie)
   */

  protected openForm(componentClass: Type<any>, item?: T) {
    this.ref = this.dialogService.open(componentClass, {
      header: item ? 'Editing' : 'Creating', // Dynamiczny nagłówek
      width: '400px',
      contentStyle: { overflow: 'auto' },
      breakpoints: { '640px': '95vw' },
      data: {
        // Przekazujemy dane pod uniwersalnym kluczem 'formData'
        formData: item
      }
    });

    if (this.ref != null) {
      this.ref.onClose.subscribe((result: T) => {
        if (result) {
          if (item) {
            // Edycja: Nadpisujemy istniejący element
            // Uwaga: 'result' musi zawierać ID, albo musimy je skleić
            this.handleUpdate(item.id, result);
          } else {
            // Dodawanie
            this.handleCreate(result);
          }
        }
      });
    };

  }

  protected handleCreate(newItem: T) {
    this.service.create(newItem).subscribe({
      next: () => {
        this.showToast("Added successfully")
        this.loadData();
      },
      error: () => {
        this.showToast("Error while saving")
      }
    })
  }

  protected handleUpdate(id: string, updatedItem: Partial<T>) {
    this.service.update(id, updatedItem).subscribe({
      next: () => {
        this.showToast('Zaktualizowano pomyślnie');
        this.loadData(); // Odświeżamy listę
      },
      error: () => this.showToast('Błąd edycji', 'error')
    });
  }

  protected handleDelete(item: T) {
    this.service.delete(item.id).subscribe({
      next: () => {
        this.showToast('Usunięto element', 'info');
        this.loadData(); // Odświeżamy listę
      },
      error: () => this.showToast('Nie udało się usunąć', 'error')
    });
  }

  private showToast(msg: string, severity: 'success' | 'info' | 'error' = 'success') {
    this.messageService.add({ severity, summary: 'Info', detail: msg });
  }
}