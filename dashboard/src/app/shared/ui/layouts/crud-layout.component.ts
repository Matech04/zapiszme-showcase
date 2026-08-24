import { Component, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

@Component({
  selector: 'app-crud-layout',
  standalone: true,
  imports: [ButtonModule, ProgressSpinnerModule],
  template: `
    <div class="admin-glass-card rounded-4xl p-5 sm:p-6 flex flex-col gap-6">
      
      <div class="flex flex-row justify-between items-center border-b pb-4 border-surface-200/70 dark:border-surface-200/70">
        
        <h1 class="text-2xl font-black tracking-tight text-surface-900">
          {{ title() }}
        </h1>
        
        <p-button 
          [label]="addButtonLabel()" 
          icon="pi pi-plus" 
          (onClick)="addClicked.emit()" 
          styleClass="font-bold"
        />
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-5">

        @if (isLoading()){
          <div class="col-span-full flex justify-center p-10">
              <p-progressSpinner class="w-12 h-12" />
          </div>
        }

        <ng-content></ng-content>
      </div>

    </div>
    `
})
export class CrudLayoutComponent {
  // Nowoczesne Inputy (Signals)
  title = input.required<string>();
  addButtonLabel = input<string>('Dodaj'); // Domyślnie "Dodaj", ale można zmienić
  isLoading = input<boolean>(false);

  // Nowoczesny Output
  addClicked = output<void>();
}