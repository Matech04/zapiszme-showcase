import { CommonModule } from "@angular/common";
import { Component, computed, input, output } from "@angular/core";
import { ServicesList } from "../feature/services-list.component";
import { ServiceCategoryDto } from "@core/api/api-client";
import { ButtonModule } from "primeng/button";
import { TieredMenuModule } from 'primeng/tieredmenu';
import { MenuItem } from "primeng/api";

@Component({
  selector: 'app-service-category-card',
  standalone: true,
  imports: [CommonModule, ButtonModule, TieredMenuModule, ServicesList],
  template: `
  <section
    class="rounded-2xl border border-surface-200/70 dark:border-surface-200/60 bg-white/70 dark:bg-surface-50/40 overflow-hidden"
    data-testid="category-section"
  >
    <!-- Nagłówek kategorii: nazwa + licznik + akcje (usługi zawsze widoczne poniżej). -->
    <div class="flex items-center gap-2 px-3 py-2.5">
      <h3 class="text-lg sm:text-xl font-black tracking-tight text-surface-900 m-0 truncate">{{ title() }}</h3>
      <span class="shrink-0 text-sm font-bold text-surface-500 dark:text-surface-400 bg-surface-100 dark:bg-surface-100 rounded-full px-2.5 py-0.5">
        {{ serviceCount() }}
      </span>

      @if (!reorderMode()) {
        <div class="ml-auto flex items-center gap-1 shrink-0">
          <button
            type="button"
            data-testid="category-add-service-btn"
            (click)="addService.emit()"
            class="flex items-center gap-1.5 px-3 py-2 rounded-xl text-sm font-bold text-primary hover:bg-primary-50 dark:hover:bg-primary-900/30 transition-colors cursor-pointer">
            <i class="pi pi-plus text-xs"></i>
            <span>Dodaj</span>
          </button>

          <p-button
            icon="pi pi-ellipsis-h"
            (click)="menu.toggle($event)"
            styleClass="w-11 h-11 p-0 text-surface-500 hover:text-surface-900 hover:bg-surface-100 dark:hover:bg-surface-100 rounded-full transition-colors flex items-center justify-center cursor-pointer"
            [text]="true"
            [plain]="true" />
          <p-tieredmenu #menu [popup]="true" [model]="items" appendTo="body" />
        </div>
      }
    </div>

    <div class="border-t border-surface-100 dark:border-surface-200/50 px-1.5 py-1">
      <app-services-list
        [category]="category()"
        [refreshTick]="refreshTick()"
        [reorderMode]="reorderMode()"
        (editService)="editService.emit($event)"
        (addService)="addService.emit()"
      />
    </div>
  </section>
  `
})
export class ServiceCategoryCardComponent {


  category = input.required<ServiceCategoryDto>();
  /** Licznik odświeżenia z katalogu — przekazywany do `app-services-list`, by wymusić reload listy usług. */
  refreshTick = input<number>(0);
  /** Liczba usług w kategorii (z katalogu) — pokazywana obok nazwy sekcji. */
  serviceCount = input<number>(0);
  /** Tryb zmiany kolejności — chowa akcje (dodaj/kebab), pokazuje uchwyty w liście usług. */
  reorderMode = input<boolean>(false);
  title = computed(() => this.category().name);

  addService = output<void>();
  editCategory = output<string | undefined>();
  deleteCategory = output<string | undefined>();
  editService = output<string | undefined>();
  deleteService = output<string | undefined>();

  items: MenuItem[] = [
    { label: 'Edytuj', icon: 'pi pi-pencil', command: () => this.editCategory.emit(this.category().id) },
    { label: 'Usuń', icon: 'pi pi-trash', command: () => this.deleteCategory.emit(this.category().id) }
  ];
}
