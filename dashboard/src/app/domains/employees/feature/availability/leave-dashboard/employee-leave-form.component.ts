import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { form, FormField, required, validate } from '@angular/forms/signals';
import { CommonModule } from '@angular/common';
import { Button } from 'primeng/button';
import { EMPTY } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { MessageService } from 'primeng/api';
import { AbsenceType, EmployeesClient } from '@core/api/api-client';
import { createFormActions } from '@shared/utils/createFormActions';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { FormFieldCalendarComponent } from '@shared/ui/forms/form-field-calendar.component';
import {
  FormFieldSelectComponent,
  type SelectOption,
} from '@shared/ui/forms/form-field-select.component';
import { ActivatedRoute, Router } from '@angular/router';

interface EmployeeLeaveFormModel {
  startDate: string;
  endDate: string;
  /** Typ nieobecności jako string ze stałych poniżej (FormFieldSelectComponent przyjmuje string). */
  absenceType: string;
}

function toYyyyMmDd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function todayYyyyMmDd(): string {
  return toYyyyMmDd(new Date());
}

function addDaysYyyyMmDd(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return toYyyyMmDd(d);
}

/** Następny pełen tydzień roboczy (poniedziałek-piątek), niezależnie od dnia tygodnia. */
function nextWeekRange(): { start: string; end: string } {
  const today = new Date();
  const day = today.getDay(); // 0=Sun, 1=Mon, ..., 6=Sat
  // Dni do następnego poniedziałku (zawsze przyszłość — jeśli dziś pon, to za 7 dni).
  const daysToNextMonday = day === 0 ? 1 : 8 - day;
  const start = new Date(today);
  start.setDate(today.getDate() + daysToNextMonday);
  const end = new Date(start);
  end.setDate(start.getDate() + 4); // poniedziałek + 4 = piątek
  return { start: toYyyyMmDd(start), end: toYyyyMmDd(end) };
}

