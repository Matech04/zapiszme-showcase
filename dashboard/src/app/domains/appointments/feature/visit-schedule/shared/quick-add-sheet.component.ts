import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AppointmentPreviewDto,
  EmployeeLeaveDto,
  EmployeeScheduleDto,
  ScheduleOverrideDto,
} from '@core/api/api-client';
import { ButtonModule } from 'primeng/button';
import { AdminDrawerComponent } from './admin-drawer.component';
import {
  CreateAppointmentContext,
  CreateAppointmentDrawerComponent,
} from './create-appointment-drawer.component';
import {
  BreakEditorContext,
  BreakEditorDrawerComponent,
} from './break-editor-drawer.component';

type QuickAddTab = 'visit' | 'break';

/**
 * Mobilny arkusz „szybkiego dodawania" otwierany z FAB. Zakładki u góry: Wizyta / Przerwa.
 * Hostuje kreator wizyty i edytor przerwy w trybie `embedded` (bez własnego drawera) i steruje
 * jedną wspólną stopką (submit aktywnej zakładki). Na desktopie te akcje są osobnymi chipami,
 * więc ten arkusz jest używany tylko na wąskich ekranach (FAB jest `lg:hidden`).
 */
@Component({
  selector: 'app-quick-add-sheet',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    ButtonModule,
    AdminDrawerComponent,
    CreateAppointmentDrawerComponent,
    BreakEditorDrawerComponent,
  ],
  template: `
    <app-admin-drawer
      [isOpen]="isOpen()"
      [isDesktop]="isDesktop()"
      [title]="effectiveTab() === 'visit' ? 'Nowa wizyta' : 'Nowa przerwa'"
      label="Kalendarz"
      [closeable]="!activeSubmitting()"
      (closeRequested)="closeRequested.emit()"
    >
      <div drawer-body class="flex flex-col gap-5">
        <!-- Zakładki — tylko gdy przerwa dozwolona (grafik dynamiczny). Dla statycznego zostaje sama wizyta. -->
        @if (allowBreak()) {
        <div
          class="grid grid-cols-2 gap-1 p-1 rounded-2xl bg-surface-100 dark:bg-surface-100/60"
          role="tablist"
        >
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="activeTab() === 'visit'"
            (click)="activeTab.set('visit')"
            [class.bg-surface-0]="activeTab() === 'visit'"
            [class.dark:bg-surface-50]="activeTab() === 'visit'"
            [class.shadow-sm]="activeTab() === 'visit'"
            [class.text-surface-900]="activeTab() === 'visit'"
            [class.text-surface-500]="activeTab() !== 'visit'"
            class="flex items-center justify-center gap-2 rounded-xl py-2 text-sm font-bold transition-colors"
            data-testid="quick-tab-visit"
          >
            <i class="pi pi-plus text-xs" aria-hidden="true"></i>
            Wizyta
          </button>
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="activeTab() === 'break'"
            (click)="activeTab.set('break')"
            [class.bg-surface-0]="activeTab() === 'break'"
            [class.dark:bg-surface-50]="activeTab() === 'break'"
            [class.shadow-sm]="activeTab() === 'break'"
            [class.text-surface-900]="activeTab() === 'break'"
            [class.text-surface-500]="activeTab() !== 'break'"
            class="flex items-center justify-center gap-2 rounded-xl py-2 text-sm font-bold transition-colors"
            data-testid="quick-tab-break"
          >
            <i class="pi pi-pause text-xs" aria-hidden="true"></i>
            Przerwa
          </button>
        </div>
        }

        <!-- Treść zakładek (obie w DOM — stan formularzy zachowany przy przełączaniu) -->
        <div [class.hidden]="effectiveTab() !== 'visit'">
          <app-create-appointment-drawer
            [embedded]="true"
            [context]="visitContext()"
            [allowEmployeeChange]="allowEmployeeChange()"
            (success)="createSuccess.emit($event)"
          />
        </div>
        <div [class.hidden]="effectiveTab() !== 'break'">
          <app-break-editor-drawer
            [embedded]="true"
            [context]="breakCtx()"
            [schedules]="schedules()"
            [overrides]="overrides()"
            [leaves]="leaves()"
            [dayAppointments]="dayAppointments()"
            [slotStepMinutes]="slotStepMinutes()"
            (success)="breakSuccess.emit()"
          />
        </div>
      </div>

      <div drawer-footer class="flex gap-3">
        <p-button
          label="Anuluj"
          [outlined]="true"
          [disabled]="activeSubmitting()"
          (onClick)="closeRequested.emit()"
          styleClass="flex-1 py-3 font-bold uppercase tracking-wider"
        />
        <p-button
          [label]="activeSubmitLabel()"
          [loading]="activeSubmitting()"
          [disabled]="!activeCanSubmit()"
          (onClick)="submitActive()"
          styleClass="flex-[2] py-3 font-bold uppercase tracking-wider"
        />
      </div>
    </app-admin-drawer>
  `,
})
export class QuickAddSheetComponent {
  readonly isOpen = input<boolean>(false);
  readonly isDesktop = input<boolean>(false);
  /** yyyy-MM-dd wybranego dnia. */
  readonly date = input<string>('');
  readonly employeeId = input<string | undefined>(undefined);
  readonly allowEmployeeChange = input<boolean>(true);
  readonly schedules = input<EmployeeScheduleDto[] | undefined>(undefined);
  readonly overrides = input<ScheduleOverrideDto[] | undefined>(undefined);
  readonly leaves = input<EmployeeLeaveDto[] | undefined>(undefined);
  readonly dayAppointments = input<AppointmentPreviewDto[]>([]);
  readonly slotStepMinutes = input<number>(15);
  /** Czy dostępna jest zakładka „Przerwa" — false np. dla grafiku statycznego (przerwy tylko dynamiczne). */
  readonly allowBreak = input<boolean>(true);

