import { Component, computed, inject, input, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { rxResource } from '@angular/core/rxjs-interop';
import { EmployeeScheduleDto, EmployeesClient } from '@core/api/api-client';
import { buildStandardWeekScheduleDto } from '@core/api/quick-start-schedule';
import { safeBackWith } from '@core/navigation/safe-back';

interface ScheduleRow {
  schedule: EmployeeScheduleDto;
  status: 'upcoming' | 'active' | 'past' | 'inactive' | 'unknown';
  durationDays: number;
}

@Component({
  selector: 'app-employee-schedules-list',
  standalone: true,
  imports: [CommonModule, RouterLink, Button],
  template: `
    <div class="min-h-full bg-surface-50 dark:bg-surface-950">
      <div class="max-w-3xl mx-auto px-4 sm:px-6 py-8 sm:py-10">
        <nav
          class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1"
        >
          <a [routerLink]="hubLink()" class="hover:text-primary transition-colors">{{ hubLabel() }}</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <a [routerLink]="availabilityLink()" class="hover:text-primary transition-colors">Dostępność</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <span class="text-surface-700 dark:text-surface-300">Grafiki powtarzalne</span>
        </nav>

        <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 mb-8">
          <div>
            <h1
              class="text-2xl sm:text-3xl font-bold text-surface-900 tracking-tight mb-2"
            >
              Grafiki powtarzalne
            </h1>
            <p class="text-surface-600 dark:text-surface-400 text-sm sm:text-base max-w-xl leading-relaxed">
              Twoje godziny pracy powtarzające się co tydzień — zwykle wystarczy jeden grafik.
              Nowy dodasz tylko wtedy, gdy godziny mają się zmienić od konkretnego dnia (np. inne od nowego miesiąca).
            </p>
          </div>

          <div class="flex flex-col sm:flex-row gap-2 sm:gap-3 shrink-0 w-full sm:w-auto">
            <p-button
              label="Wróć"
              severity="secondary"
              [outlined]="true"
              styleClass="w-full sm:w-auto"
              [disabled]="schedulesData.isLoading()"
              (onClick)="goBack()"
            />
            <p-button
              data-tour="schedules-new"
              label="Nowy grafik powtarzalny"
              icon="pi pi-plus"
              styleClass="w-full sm:w-auto font-semibold"
              [disabled]="schedulesData.isLoading()"
              (onClick)="goToNew()"
            />
          </div>
        </div>

        @if (schedulesData.isLoading()) {
          <div
            class="rounded-2xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-12 text-center text-surface-500"
          >
            Wczytywanie…
          </div>
        } @else if (rows().length === 0) {
          <div
            class="rounded-2xl border-2 border-dashed border-surface-200 dark:border-surface-100 p-12 text-center bg-surface-0 dark:bg-surface-50/60"
          >
            <div class="w-16 h-16 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mx-auto mb-4">
              <i class="pi pi-calendar text-2xl text-surface-400"></i>
            </div>
            <h3 class="text-lg font-bold text-surface-900 mb-2">Brak grafików</h3>
            <p class="text-surface-600 dark:text-surface-400 text-sm max-w-sm mx-auto mb-6">
              Utwórz pierwszy grafik, aby rezerwacje uwzględniały godziny pracy pracownika.
            </p>
            <div class="flex flex-col sm:flex-row gap-2 justify-center items-stretch sm:items-center">
              <p-button
                label="Standardowy tydzień (9-17, pon-pt)"
                icon="pi pi-bolt"
                severity="warn"
                [loading]="quickStartPending()"
                (onClick)="onQuickStart()"
              />
              <span class="text-xs text-surface-400 self-center hidden sm:block">lub</span>
              <p-button
                data-tour="schedules-new"
                label="Utwórz ręcznie"
                icon="pi pi-plus"
                [outlined]="true"
                severity="secondary"
                [disabled]="quickStartPending()"
                (onClick)="goToNew()"
              />
            </div>
          </div>
        } @else {
          <div class="flex flex-col gap-3 sm:gap-4">
            @for (row of rows(); track row.schedule.id) {
              <div
                class="group rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm transition-[box-shadow,border-color] hover:shadow-md dark:hover:border-surface-700"
                [class.opacity-60]="row.status === 'inactive'"
              >
                <div class="space-y-2">
                  <div class="flex items-center justify-between gap-3 min-h-9">
                    <div class="flex flex-wrap items-center gap-x-2 gap-y-1 min-w-0">
                      <span
                        class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-md border shrink-0"
                        [ngClass]="statusBadgeClass(row.status)"
                      >
                        {{ statusLabel(row.status) }}
                      </span>
                      <span class="text-sm text-surface-500 dark:text-surface-400">
                        Cykl {{ row.schedule.numberOfCycles }} {{ row.schedule.numberOfCycles === 1 ? 'tydzień' : 'tygodnie' }}
                      </span>
                      @if (isIndefiniteRow(row)) {
                        <span class="text-sm text-surface-400 dark:text-surface-500">
                          · bezterminowo
                        </span>
                      } @else {
                        <span class="text-sm text-surface-400 dark:text-surface-500">
                          · {{ row.durationDays }} dni
                        </span>
                      }
                    </div>
                    <div class="flex items-center gap-2 shrink-0">
                      <p-button
                        [label]="row.status === 'inactive' ? 'Włącz' : 'Wyłącz'"
                        [icon]="row.status === 'inactive' ? 'pi pi-check-circle' : 'pi pi-ban'"
                        [severity]="row.status === 'inactive' ? 'success' : 'secondary'"
                        [outlined]="true"
                        styleClass="!text-sm"
                        [disabled]="togglingId() !== null"
                        [loading]="togglingId() === row.schedule.id"
                        (onClick)="toggleActive(row.schedule)"
                      />
                      <p-button
                        label="Edytuj"
                        icon="pi pi-pencil"
                        severity="secondary"
                        [outlined]="true"
                        styleClass="!text-sm"
                        (onClick)="goToEdit(row.schedule)"
                      />
                      <p-button
                        icon="pi pi-trash"
                        [rounded]="true"
                        [text]="true"
                        severity="danger"
                        [disabled]="removingId() !== null"
                        [loading]="removingId() === row.schedule.id"
                        styleClass="!w-9 !h-9 !p-0"
                        (onClick)="confirmRemove(row.schedule)"
                        [attr.aria-label]="'Usuń grafik'"
                      />
                    </div>
                  </div>
                  <p class="text-base sm:text-lg font-semibold text-surface-900 leading-snug tracking-tight">
                    {{ formatRange(row.schedule) }}
                  </p>
                </div>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
})
export class EmployeeSchedulesListComponent {
  private employeesService = inject(EmployeesClient);
  private location = inject(Location);
  private router = inject(Router);
  private confirmationService = inject(ConfirmationService);
  private messageService = inject(MessageService);

  id = input.required<string>();

  removingId = signal<string | null>(null);
  togglingId = signal<string | null>(null);
  quickStartPending = signal(false);

  private isSelfMode = computed(() => this.router.url.startsWith('/admin/my-availability/'));
  protected hubLabel = computed(() => (this.isSelfMode() ? 'Moja dostępność' : 'Zarządzanie'));
  protected hubLink = computed(() =>
    this.isSelfMode() ? ['/admin/my-availability', this.id()] : '/admin/resources',
  );
  protected availabilityLink = computed(() =>
    this.isSelfMode()
      ? ['/admin/my-availability', this.id()]
      : ['/admin/resources/employees', this.id(), 'availability'],
  );
  protected routeBase = computed(() =>
    this.isSelfMode() ? '/admin/my-availability' : '/admin/resources/employees',
  );

  schedulesData = rxResource({
    stream: () => this.employeesService.getEmployeeSchedules(this.id()),
    defaultValue: [] as EmployeeScheduleDto[],
  });

  rows = computed<ScheduleRow[]>(() => {
    const list = this.schedulesData.value() ?? [];
    const today = this.startOfDay(new Date());

    const withMeta: ScheduleRow[] = list.map((schedule) => {
      const start = this.coerceDate(schedule.activeFrom);
      const end = this.coerceDate(schedule.activeTo);
      if (!start || !end) {
        return { schedule, status: 'unknown' as const, durationDays: 0 };
      }
      const s = this.startOfDay(start);
      const e = this.startOfDay(end);
      const indefinite = this.isIndefinite(end);
      const durationDays = indefinite
        ? 0
        : Math.max(0, Math.round((e.getTime() - s.getTime()) / 86400000) + 1);

      let status: ScheduleRow['status'];
      if (schedule.isActive === false) status = 'inactive';
      else if (!indefinite && today.getTime() > e.getTime()) status = 'past';
      else if (today.getTime() < s.getTime()) status = 'upcoming';
      else status = 'active';

      return { schedule, status, durationDays };
    });

    return withMeta.sort((a, b) => {
      const da = this.coerceDate(a.schedule.activeFrom)?.getTime() ?? 0;
      const db = this.coerceDate(b.schedule.activeFrom)?.getTime() ?? 0;
      return da - db;
    });
  });

  goBack() {
    safeBackWith(this.location, this.router, this.availabilityLink() as string | string[]);
  }

  goToNew() {
    void this.router.navigate([this.routeBase(), this.id(), 'schedules', 'new']);
  }

  onQuickStart(): void {
    if (this.quickStartPending()) return;
    this.quickStartPending.set(true);
    this.employeesService.setEmployeeSchedule(this.id(), buildStandardWeekScheduleDto()).subscribe({
      next: () => {
        this.quickStartPending.set(false);
        this.schedulesData.reload();
        this.messageService.add({
          severity: 'success',
          summary: 'Grafik utworzony',
          detail: 'Standardowy tydzień 9–17 (pon–pt) jest aktywny. Klienci widzą Twoje wolne godziny.',
          life: 5000,
        });
      },
      error: () => {
        this.quickStartPending.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Nie udało się utworzyć grafiku',
          detail: 'Spróbuj ponownie lub utwórz grafik ręcznie.',
          life: 5000,
        });
      },
    });
  }

  goToEdit(schedule: EmployeeScheduleDto) {
    if (!schedule.id) return;
    void this.router.navigate([this.routeBase(), this.id(), 'schedules', schedule.id, 'edit']);
  }

  toggleActive(schedule: EmployeeScheduleDto) {
    if (!schedule.id || this.togglingId() !== null) return;
    const sid = schedule.id;
    const next = !(schedule.isActive ?? true);
    this.togglingId.set(sid);
    this.employeesService.setEmployeeScheduleActive(this.id(), sid, { isActive: next }).subscribe({
      next: () => {
        this.togglingId.set(null);
        this.messageService.add({
          severity: 'success',
          summary: next ? 'Grafik włączony' : 'Grafik wyłączony',
          detail: next
            ? 'Grafik jest aktywny i generuje wolne terminy.'
            : 'Grafik jest nieaktywny — nie generuje wolnych terminów.',
          life: 4000,
        });
        this.schedulesData.reload();
      },
      // Kolizję zakresów (schedule.schedules_collision) errorInterceptor pokaże po polsku.
      error: () => {
        this.togglingId.set(null);
      },
    });
  }

  confirmRemove(schedule: EmployeeScheduleDto) {
    if (!schedule.id) return;
    this.confirmationService.confirm({
      message: 'Czy na pewno chcesz usunąć ten grafik?',
      header: 'Usuwanie grafiku',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Tak',
      rejectLabel: 'Nie',
      accept: () => {
        const sid = schedule.id!;
        this.removingId.set(sid);
        this.employeesService.deleteEmployeeSchedule(this.id(), sid).subscribe({
          next: () => {
            this.removingId.set(null);
            this.messageService.add({
              severity: 'success',
              summary: 'Usunięto',
              detail: 'Grafik został usunięty.',
              life: 4000,
            });
            this.schedulesData.reload();
          },
          error: () => {
            this.removingId.set(null);
            this.messageService.add({
              severity: 'error',
              summary: 'Błąd',
              detail: 'Nie udało się usunąć grafiku.',
              life: 5000,
            });
          },
        });
      },
    });
  }

  formatRange(schedule: EmployeeScheduleDto): string {
    const start = this.coerceDate(schedule.activeFrom);
    const end = this.coerceDate(schedule.activeTo);
    if (!start || !end) return '—';
    const opts: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' };
    if (this.isIndefinite(end)) {
      return `${start.toLocaleDateString('pl-PL', opts)} — do odwołania`;
    }
    return `${start.toLocaleDateString('pl-PL', opts)} — ${end.toLocaleDateString('pl-PL', opts)}`;
  }

  protected isIndefiniteRow(row: ScheduleRow): boolean {
    const end = this.coerceDate(row.schedule.activeTo);
    return !!end && this.isIndefinite(end);
  }

  private isIndefinite(end: Date): boolean {
    return end.getFullYear() >= 9999;
  }

  statusLabel(status: ScheduleRow['status']): string {
    switch (status) {
      case 'upcoming':
        return 'Nadchodzący';
      case 'active':
        return 'Aktywny';
      case 'past':
        return 'Zakończony';
      case 'inactive':
        return 'Nieaktywny';
      default:
        return 'Brak danych';
    }
  }

  statusBadgeClass(status: ScheduleRow['status']): string {
    switch (status) {
      case 'active':
        return 'bg-emerald-500/12 text-emerald-800 dark:text-emerald-200 border-emerald-500/35';
      case 'upcoming':
        return 'bg-amber-500/12 text-amber-800 dark:text-amber-200 border-amber-500/35';
      case 'inactive':
        return 'bg-surface-100 dark:bg-surface-100 text-surface-500 dark:text-surface-400 border-surface-300 dark:border-surface-300';
      case 'past':
        return 'bg-surface-100 dark:bg-surface-100 text-surface-600 dark:text-surface-400 border-surface-200 dark:border-surface-200';
      default:
        return 'bg-surface-100 dark:bg-surface-100 text-surface-600 dark:text-surface-400 border-surface-200 dark:border-surface-200';
    }
  }

  private coerceDate(value: Date | string | undefined): Date | null {
    if (value == null) return null;
    if (value instanceof Date) {
      return Number.isNaN(value.getTime()) ? null : value;
    }
    const d = new Date(value);
    return Number.isNaN(d.getTime()) ? null : d;
  }

  private startOfDay(d: Date): Date {
    const x = new Date(d);
    x.setHours(0, 0, 0, 0);
    return x;
  }
}
