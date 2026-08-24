import { Component, effect, input, output } from '@angular/core';
import { Button } from 'primeng/button';
import { DatePicker } from 'primeng/datepicker';
import { FormsModule } from '@angular/forms';

const TIME_INPUT_CLASS =
  '!bg-transparent !border-0 !shadow-none !p-0 !text-center tabular-nums w-[5rem] text-sm font-medium text-surface-900 dark:text-surface-0';

@Component({
  selector: 'app-slot-time-row',
  standalone: true,
  imports: [Button, DatePicker, FormsModule],
  template: `
<div class="group flex items-center gap-2 sm:gap-3 rounded-xl bg-surface-100/90 dark:bg-surface-800/80 px-3 py-2.5 sm:px-4 border border-transparent hover:border-surface-200 dark:hover:border-surface-600 transition-colors">
  <i class="pi pi-clock text-primary text-sm shrink-0" aria-hidden="true"></i>

  <div class="flex-1 flex justify-center min-w-0">
    <p-date-picker
      [showIcon]="false"
      [timeOnly]="true"
      [readonlyInput]="false"
      [ngModel]="timeAsDate"
      (ngModelChange)="onTimeChange($event)"
      [inputStyleClass]="timeInputClass"
      hourFormat="24"
      ariaLabel="Godzina startu"
    />
  </div>

  <p-button
    icon="pi pi-trash"
    severity="danger"
    [text]="true"
    [rounded]="true"
    styleClass="!p-2 opacity-80 hover:opacity-100"
    ariaLabel="Usuń godzinę"
    (onClick)="deleteTime.emit()"
  />
</div>
  `,
})
export class SlotTimeRow {
  /** Godzina w formacie HH:mm. */
  readonly time = input.required<string>();
  readonly timeInputClass = TIME_INPUT_CLASS;

  changedTime = output<string>();
  deleteTime = output<void>();

  timeAsDate: Date | undefined;

  constructor() {
    effect(() => {
      this.timeAsDate = this.parseTimeToDate(this.time());
    });
  }

  onTimeChange(newDate: Date | undefined) {
    if (!newDate) return;
    const hours = newDate.getHours().toString().padStart(2, '0');
    const minutes = newDate.getMinutes().toString().padStart(2, '0');
    this.changedTime.emit(`${hours}:${minutes}`);
  }

  private parseTimeToDate(timeStr: string | undefined): Date | undefined {
    if (!timeStr) return undefined;
    const [hours, minutes] = timeStr.split(':').map(Number);
    const date = new Date();
    date.setHours(hours, minutes, 0, 0);
    return date;
  }
}
