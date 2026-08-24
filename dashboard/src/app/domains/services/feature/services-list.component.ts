import { Component, effect, inject, input, output, signal } from '@angular/core';
import { ServicesClient, ServiceCategoryDto, ServiceDto } from '@core/api/api-client';
import { rxResource } from '@angular/core/rxjs-interop';
import { lastValueFrom } from 'rxjs';
import {
  CdkDrag,
  CdkDragDrop,
  CdkDragHandle,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { ServiceCardComponent } from '../ui/service-card.component';
import { ConfirmationService, MessageService } from 'primeng/api';

@Component({
  selector: 'app-services-list',
  standalone: true,
  imports: [ServiceCardComponent, CdkDropList, CdkDrag, CdkDragHandle],
  template: `
    <div
      class="divide-y divide-surface-100 dark:divide-surface-200/50"
      cdkDropList
      (cdkDropListDropped)="onServiceReorder($event)"
    >
      @for (service of orderedServices(); track service.id) {
        <div cdkDrag [cdkDragDisabled]="!reorderMode()" class="flex items-stretch gap-1" data-testid="category-service-drag-item">
          @if (reorderMode()) {
            <button
              type="button"
              cdkDragHandle
              data-testid="category-service-drag-handle"
              aria-label="Przeciągnij, aby zmienić kolejność"
              class="self-center shrink-0 grid place-items-center w-8 h-9 rounded-lg text-surface-400 hover:text-surface-600 cursor-grab active:cursor-grabbing touch-none"
            >
              <i class="pi pi-bars text-sm"></i>
            </button>
          }
          <div class="flex-1 min-w-0">
            <app-service-card
              [service]="service"
              [reorderMode]="reorderMode()"
              (editService)="editService.emit($event)"
              (deleteService)="handleDeleteService($event)"
            />
          </div>
        </div>
      }

      @if (servicesList.value().length === 0 && !servicesList.isLoading()) {
        <button
          type="button"
          data-testid="category-empty-add-service"
          (click)="addService.emit()"
          class="w-full flex items-center gap-2 py-3 px-2 text-left text-sm text-surface-500 dark:text-surface-400 hover:text-primary transition-colors"
        >
          <i class="pi pi-plus text-xs"></i>
          Dodaj pierwszą usługę do tej kategorii
        </button>
      }
    </div>
  `,
})
export class ServicesList {
  category = input.required<ServiceCategoryDto>();
  /**
   * Licznik odświeżenia z katalogu. Po każdej zmianie (poza wartością początkową)
   * przeładowujemy `servicesList`, by lista usług w kategorii pokazała aktualne dane
   * (np. zmienioną nazwę usługi) bez ręcznego odświeżenia strony.
   */
  refreshTick = input<number>(0);
  /** Tryb zmiany kolejności — pokazuje uchwyty i włącza przeciąganie kart usług. */
  reorderMode = input<boolean>(false);
  /** Emit ID usługi do edycji — rodzic (catalog) otwiera drawer. */
  editService = output<string | undefined>();
  /** Emit żądania dodania usługi do tej kategorii (pusta lista). */
  addService = output<void>();

  servicesService = inject(ServicesClient);
  private confirmationService = inject(ConfirmationService);
  private message = inject(MessageService);

  servicesList = rxResource({
    stream: () => this.servicesService.getServices(this.category().id),
    defaultValue: [],
  });

  /** Lokalna kopia do optimistic reorder; synchronizowana z resource. */
  protected orderedServices = signal<ServiceDto[]>([]);

  /** Pomijamy pierwszy odczyt ticka, by nie dublować inicjalnego ładowania resource. */
  private tickInitialized = false;

  constructor() {
    effect(() => this.orderedServices.set([...this.servicesList.value()]));

    effect(() => {
      // Subskrybuj tick. Pierwsze uruchomienie tylko inicjalizuje — resource sam się ładuje.
      const tick = this.refreshTick();
      if (!this.tickInitialized) {
        this.tickInitialized = true;
        return;
      }
      void tick;
      this.servicesList.reload();
    });
  }

  reload() {
    this.servicesList.reload();
  }

  onServiceReorder(event: CdkDragDrop<ServiceDto[]>) {
    const { previousIndex, currentIndex } = event;
    if (previousIndex === currentIndex) return;
    const prev = this.orderedServices();
    const next = [...prev];
    moveItemInArray(next, previousIndex, currentIndex);
    this.orderedServices.set(next);
    const orderedServiceIds = next.map((s) => s.id!).filter(Boolean);
    lastValueFrom(
      this.servicesService.reorderServices({ orderedServiceIds, categoryId: this.category().id }),
    )
      .then(() => this.servicesList.reload())
      .catch((err) => {
        this.orderedServices.set(prev);
        this.message.add({
          severity: 'error',
          summary: 'Błąd',
          detail: 'Nie udało się zmienić kolejności usług.',
        });
        console.error('reorderServices failed:', err);
      });
  }

  handleDeleteService(serviceId: string | undefined) {
    this.confirmationService.confirm({
      message:
        'Usługa zostanie ukryta w nowych rezerwacjach. Historia wizyt pozostaje nietknięta.',
      header: 'Usunąć usługę?',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Usuń',
      rejectLabel: 'Anuluj',
      accept: () => {
        if (!serviceId) return;
        this.servicesService.deleteService(serviceId).subscribe({
          next: () => this.servicesList.reload(),
          error: (err) => console.error('Błąd podczas usuwania usługi:', err),
        });
      },
    });
  }
}
