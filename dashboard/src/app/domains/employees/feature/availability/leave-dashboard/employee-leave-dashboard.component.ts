import { Component, computed, inject, input, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Button } from 'primeng/button';
import { AbsenceStatus, AbsenceType, EmployeesClient, EmployeeLeaveDto } from '@core/api/api-client';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { safeBackWith } from '@core/navigation/safe-back';

type LeaveStatus = 'upcoming' | 'active' | 'past';

interface LeaveRow {
  leave: EmployeeLeaveDto;
  status: LeaveStatus;
  durationDays: number;
}

@Component({
  selector: 'app-employee-leave-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, Button],
  template: `
  <div class="min-h-full bg-surface-50 dark:bg-surface-950">
    <div class="max-w-3xl mx-auto px-4 sm:px-6 py-8 sm:py-10">
      <nav class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1">
        <a [routerLink]="hubLink()" class="hover:text-primary transition-colors">{{ hubLabel() }}</a>
        <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
        <span class="text-surface-700 dark:text-surface-300">Urlopy i chorobowe</span>
      </nav>

      <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 mb-8">
        <div>
          <h1 class="text-2xl sm:text-3xl font-bold text-surface-900 tracking-tight mb-2">
            Urlopy i chorobowe
          </h1>
          <p class="text-surface-600 dark:text-surface-400 text-sm sm:text-base max-w-xl leading-relaxed">
            Kilkudniowe nieobecności wykluczające rezerwacje w wybranym okresie. Aby zmienić godziny w pojedynczym dniu, użyj
            <strong>Ustaw godziny na ten dzień</strong>.
          </p>
        </div>

        <div class="flex flex-col sm:flex-row gap-2 sm:gap-3 shrink-0 w-full sm:w-auto">
          <p-button
            label="Wróć"
            severity="secondary"
            [outlined]="true"
            styleClass="w-full sm:w-auto"
            [disabled]="leavesData.isLoading()"
            (onClick)="goBack()"
          />
          <p-button
            label="Grafiki"
            icon="pi pi-calendar"
            severity="secondary"
            styleClass="w-full sm:w-auto"
            (onClick)="goToWeeklySchedule()"
          />
          <p-button
            data-tour="leave-add"
            label="Dodaj urlop"
            icon="pi pi-plus"
            styleClass="w-full sm:w-auto font-semibold"
            [disabled]="leavesData.isLoading()"
            (onClick)="goToAddLeave()"
          />
        </div>
      </div>

      <div class="rounded-2xl border border-surface-200/80 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-5 shadow-sm mb-6 sm:mb-8">
        <div class="flex items-center gap-4">
          <div
            class="shrink-0 w-14 h-14 rounded-xl border-2 border-primary/30 bg-primary/5 flex items-center justify-center text-lg font-bold text-primary"
            aria-hidden="true"
          >
            {{ employeeInitials() }}
          </div>
          <div class="min-w-0 flex-1">
            <p class="font-bold text-surface-900 text-base sm:text-lg truncate">
              {{ employeeDisplayName() }}
            </p>
            <p class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 truncate">
              {{ employeeSubtitle() }}
            </p>
          </div>
        </div>
      </div>

      @if (isSelfMode()) {
        @if (totalCount() > 0) {
          <div class="rounded-xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm mb-6 sm:mb-8 flex items-center justify-between gap-4">
            <div>
              <p class="text-[10px] font-bold uppercase tracking-wider text-surface-500 mb-1">Zaplanowane wolne dni</p>
              <p class="text-2xl font-bold text-surface-900">
                {{ upcomingPlusActiveDays() }}
              </p>
            </div>
            <p class="text-xs text-surface-500 dark:text-surface-400 max-w-[18ch] text-right leading-snug hidden sm:block">
              Łączna liczba dni nieobecności w przyszłości
            </p>
          </div>
        }
      } @else {
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-3 sm:gap-4 mb-8">
          <div class="rounded-xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm">
            <p class="text-[10px] font-bold uppercase tracking-wider text-surface-500 mb-1">Wpisy</p>
            <p class="text-2xl font-bold text-surface-900">{{ totalCount() }}</p>
          </div>
          <div class="rounded-xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm">
            <p class="text-[10px] font-bold uppercase tracking-wider text-surface-500 mb-1">Nadchodzące</p>
            <p class="text-2xl font-bold text-primary">{{ upcomingCount() }}</p>
          </div>
          <div class="rounded-xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm">
            <p class="text-[10px] font-bold uppercase tracking-wider text-surface-500 mb-1">Aktywne / trwające</p>
            <p class="text-2xl font-bold text-surface-900">{{ activeCount() }}</p>
          </div>
        </div>
      }

      @if (leavesData.isLoading()) {
        <div class="rounded-2xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-12 text-center text-surface-500">
          Wczytywanie…
        </div>
      } @else if (leaveRows().length === 0) {
        <div class="rounded-2xl border-2 border-dashed border-surface-200 dark:border-surface-100 p-12 text-center bg-surface-0 dark:bg-surface-50/60">
          <div class="w-16 h-16 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mx-auto mb-4">
            <i class="pi pi-calendar-plus text-2xl text-surface-400"></i>
          </div>
          <h3 class="text-lg font-bold text-surface-900 mb-2">Brak zaplanowanych nieobecności</h3>
          <p class="text-surface-600 dark:text-surface-400 text-sm max-w-sm mx-auto mb-6">
            Dodaj pierwszy urlop lub chorobowe, aby zablokować rezerwacje w wybranym okresie.
          </p>
          <p-button
            data-tour="leave-add"
            label="Dodaj urlop"
            icon="pi pi-plus"
            (onClick)="goToAddLeave()"
          />
        </div>
      } @else {
        <div class="flex flex-col gap-3 sm:gap-4">
          @for (row of leaveRows(); track row.leave.id) {
            <div
              class="group rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm transition-[box-shadow,border-color] hover:shadow-md dark:hover:border-surface-700"
            >
              <div class="space-y-2">
                <div class="flex items-center justify-between gap-3 min-h-9">
                  <div class="flex flex-wrap items-center gap-x-2 gap-y-1 min-w-0">
                    <span
                      class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-md border shrink-0"
                      [ngClass]="absenceTypeBadgeClass(row.leave.absenceType)"
                    >
                      {{ absenceTypeLabel(row.leave.absenceType) }}
                    </span>
                    @if (!isSelfMode()) {
                      <span
                        class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-md border shrink-0"
                        [ngClass]="absenceStatusBadgeClass(row.leave.absenceStatus)"
                      >
                        {{ absenceStatusLabel(row.leave.absenceStatus) }}
                      </span>
                    }
                    <span
                      class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-md border shrink-0"
                      [ngClass]="statusBadgeClass(row.status)"
                    >
                      {{ statusLabel(row.status) }}
                    </span>
                    <span class="text-sm text-surface-500 dark:text-surface-400">
                      {{ row.durationDays }} {{ row.durationDays === 1 ? 'dzień' : 'dni' }}
                    </span>
                  </div>
                  @if (row.status === 'upcoming') {
                    <p-button
                      icon="pi pi-trash"
                      [rounded]="true"
                      [text]="true"
                      severity="danger"
                      [disabled]="removingLeaveId() !== null"
                      [loading]="removingLeaveId() === row.leave.id"
                      styleClass="shrink-0 !w-9 !h-9 !p-0 opacity-90 group-hover:opacity-100"
                      (onClick)="confirmRemoveLeave(row)"
                      [attr.aria-label]="'Usuń urlop od ' + formatRange(row.leave)"
                    />
                  }
                </div>
                <p class="text-base sm:text-lg font-semibold text-surface-900 leading-snug tracking-tight">
                  {{ formatRange(row.leave) }}
                </p>
              </div>
            </div>
          }
        </div>
      }
    </div>
  </div>
  `
})
export class EmployeeLeaveDashboardComponent {
  private employeesService = inject(EmployeesClient);
  private location = inject(Location);
  private router = inject(Router);
  private confirmationService = inject(ConfirmationService);
  private messageService = inject(MessageService);

