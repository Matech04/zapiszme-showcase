import { Component, inject, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePickerModule } from 'primeng/datepicker';
import { Button } from "primeng/button";
import { DynamicDialogRef } from 'primeng/dynamicdialog';

@Component({
  selector: 'app-pick-date-form',
  template: `
        <div class="card flex justify-center">
            <p-datepicker class="max-w-full" [(ngModel)]="date" [inline]="true" [showWeek]="true" />
        </div>
        <div class="flex flex-row justify-around mt-6">
        <p-button
        (onClick)="submitDate()"
        >Confirm</p-button>

        <p-button
        (onClick)="close()"
        >
          Close
        </p-button>
        </div>
    `,
  standalone: true,
  imports: [DatePickerModule, FormsModule, Button]
})
export class PickDateForm {
  date: Date | undefined;

  protected ref = inject(DynamicDialogRef);

  submitDate() {
    if (this.date) {
      if (this.ref != null)
        this.ref.close(this.formatDateToLocalString(this.date));
    }
  }

  close() {

    if (this.ref != null)
      this.ref.close();

  }


  formatDateToLocalString(date: Date): string {
    const year = date.getFullYear();
    // getMonth() zwraca 0-11, więc dodajemy +1. padStart dodaje zero wiodące (01, 02...)
    const month = (date.getMonth() + 1).toString().padStart(2, '0');
    const day = date.getDate().toString().padStart(2, '0');

    return `${year}-${month}-${day}`;
  }

}