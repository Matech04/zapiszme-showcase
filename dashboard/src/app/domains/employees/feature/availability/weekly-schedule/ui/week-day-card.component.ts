import { Component, computed, inject, input, output } from "@angular/core";
import { FormsModule } from "@angular/forms";
import { ToggleSwitch } from "primeng/toggleswitch";
import { Menu } from "primeng/menu";
import { Button } from "primeng/button";
import { MenuItem } from "primeng/api";
import { DayScheduleUi } from "../weekly-schedule.component";
import { TimeBlock } from "./time-block/time-block";
import { SlotTimeRow } from "./slot-time-row/slot-time-row";
import { TimeRangeDto, ShiftTemplateDto, SlotGenerationMode } from "@core/api/api-client";

@Component({
  selector: 'week-day-card',
  standalone: true,
  imports: [TimeBlock, SlotTimeRow, ToggleSwitch, FormsModule, Menu, Button],
  template: `
  <div
    [attr.data-testid]="'day-card-' + day().dayKey + '-' + day().weekIndex"
    class="rounded-2xl border bg-surface-0 dark:bg-surface-50 p-4 sm:p-5 shadow-[0_1px_3px_rgba(0,0,0,0.06)] dark:shadow-none transition-opacity"
    [class.border-surface-200/90]="!validationError()"
    [class.dark:border-surface-100]="!validationError()"
    [class.border-red-400]="!!validationError()"
    [class.dark:border-red-700]="!!validationError()"
    [class.opacity-80]="!day().isWorking && !validationError()"
  >
    <div class="flex flex-row items-start justify-between gap-4 mb-4">
      <div class="min-w-0 flex-1">
        <h3 class="text-base sm:text-lg font-bold text-surface-900 leading-tight flex items-center gap-2">
          {{ day().dayName }}
          @if (validationError()) {
            <i class="pi pi-exclamation-circle text-red-500 dark:text-red-400 text-sm" [attr.aria-label]="'Błąd walidacji w dniu: ' + day().dayName"></i>
          }
        </h3>
        <p
          class="text-xs sm:text-sm mt-1"
          [class.text-surface-500]="day().isWorking"
          [class.dark:text-surface-400]="day().isWorking"
          [class.text-surface-400]="!day().isWorking"
          [class.dark:text-surface-500]="!day().isWorking"
        >
          {{ day().isWorking ? 'Dzień roboczy' : 'Dzień wolny' }}
        </p>
      </div>
      <div class="flex items-center gap-1 shrink-0">
        @if (day().isWorking && canCopy()) {
          <p-button
            icon="pi pi-ellipsis-v"
            [text]="true"
            [rounded]="true"
            severity="secondary"
            styleClass="!p-2 opacity-80 hover:opacity-100"
            [attr.data-testid]="'day-actions-' + day().dayKey + '-' + day().weekIndex"
            ariaLabel="Więcej akcji dla dnia"
            (onClick)="dayMenu.toggle($event)"
          />
          <p-menu #dayMenu [popup]="true" [model]="dayMenuItems()" [appendTo]="'body'" />
        }
        <p-toggleswitch
          [attr.data-testid]="'work-toggle-' + day().dayKey + '-' + day().weekIndex"
          [ngModel]="day().isWorking"
          (ngModelChange)="onWorkingToggle($event)"
          [inputId]="'work-' + day().dayKey + '-' + day().weekIndex"
          [attr.aria-label]="'Praca w dniu: ' + day().dayName"
        />
      </div>
    </div>

    @if (validationError()) {
      <div role="alert" class="mb-4 rounded-xl border border-red-300 dark:border-red-700/60 bg-red-50 dark:bg-red-950/30 px-3 py-2 text-sm text-red-800 dark:text-red-200 flex items-start gap-2">
        <i class="pi pi-exclamation-triangle text-xs mt-0.5 shrink-0"></i>
        <span>{{ validationError() }}</span>
      </div>
    }

    @if (day().isWorking && templateMenuItems().length > 0) {
      <div class="mb-4">
        <p-button
          label="Użyj szablonu"
          icon="pi pi-bolt"
          severity="secondary"
          [outlined]="true"
          styleClass="w-full !justify-center font-semibold"
          [attr.data-testid]="'use-template-' + day().dayKey + '-' + day().weekIndex"
          (onClick)="templatesMenu.toggle($event)"
          [attr.aria-label]="'Użyj szablonu w dniu ' + day().dayName"
        />
        <p-menu #templatesMenu [popup]="true" [model]="templateMenuItems()" styleClass="shift-template-menu" [appendTo]="'body'" />
      </div>
    }

    @if (isFixed()) {
      @if (day().isWorking) {
        <div class="mb-2">
          <h4 class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 mb-2">
            Godziny startu
          </h4>
          <div class="flex flex-col gap-2.5">
            @for (time of day().fixedStartTimes; track $index) {
              <app-slot-time-row
                [time]="time"
                (changedTime)="updateFixedTime($event, $index)"
                (deleteTime)="deleteFixedTime($index)"
              />
            } @empty {
              <p class="text-sm text-surface-500 dark:text-surface-400 text-center py-2">
                Dodaj co najmniej jedną godzinę startu.
              </p>
            }
          </div>
          <button
            type="button"
            [attr.data-testid]="'add-fixed-time-' + day().dayKey + '-' + day().weekIndex"
            class="mt-3 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-primary/35 dark:border-primary/40 bg-transparent py-3 px-3 text-sm font-semibold text-primary hover:bg-primary/5 transition-colors"
            (click)="addFixedTime()"
          >
            <i class="pi pi-plus text-xs" aria-hidden="true"></i>
            Dodaj godzinę
          </button>
        </div>
      } @else {
        <p class="text-sm italic text-surface-400 dark:text-surface-500 py-1">
          Brak zdefiniowanych godzin
        </p>
      }
    } @else {
    @if (day().isWorking) {
      <div class="mb-2">
        <h4 class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 mb-2">
          Bloki pracy
        </h4>
        <div class="flex flex-col gap-2.5">
          @for (block of day().workRanges; track $index) {
            <app-time-block
              [block]="block"
              (changedBlock)="updateWorkRange($event, $index)"
              (deleteBlock)="deleteWorkRange($index)"
            />
          } @empty {
            <p class="text-sm text-surface-500 dark:text-surface-400 text-center py-2">
              Dodaj co najmniej jeden blok pracy.
            </p>
          }
        </div>
        <button
          type="button"
          class="mt-3 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-primary/35 dark:border-primary/40 bg-transparent py-3 px-3 text-sm font-semibold text-primary hover:bg-primary/5 transition-colors"
          (click)="addWorkRange()"
        >
          <i class="pi pi-plus text-xs" aria-hidden="true"></i>
          Dodaj blok pracy
        </button>
      </div>

      <div class="mt-4 pt-4 border-t border-surface-200/70 dark:border-surface-100">
        <h4 class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 mb-2">
          Przerwy
        </h4>
        <div class="flex flex-col gap-2.5">
          @for (br of day().breaks; track $index) {
            <app-time-block
              [block]="br"
              (changedBlock)="updateBreak($event, $index)"
              (deleteBlock)="deleteBreak($index)"
            />
          } @empty {
            <p class="text-sm text-surface-500 dark:text-surface-400 italic py-1">
              Brak zdefiniowanych przerw.
            </p>
          }
        </div>
        <button
          type="button"
          class="mt-3 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-surface-300/70 dark:border-surface-200 bg-transparent py-3 px-3 text-sm font-semibold text-surface-700 hover:bg-surface-100 dark:hover:bg-surface-100/70 transition-colors"
          (click)="addBreak()"
        >
          <i class="pi pi-plus text-xs" aria-hidden="true"></i>
          Dodaj przerwę
        </button>
      </div>
    } @else {
      <p class="text-sm italic text-surface-400 dark:text-surface-500 py-1">
        Brak zdefiniowanych godzin pracy
      </p>
    }
    }
  </div>
`
})
export class WeekDayCardComponent {
  day = input.required<DayScheduleUi>();
  mode = input<SlotGenerationMode>(SlotGenerationMode.Grid);
  templates = input<ShiftTemplateDto[]>([]);