  id = input.required<string>();
  protected isSelfMode = computed(() => this.router.url.startsWith('/admin/my-availability/'));
  protected hubLabel = computed(() => this.isSelfMode() ? 'Moja dostępność' : 'Zarządzanie');
  protected hubLink = computed(() =>
    this.isSelfMode() ? ['/admin/my-availability', this.id()] : '/admin/resources');

  /** Id urlopu w trakcie usuwania (blokuje pozostałe przyciski). */
  removingLeaveId = signal<string | null>(null);

  employeeData = rxResource({
    stream: () => this.employeesService.getEmployee(this.id())
  });

  leavesData = rxResource({
    stream: () => this.employeesService.getEmployeeLeaves(this.id()),
    defaultValue: [] as EmployeeLeaveDto[]
  });

  employeeDisplayName = computed(() => {
    const e = this.employeeData.value();
    if (!e) return 'Pracownik';
    const parts = [e.firstName, e.lastName].filter(Boolean);
    return parts.length ? parts.join(' ') : 'Pracownik';
  });

  employeeSubtitle = computed(() => {
    const e = this.employeeData.value();
    if (!e?.email) return 'Kalendarz urlopów';
    return e.email;
  });

  employeeInitials = computed(() => {
    const e = this.employeeData.value();
    if (!e) return '?';
    const a = (e.firstName?.trim()?.[0] ?? '').toUpperCase();
    const b = (e.lastName?.trim()?.[0] ?? '').toUpperCase();
    const initials = `${a}${b}`.trim();
    return initials || '?';
  });