  readonly closeRequested = output<void>();
  readonly createSuccess = output<string>();
  readonly breakSuccess = output<void>();

  protected readonly activeTab = signal<QuickAddTab>('visit');

  /** Efektywna zakładka: gdy przerwy niedozwolone, zawsze „visit" (nawet gdy activeTab został „break"). */
  protected readonly effectiveTab = computed<QuickAddTab>(() =>
    this.allowBreak() ? this.activeTab() : 'visit',
  );

  private readonly createRef = viewChild(CreateAppointmentDrawerComponent);
  private readonly breakRef = viewChild(BreakEditorDrawerComponent);

  /** Kontekst kreatora wizyty — niepusty tylko gdy arkusz otwarty (inaczej formularz się resetuje). */
  protected readonly visitContext = computed<CreateAppointmentContext | null>(() => {
    if (!this.isOpen()) return null;
    const emp = this.employeeId();
    return { date: this.date(), ...(emp ? { employeeId: emp } : {}) };
  });

  protected readonly breakCtx = computed<BreakEditorContext | null>(() => {
    if (!this.isOpen()) return null;
    const emp = this.employeeId();
    if (!emp) return null;
    return { employeeId: emp, date: this.date() };
  });

  protected readonly activeCanSubmit = computed(() =>
    this.effectiveTab() === 'visit'
      ? (this.createRef()?.canSubmit() ?? false)
      : (this.breakRef()?.canSubmit() ?? false),
  );

  protected readonly activeSubmitting = computed(() =>
    this.effectiveTab() === 'visit'
      ? (this.createRef()?.submitting() ?? false)
      : (this.breakRef()?.submitting() ?? false),
  );

  protected readonly activeSubmitLabel = computed(() =>
    this.effectiveTab() === 'visit'
      ? (this.createRef()?.submitLabel() ?? 'Zarezerwuj')
      : 'Dodaj przerwę',
  );

  protected submitActive(): void {
    if (this.effectiveTab() === 'visit') this.createRef()?.submit();
    else this.breakRef()?.submit();
  }
}