  isFixed = computed(() => this.mode() === SlotGenerationMode.FixedStartTimes);
  /** Pojedynczy błąd walidacji dla tego dnia. Czyszczony przez parent po edycji. */
  validationError = input<string | undefined>(undefined);

  changedDay = output<DayScheduleUi>();
  /** Prośba o skopiowanie godzin tego dnia do pozostałych dni roboczych (obsługa w parencie). */
  copyToOthers = output<DayScheduleUi>();

  /** Czy dzień ma co kopiować (bloki pracy lub godziny startu, zależnie od trybu). */
  canCopy = computed(() =>
    this.isFixed() ? this.day().fixedStartTimes.length > 0 : this.day().workRanges.length > 0,
  );

  dayMenuItems = computed<MenuItem[]>(() => [
    {
      label: 'Skopiuj do pozostałych dni roboczych',
      icon: 'pi pi-copy',
      command: () => this.copyToOthers.emit(this.day()),
    },
  ]);

  templateMenuItems = computed<MenuItem[]>(() => {
    const list = this.templates() ?? [];
    if (list.length === 0) return [];
    return list.map((t) => ({
      label: t.name ?? 'Szablon',
      icon: 'pi pi-clock',
      command: () => this.applyTemplate(t),
    }));
  });

  onWorkingToggle(enabled: boolean) {
    if (this.isFixed()) {
      if (enabled) {
        this.changedDay.emit({
          ...this.day(),
          isWorking: true,
          fixedStartTimes: this.day().fixedStartTimes.length ? [...this.day().fixedStartTimes] : ['09:00'],
        });
      } else {
        this.changedDay.emit({
          ...this.day(),
          isWorking: false,
          fixedStartTimes: [],
        });
      }
      return;
    }

    if (enabled) {
      this.changedDay.emit({
        ...this.day(),
        isWorking: true,
        workRanges: this.day().workRanges.length ? [...this.day().workRanges] : [{ startTime: '09:00', endTime: '17:00' }],
      });
    } else {
      this.changedDay.emit({
        ...this.day(),
        isWorking: false,
        workRanges: [],
        breaks: [],
      });
    }
  }