  leaveRows = computed((): LeaveRow[] => {
    const list = this.leavesData.value() ?? [];
    const today = this.startOfDay(new Date());

    const rows: LeaveRow[] = list.map((leave) => {
      const start = this.coerceDate(leave.startDate);
      const end = this.coerceDate(leave.endDate);
      if (!start || !end) {
        return { leave, status: 'past' as const, durationDays: 0 };
      }
      const s = this.startOfDay(start);
      const e = this.startOfDay(end);
      const durationDays = Math.round((e.getTime() - s.getTime()) / 86400000) + 1;

      let status: LeaveStatus;
      if (today.getTime() > e.getTime()) {
        status = 'past';
      } else if (today.getTime() < s.getTime()) {
        status = 'upcoming';
      } else {
        status = 'active';
      }

      return { leave, status, durationDays };
    });

    return rows.sort((a, b) => {
      const da = this.coerceDate(a.leave.startDate)?.getTime() ?? 0;
      const db = this.coerceDate(b.leave.startDate)?.getTime() ?? 0;
      return da - db;
    });
  });

  totalCount = computed(() => this.leaveRows().length);

  upcomingCount = computed(() => this.leaveRows().filter((r) => r.status === 'upcoming').length);

  activeCount = computed(() => this.leaveRows().filter((r) => r.status === 'active').length);

  /** Suma dni kalendarzowych dla wpisów nadchodzących + aktualnie trwających (SOLO widok skrócony). */
  upcomingPlusActiveDays = computed(() =>
    this.leaveRows()
      .filter((r) => r.status === 'upcoming' || r.status === 'active')
      .reduce((sum, r) => sum + r.durationDays, 0),
  );

  goBack() {
    safeBackWith(this.location, this.router, this.hubLink() as string | string[]);
  }

  goToWeeklySchedule() {
    this.router.navigate([
      this.isSelfMode() ? '/admin/my-availability' : '/admin/resources/employees',
      this.id(),
      'schedules',
    ]);
  }

  goToAddLeave() {
    this.router.navigate([
      this.isSelfMode() ? '/admin/my-availability' : '/admin/resources/employees',
      this.id(),
      'leaves',
      'new',
    ]);
  }

