import { Component, effect, model, output } from '@angular/core';
import { TimeRangeDto } from '@core/api/api-client';
import { Button } from "primeng/button";
import { DatePicker } from "primeng/datepicker";
import { FormsModule } from '@angular/forms';

const TIME_INPUT_CLASS =
  '!bg-transparent !border-0 !shadow-none !p-0 !text-center tabular-nums w-[4.75rem] sm:w-[5rem] text-sm font-medium text-surface-900';

@Component({
  selector: 'app-time-block',
  standalone: true,
  imports: [Button, DatePicker, FormsModule],
  template: `
<div class="group flex items-center gap-2 sm:gap-3 rounded-xl bg-surface-100/90 dark:bg-surface-100/80 px-3 py-2.5 sm:px-4 border border-transparent hover:border-surface-200 dark:hover:border-surface-600 transition-colors">
  <i class="pi pi-clock text-primary text-sm shrink-0" aria-hidden="true"></i>

  <div class="flex flex-1 items-center min-w-0">
    <div class="flex-1 flex justify-center min-w-0">
      <p-date-picker
        [showIcon]="false"
        [timeOnly]="true"
        [readonlyInput]="false"
        [ngModel]="startTimeAsDate"
        (ngModelChange)="onTimeChange($event, 'startTime')"
        [inputStyleClass]="timeInputClass"
        hourFormat="24"
      />
    </div>
    <div
      class="shrink-0 flex items-center justify-center px-2 sm:px-2.5 self-stretch"
      role="presentation"
      aria-hidden="true"
    >
      <span class="h-4 w-px rounded-full bg-surface-300 dark:bg-surface-600"></span>
    </div>
    <div class="flex-1 flex justify-center min-w-0">
      <p-date-picker
        [showIcon]="false"
        [timeOnly]="true"
        [readonlyInput]="false"
        [ngModel]="endTimeAsDate"
        (ngModelChange)="onTimeChange($event, 'endTime')"
        [inputStyleClass]="timeInputClass"
        hourFormat="24"
      />
    </div>
  </div>

  <p-button
    icon="pi pi-trash"
    severity="danger"
    [text]="true"
    [rounded]="true"
    styleClass="!p-2 opacity-80 hover:opacity-100"
    ariaLabel="Usuń przedział"
    (onClick)="deleteBlock.emit()"
  />
</div>
  `
})
export class TimeBlock {
  readonly block = model.required<TimeRangeDto>();
  readonly timeInputClass = TIME_INPUT_CLASS;

  changedBlock = output<TimeRangeDto>();

  deleteBlock = output();

  startTimeAsDate: Date | undefined;
  endTimeAsDate: Date | undefined;

  constructor() {
    effect(() => {
      this.startTimeAsDate = this.parseTimeToDate(this.block().startTime);
      this.endTimeAsDate = this.parseTimeToDate(this.block().endTime);
    });
  }

  onTimeChange(newDate: Date | undefined, field: 'startTime' | 'endTime') {
    if (!newDate) return;

    const hours = newDate.getHours().toString().padStart(2, '0');
    const minutes = newDate.getMinutes().toString().padStart(2, '0');
    const timeString = `${hours}:${minutes}`;

    this.changedBlock.emit({
      ...this.block(),
      [field]: timeString
    });
  }

  private parseTimeToDate(timeStr: string | undefined): Date | undefined {
    if (!timeStr) return undefined;
    const [hours, minutes] = timeStr.split(':').map(Number);
    const date = new Date();
    date.setHours(hours, minutes, 0, 0);
    return date;
  }
}