@Component({
  selector: 'app-employee-leave-form',
  standalone: true,
  imports: [CommonModule, FormLayoutComponent, FormFieldCalendarComponent, FormFieldSelectComponent, FormField, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
  @if (embedded()) {
    <ng-container [ngTemplateOutlet]="fields" />
  } @else {
    <app-form-layout
      title="Nowy urlop"
      (submit)="FormActions.handleSubmit()"
      (cancel)="FormActions.handleCancel()"
      [isEdit]="false"
      [confirmOnCancel]="true"
      [testId]="'employee-leave-form'"
    >
      <ng-container [ngTemplateOutlet]="fields" />
    </app-form-layout>
  }

  <ng-template #fields>
    <div class="grid grid-cols-1 gap-6">
      <app-form-field-select
        testId="leave-absence-type"
        data-tour="leave-type"
        label="Typ"
        id="absenceType"
        placeholder="Wybierz typ"
        [options]="absenceTypeOptions"
        [formField]="leaveForm.absenceType"
      />

      <div>
        <p class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 mb-2">
          Szybki wybór
        </p>
        <div class="flex flex-wrap gap-2">
          <p-button
            label="Dziś"
            icon="pi pi-bolt"
            severity="secondary"
            size="small"
            [outlined]="true"
            (onClick)="applyQuickRange('today')"
          />
          <p-button
            label="Jutro"
            icon="pi pi-bolt"
            severity="secondary"
            size="small"
            [outlined]="true"
            (onClick)="applyQuickRange('tomorrow')"
          />
          <p-button
            label="Następny tydzień (pon-pt)"
            icon="pi pi-bolt"
            severity="secondary"
            size="small"
            [outlined]="true"
            (onClick)="applyQuickRange('nextWeek')"
          />
        </div>
      </div>

      <div class="grid grid-cols-1 sm:grid-cols-2 gap-6">
        <app-form-field-calendar
          testId="leave-start-date"
          data-tour="leave-start"
          label="Data rozpoczęcia"
          id="startDate"
          [formField]="leaveForm.startDate"
        />

        <app-form-field-calendar
          testId="leave-end-date"
          label="Data zakończenia"
          id="endDate"
          [notBefore]="leaveModel().startDate || undefined"
          [formField]="leaveForm.endDate"
        />
      </div>
    </div>
  </ng-template>
  `
})
export class EmployeeLeaveForm {
  /** Identyfikator pracownika (parametr trasy `:id`). */
  id = input.required<string>();

  /** Tryb osadzony (drawer) — bez `app-form-layout`, bez nawigacji; emituje `saved`/`cancelled`. */
  embedded = input<boolean>(false);
  /** Data startowa (YYYY-MM-DD) podawana w trybie osadzonym zamiast query param `?date`. */
  initialDate = input<string | null>(null);

  /** Emitowane po udanym zapisie (tryb osadzony — rodzic zamyka drawer + odświeża dane). */
  saved = output<void>();
  /** Emitowane przy anulowaniu (tryb osadzony). */
  cancelled = output<void>();

  private employeesClient = inject(EmployeesClient);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private messageService = inject(MessageService);
  private isSelfMode = () => this.router.url.startsWith('/admin/my-availability/');

  protected readonly absenceTypeOptions: SelectOption[] = [
    { label: 'Urlop', value: String(AbsenceType.Vacation) },
    { label: 'Chorobowe', value: String(AbsenceType.SickLeave) },
  ];

  /**
   * Initial value: dziś + dziś (1-dniowy urlop) — najczęstszy case SOLO ownera.
   * Jeśli przyszliśmy z modalu kalendarza (`?date=YYYY-MM-DD`), preferujemy tę datę.
   * Inicjowane w field-initializerze, bo `route` jest deklarowane wyżej w klasie.
   * Plus signal `queryDate` + effect poniżej obsługują re-navigation (gdy Angular re-używa komponent).
   */
  leaveModel = signal<EmployeeLeaveFormModel>(this.buildInitialLeaveModel());

  /** Reaktywne źródło `?date=...` — działa też przy zmianie query bez re-tworzenia komponentu. */
  private readonly queryDate = toSignal(
    this.route.queryParamMap.pipe(map((m) => m.get('date'))),
    { initialValue: this.route.snapshot.queryParamMap.get('date') },
  );

  /** Sync startDate/endDate z query param przy każdej zmianie URL. Tylko gdy form jest nieruszony (pusty/initial). */
  private readonly _syncDateFromQuery = effect(() => {
    const qd = this.queryDate();
    if (!qd || !/^\d{4}-\d{2}-\d{2}$/.test(qd)) return;
    if (this.leaveModel().startDate === qd && this.leaveModel().endDate === qd) return;
    this.leaveModel.update((m) => ({ ...m, startDate: qd, endDate: qd }));
  });

  /** Sync z `initialDate` (tryb osadzony/drawer) — input dociera po konstruktorze, więc obsługujemy efektem. */
  private readonly _syncInitialDate = effect(() => {
    const d = this.initialDate();
    if (!d || !/^\d{4}-\d{2}-\d{2}$/.test(d)) return;
    if (this.leaveModel().startDate === d && this.leaveModel().endDate === d) return;
    this.leaveModel.update((m) => ({ ...m, startDate: d, endDate: d }));
  });

  /** Auto-fill endDate = startDate gdy user zmienia start (a end jeszcze nieustawione lub wcześniejsze). */
  private readonly _autoEnd = effect(() => {
    const m = this.leaveModel();
    if (m.startDate && (!m.endDate || m.endDate < m.startDate)) {
      this.leaveModel.update((prev) => ({ ...prev, endDate: prev.startDate }));
    }
  });

  /** Initial model — preferuje datę z `?date=YYYY-MM-DD` (np. z modalu kalendarza), inaczej dziś. */
  private buildInitialLeaveModel(): EmployeeLeaveFormModel {
    const queryDate = this.route.snapshot.queryParamMap.get('date');
    const initial = queryDate && /^\d{4}-\d{2}-\d{2}$/.test(queryDate) ? queryDate : todayYyyyMmDd();
    return {
      startDate: initial,
      endDate: initial,
      absenceType: String(AbsenceType.Vacation),
    };
  }

  applyQuickRange(kind: 'today' | 'tomorrow' | 'nextWeek'): void {
    if (kind === 'today') {
      const d = todayYyyyMmDd();
      this.leaveModel.update((m) => ({ ...m, startDate: d, endDate: d }));
    } else if (kind === 'tomorrow') {
      const d = addDaysYyyyMmDd(1);
      this.leaveModel.update((m) => ({ ...m, startDate: d, endDate: d }));
    } else {
      const { start, end } = nextWeekRange();
      this.leaveModel.update((m) => ({ ...m, startDate: start, endDate: end }));
    }
  }

  leaveForm = form(this.leaveModel, (schemaPath) => {
    required(schemaPath.startDate, { message: 'Wybierz datę rozpoczęcia' });
    required(schemaPath.endDate, { message: 'Wybierz datę zakończenia' });
    required(schemaPath.absenceType, { message: 'Wybierz typ wpisu' });
    validate(schemaPath.endDate, (ctx) => {
      const start = ctx.valueOf(schemaPath.startDate);
      const end = ctx.value();
      if (!start || !end) {
        return [];
      }
      if (start > end) {
        return [
          {
            kind: 'leaveRange',
            message: 'Data zakończenia nie może być wcześniejsza niż początek'
          }
        ];
      }
      return [];
    });
  });

  protected FormActions = createFormActions<EmployeeLeaveFormModel, unknown>(
    {
      create: (data) =>
        this.employeesClient.addEmployeeLeave(this.id(), {
          startDate: data.startDate as unknown as Date,
          endDate: data.endDate as unknown as Date,
          absenceType: this.parseAbsenceType(data.absenceType),
        }).pipe(tap(result => this.warnAboutAppointmentsInLeave(result.appointmentsInLeave?.length ?? 0))),
      update: () => EMPTY,
      delete: () => EMPTY,
      get: () => EMPTY
    },
    this.leaveForm as any,
    this.leaveModel,
    () => undefined,
    {
      successMessage: 'Wpis został zapisany.',
      // Callbacki rozgałęziają się w czasie wywołania (input `embedded` dociera po field-init).
      // Tryb osadzony (drawer): emit zamiast nawigacji. Tryb strony: nawigacja do leave-dashboard.
      onSuccess: () => {
        if (this.embedded()) { this.saved.emit(); return; }
        this.router.navigate(this.redirectCommands());
      },
      onCancel: () => {
        if (this.embedded()) { this.cancelled.emit(); return; }
        this.router.navigate(this.redirectCommands());
      },
    }
  );

  private redirectCommands(): string[] {
    return this.isSelfMode()
      ? ['/admin', 'my-availability', this.id(), 'leave-dashboard']
      : ['/admin', 'resources', 'employees', this.id(), 'leave-dashboard'];
  }

  /** Wywoływane przez stopkę drawera (tryb osadzony). */
  submit(): void {
    this.FormActions.handleSubmit();
  }

  /** Spinner stopki drawera w trakcie zapisu. */
  submitting = computed(() => this.FormActions.isPending());

  private parseAbsenceType(raw: string | undefined): AbsenceType {
    const n = Number(raw);
    if (n === AbsenceType.Vacation) return AbsenceType.Vacation;
    if (n === AbsenceType.SickLeave) return AbsenceType.SickLeave;
    if (n === AbsenceType.SpecialDay) return AbsenceType.SpecialDay;
    return AbsenceType.Vacation;
  }

  private warnAboutAppointmentsInLeave(count: number) {
    if (count <= 0) return;
    this.messageService.add({
      severity: 'warn',
      summary: 'Wizyty w trakcie urlopu',
      detail: `${count} ${plWizyt(count)} pozostaje w kalendarzu w zakresie tego urlopu — przejrzyj je w kalendarzu pracownika.`,
      life: 10000,
    });
  }
}

function plWizyt(n: number): string {
  if (n === 1) return 'wizyta';
  const lastTwo = n % 100;
  const last = n % 10;
  if (lastTwo >= 12 && lastTwo <= 14) return 'wizyt';
  if (last >= 2 && last <= 4) return 'wizyty';
  return 'wizyt';
}