  confirmRemoveLeave(row: LeaveRow) {
    const leaveId = row.leave.id;
    if (!leaveId || row.status !== 'upcoming') return;

    this.confirmationService.confirm({
      message: 'Czy na pewno chcesz usunąć ten zaplanowany urlop?',
      header: 'Usuwanie urlopu',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Tak',
      rejectLabel: 'Nie',
      accept: () => {
        this.removingLeaveId.set(leaveId);
        this.employeesService.removeEmployeeLeave(this.id(), leaveId).subscribe({
          next: () => {
            this.removingLeaveId.set(null);
            this.messageService.add({
              severity: 'success',
              summary: 'Usunięto',
              detail: 'Zaplanowany urlop został usunięty.',
              life: 4000
            });
            this.leavesData.reload();
          },
          error: () => {
            this.removingLeaveId.set(null);
            this.messageService.add({
              severity: 'error',
              summary: 'Błąd',
              detail: 'Nie udało się usunąć urlopu.',
              life: 5000
            });
          }
        });
      }
    });
  }

  formatRange(leave: EmployeeLeaveDto): string {
    const start = this.coerceDate(leave.startDate);
    const end = this.coerceDate(leave.endDate);
    if (!start || !end) return '—';
    const opts: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' };
    return `${start.toLocaleDateString('pl-PL', opts)} — ${end.toLocaleDateString('pl-PL', opts)}`;
  }

  absenceTypeLabel(type: AbsenceType | undefined): string {
    switch (type) {
      case AbsenceType.Vacation:
        return 'Urlop';
      case AbsenceType.SickLeave:
        return 'Chorobowe';
      case AbsenceType.SpecialDay:
        return 'Dzień specjalny';
      default:
        return 'Urlop';
    }
  }

  absenceTypeBadgeClass(type: AbsenceType | undefined): string {
    switch (type) {
      case AbsenceType.SickLeave:
        return 'bg-rose-500/12 text-rose-800 dark:text-rose-200 border-rose-500/35';
      case AbsenceType.SpecialDay:
        return 'bg-violet-500/12 text-violet-800 dark:text-violet-200 border-violet-500/35';
      case AbsenceType.Vacation:
      default:
        return 'bg-amber-500/12 text-amber-800 dark:text-amber-200 border-amber-500/35';
    }
  }

  absenceStatusLabel(status: AbsenceStatus | undefined): string {
    switch (status) {
      case AbsenceStatus.Approved:
        return 'Zatwierdzone';
      case AbsenceStatus.Pending:
        return 'Oczekuje';
      case AbsenceStatus.Rejected:
        return 'Odrzucone';
      default:
        return 'Zatwierdzone';
    }
  }

  absenceStatusBadgeClass(status: AbsenceStatus | undefined): string {
    switch (status) {
      case AbsenceStatus.Pending:
        return 'bg-sky-500/12 text-sky-800 dark:text-sky-200 border-sky-500/35';
      case AbsenceStatus.Rejected:
        return 'bg-rose-500/12 text-rose-800 dark:text-rose-200 border-rose-500/35 line-through';
      case AbsenceStatus.Approved:
      default:
        return 'bg-emerald-500/12 text-emerald-800 dark:text-emerald-200 border-emerald-500/35';
    }
  }

  statusLabel(status: LeaveStatus): string {
    switch (status) {
      case 'upcoming':
        return 'Nadchodzący';
      case 'active':
        return 'Trwa';
      default:
        return 'Zakończony';
    }
  }

  statusBadgeClass(status: LeaveStatus): string {
    switch (status) {
      case 'upcoming':
        return 'bg-amber-500/12 text-amber-800 dark:text-amber-200 border-amber-500/35';
      case 'active':
        return 'bg-amber-500/15 text-amber-800 dark:text-amber-200 border-amber-500/30';
      default:
        return 'bg-surface-100 dark:bg-surface-100 text-surface-600 dark:text-surface-400 border-surface-200 dark:border-surface-200';
    }
  }

  private coerceDate(value: Date | string | undefined): Date | null {
    if (value == null) return null;
    if (value instanceof Date) {
      return isNaN(value.getTime()) ? null : value;
    }
    const d = new Date(value);
    return isNaN(d.getTime()) ? null : d;
  }

  private startOfDay(d: Date): Date {
    const x = new Date(d);
    x.setHours(0, 0, 0, 0);
    return x;
  }
}