  updateFixedTime(time: string, index: number) {
    const next = this.day().fixedStartTimes.map((t, i) => (i === index ? time : t));
    this.changedDay.emit({ ...this.day(), fixedStartTimes: next });
  }

  addFixedTime() {
    const next = [...this.day().fixedStartTimes, '12:00'];
    this.changedDay.emit({ ...this.day(), isWorking: true, fixedStartTimes: next });
  }

  deleteFixedTime(index: number) {
    const remaining = this.day().fixedStartTimes.filter((_, i) => i !== index);
    this.changedDay.emit({ ...this.day(), fixedStartTimes: remaining });
  }

  updateWorkRange(updated: TimeRangeDto, index: number) {
    const next = this.day().workRanges.map((r, i) =>
      i === index
        ? {
            startTime: updated.startTime ?? '',
            endTime: updated.endTime ?? '',
          }
        : r,
    );
    this.changedDay.emit({ ...this.day(), workRanges: next });
  }

  addWorkRange() {
    const next = [...this.day().workRanges, { startTime: '09:00', endTime: '17:00' }];
    this.changedDay.emit({ ...this.day(), isWorking: true, workRanges: next });
  }

  deleteWorkRange(index: number) {
    const remaining = this.day().workRanges.filter((_, i) => i !== index);
    this.changedDay.emit({ ...this.day(), workRanges: remaining });
  }

  updateBreak(updated: TimeRangeDto, index: number) {
    const next = this.day().breaks.map((r, i) =>
      i === index
        ? {
            startTime: updated.startTime ?? '',
            endTime: updated.endTime ?? '',
          }
        : r,
    );
    this.changedDay.emit({ ...this.day(), breaks: next });
  }

  addBreak() {
    const next = [...this.day().breaks, { startTime: '12:00', endTime: '12:30' }];
    this.changedDay.emit({ ...this.day(), breaks: next });
  }

  deleteBreak(index: number) {
    const remaining = this.day().breaks.filter((_, i) => i !== index);
    this.changedDay.emit({ ...this.day(), breaks: remaining });
  }

  private applyTemplate(template: ShiftTemplateDto) {
    const toHm = (t?: string) => (t ? t.substring(0, 5) : '');

    if (this.isFixed()) {
      const fixedStartTimes = (template.fixedStartTimes ?? [])
        .filter((t) => !!t)
        .map((t) => toHm(t));
      if (fixedStartTimes.length === 0) return;
      this.changedDay.emit({ ...this.day(), isWorking: true, fixedStartTimes });
      return;
    }

    const workRanges = (template.workRanges ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => ({ startTime: toHm(r.startTime), endTime: toHm(r.endTime) }));
    const breaks = (template.breaks ?? [])
      .filter((r) => !!r?.startTime && !!r?.endTime)
      .map((r) => ({ startTime: toHm(r.startTime), endTime: toHm(r.endTime) }));

    if (workRanges.length === 0) return;

    this.changedDay.emit({
      ...this.day(),
      isWorking: true,
      workRanges,
      breaks,
    });
  }
}
