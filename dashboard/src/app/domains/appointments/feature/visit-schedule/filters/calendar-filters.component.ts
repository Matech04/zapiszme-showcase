import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  Renderer2,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { Drawer } from 'primeng/drawer';
import { MultiSelect } from 'primeng/multiselect';
import {
  APPOINTMENT_STATUS_TOKENS,
  AppointmentStatusVariant,
} from '@core/theme/status-tokens';
import { BodyScrollLockService } from '@core/services/body-scroll-lock.service';

export type CalendarViewMode = 'day' | 'week' | 'month';

export interface CalendarFiltersValue {
  employeeIds: string[];
  statuses: AppointmentStatusVariant[];
  searchQuery: string;
}

interface EmployeeOpt {
  id: string;
  label: string;
}

@Component({
  selector: 'app-calendar-filters',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, Drawer, MultiSelect],
  template: `
    @if (isDesktop()) {
      <!-- ============================== Desktop bar — bez zmian ============================== -->
      <div class="admin-glass-card rounded-3xl p-3 flex flex-wrap items-center gap-2">
        <ng-container *ngTemplateOutlet="viewToggle" />
        <ng-container *ngTemplateOutlet="searchInput" />

        @if (showEmployeeFilter() && employees().length > 1) {
          <p-multiSelect
            [options]="employees()"
            optionLabel="label"
            optionValue="id"
            [ngModel]="value().employeeIds"
            (ngModelChange)="emit({ employeeIds: $event })"
            placeholder="Wszyscy"
            display="chip"
            [filter]="employees().length > 6"
            styleClass="text-xs !rounded-xl"
            [maxSelectedLabels]="2"
            selectedItemsLabel="{0} pracowników"
          />
        }

        <div class="flex flex-wrap gap-1.5">
          @for (s of statusOptions; track s.id) {
            <button
              type="button"
              (click)="toggleStatus(s.id)"
              class="inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-[11px] font-bold uppercase tracking-wider transition-colors"
              [class]="statusButtonClasses(s.id)"
            >
              <i [class]="s.icon" class="text-[10px]"></i>
              {{ s.label }}
            </button>
          }
        </div>

        @if (hasActiveFilters()) {
          <button
            type="button"
            (click)="clearAll()"
            class="text-xs font-bold uppercase tracking-wider text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 px-2"
          >
            Wyczyść
          </button>
        }
      </div>
    } @else {
      <!-- ============================== Mobile compact bar + sheet ============================== -->
      <div class="admin-glass-card rounded-3xl p-2.5 flex flex-col gap-2">
        <!-- Układ stały we wszystkich widokach: przełącznik widoku po LEWEJ, akcje (Wyczyść / szukaj /
             filtry) zgrupowane po PRAWEJ (ml-auto). Dzień ukrywa tylko szukajkę; tydzień i miesiąc
             wyglądają identycznie. -->
        <div class="flex items-center gap-2">
          <ng-container *ngTemplateOutlet="viewToggle" />
          <div class="ml-auto flex items-center gap-2">
            @if (hasActiveFilters()) {
              <button
                type="button"
                (click)="clearAll()"
                class="shrink-0 text-[11px] font-bold uppercase tracking-wider text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 px-1"
                aria-label="Wyczyść wszystkie filtry"
              >
                Wyczyść
              </button>
            }
            <!-- Filtry: sama ikona (bez napisu), na prawym krańcu paska. Licznik jako plakietka w rogu. -->
            <button
              type="button"
              (click)="openSheet()"
              class="shrink-0 relative inline-flex items-center justify-center rounded-xl border min-h-10 w-10 transition-colors"
              [class.border-primary]="activeCount() > 0"
              [class.text-primary]="activeCount() > 0"
              [class.bg-primary/10]="activeCount() > 0"
              [class.border-surface-300]="activeCount() === 0"
              [class.dark:border-surface-600]="activeCount() === 0"
              [class.text-surface-700]="activeCount() === 0"
              [class.dark:text-surface-300]="activeCount() === 0"
              [attr.aria-label]="
                activeCount() > 0 ? activeCount() + ' aktywnych filtrów — otwórz' : 'Otwórz filtry'
              "
            >
              <i class="pi pi-sliders-h text-sm" aria-hidden="true"></i>
              @if (activeCount() > 0) {
                <span
                  class="absolute -top-1 -right-1 inline-flex items-center justify-center min-w-[1.1rem] h-[1.1rem] px-1 rounded-full bg-primary text-white text-[10px] font-bold"
                >{{ activeCount() }}</span>
              }
            </button>
          </div>
        </div>
      </div>

      <p-drawer
        [visible]="sheetOpen()"
        (visibleChange)="onSheetVisibleChange($event)"
        position="bottom"
        [modal]="false"
        [showCloseIcon]="false"
        [dismissible]="false"
        [styleClass]="
          'admin-drawer-shell !bg-surface-0 dark:!bg-surface-50 !border-t-0 !h-auto !max-h-[85vh] !rounded-t-3xl shadow-2xl'
        "
      >
        <header
          class="px-5 pt-5 pb-3 border-b border-surface-200/70 dark:border-surface-200/70"
        >
          <div class="flex justify-center pb-2 -mt-2" aria-hidden="true">
            <span class="block w-12 h-1.5 rounded-full bg-surface-300 dark:bg-surface-600"></span>
          </div>
          <div class="flex items-center justify-between gap-3">
            <div class="min-w-0">
              <p class="admin-section-label text-primary">Filtry</p>
              <h2 class="text-lg font-black text-surface-900">
                Zawęź wizyty
              </h2>
            </div>
            <button
              type="button"
              (click)="closeSheet()"
              aria-label="Zamknij"
              class="w-8 h-8 rounded-full grid place-items-center text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
            >
              <i class="pi pi-times text-sm" aria-hidden="true"></i>
            </button>
          </div>
        </header>

        <section class="px-5 py-4 flex-1 flex flex-col gap-5 overflow-y-auto">
          @if (showEmployeeFilter() && employees().length > 1) {
            <div class="flex flex-col gap-2">
              <label class="text-xs font-bold uppercase tracking-wider text-surface-500">
                Pracownik
              </label>
              <p-multiSelect
                [options]="employees()"
                optionLabel="label"
                optionValue="id"
                [ngModel]="value().employeeIds"
                (ngModelChange)="emit({ employeeIds: $event })"
                placeholder="Wszyscy"
                display="chip"
                [filter]="employees().length > 6"
                styleClass="w-full text-xs !rounded-xl"
                [maxSelectedLabels]="3"
                selectedItemsLabel="{0} pracowników"
              />
            </div>
          }

          <div class="flex flex-col gap-2">
            <label class="text-xs font-bold uppercase tracking-wider text-surface-500">
              Status
            </label>
            <div class="flex flex-wrap gap-2">
              @for (s of statusOptions; track s.id) {
                <button
                  type="button"
                  (click)="toggleStatus(s.id)"
                  class="inline-flex items-center gap-1.5 rounded-full border min-h-10 px-3.5 py-2 text-xs font-bold uppercase tracking-wider transition-colors"
                  [class]="statusButtonClasses(s.id)"
                >
                  <i [class]="s.icon" class="text-[11px]"></i>
                  {{ s.label }}
                </button>
              }
            </div>
          </div>
        </section>

        <footer
          class="px-5 py-4 border-t border-surface-200/70 dark:border-surface-200/70 flex flex-col gap-2 pb-[calc(env(safe-area-inset-bottom)+1rem)]"
        >
          <button
            type="button"
            [disabled]="!hasActiveFilters()"
            (click)="clearAll(); closeSheet()"
            class="w-full rounded-xl border border-surface-300 dark:border-surface-600 hover:border-primary/45 font-bold py-2.5 px-4 text-surface-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Wyczyść wszystkie
          </button>
          <button
            type="button"
            (click)="closeSheet()"
            class="w-full rounded-xl bg-surface-900 dark:bg-surface-100 text-surface-0 dark:text-surface-900 font-bold py-2.5 px-4 hover:opacity-90 transition-opacity"
          >
            Gotowe
          </button>
        </footer>
      </p-drawer>
    }

    <!-- ============================== Współdzielone fragmenty ============================== -->
    <ng-template #viewToggle>
      <div
        class="inline-flex p-1 rounded-2xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45"
        role="group"
        aria-label="Tryb kalendarza"
      >
        @for (m of viewModes; track m.id) {
          <button
            type="button"
            (click)="onViewMode(m.id)"
            [attr.aria-pressed]="viewMode() === m.id"
            [class]="viewButtonClasses(m.id)"
            class="min-h-10 px-3.5 py-2 text-xs font-bold uppercase tracking-wider rounded-xl transition-colors"
          >
            {{ m.label }}
          </button>
        }
      </div>
    </ng-template>

    <ng-template #searchInput>
      <div class="relative flex-1 min-w-[10rem]">
        <i
          class="pi pi-search text-xs text-surface-400 absolute left-3 top-1/2 -translate-y-1/2"
          aria-hidden="true"
        ></i>
        <input
          type="search"
          [ngModel]="searchInputModel()"
          (ngModelChange)="onSearchInput($event)"
          placeholder="Szukaj klienta po nazwisku lub telefonie…"
          class="w-full pl-9 pr-3 py-2 rounded-xl border border-surface-200/70 dark:border-surface-200/70 bg-white/65 dark:bg-surface-50/45 text-sm text-surface-800 placeholder:text-surface-400 focus:outline-none focus:border-primary/55 transition-colors"
        />
      </div>
    </ng-template>
  `,
})
export class CalendarFiltersComponent {
  readonly viewMode = input.required<CalendarViewMode>();
  readonly value = input.required<CalendarFiltersValue>();
  readonly employees = input.required<EmployeeOpt[]>();
  readonly showEmployeeFilter = input<boolean>(true);
  /** Decyduje który układ pokazać — kompaktowy mobilny z sheet'em vs pełny desktopowy. */
  readonly isDesktop = input<boolean>(false);

  readonly viewModeChange = output<CalendarViewMode>();
  readonly valueChange = output<CalendarFiltersValue>();

  private readonly renderer = inject(Renderer2);
  private readonly destroyRef = inject(DestroyRef);
  private readonly scrollLock = inject(BodyScrollLockService);

  protected readonly viewModes: { id: CalendarViewMode; label: string }[] = [
    { id: 'day', label: 'Dzień' },
    { id: 'week', label: 'Tydzień' },
    { id: 'month', label: 'Miesiąc' },
  ];

  protected readonly statusOptions = (
    ['pending', 'booked', 'inProgress', 'completed', 'canceled'] as AppointmentStatusVariant[]
  ).map((id) => ({
    id,
    label: APPOINTMENT_STATUS_TOKENS[id].label,
    icon: APPOINTMENT_STATUS_TOKENS[id].icon,
  }));

  readonly hasActiveFilters = computed(() => {
    const v = this.value();
    return v.employeeIds.length > 0 || v.statuses.length > 0 || v.searchQuery.trim() !== '';
  });

  /** Liczba aktywnych „grup" filtrów — wyświetlana w badge'u na przycisku „Filtry". */
  protected readonly activeCount = computed(() => {
    const v = this.value();
    return (
      (v.employeeIds.length > 0 ? 1 : 0) +
      (v.statuses.length > 0 ? 1 : 0) +
      (v.searchQuery.trim() !== '' ? 1 : 0)
    );
  });

  // ----------------- Sheet mobile -----------------

  protected readonly sheetOpen = signal(false);

  private clickOutsideUnsubscribe?: () => void;
  private bodyLocked = false;

  /**
   * Lokalny model search inputu — odzwierciedla każde naciśnięcie klawisza dla płynnego UI,
   * ale `valueChange` emitowane dopiero po 250ms debounce. Bez tego każdy znak powodował
   * pełen re-render osi czasu (filter + positioning), co dawało lagi na słabszych urządzeniach.
   */
  protected readonly searchInputModel = signal('');
  private readonly searchSubject = new Subject<string>();

  constructor() {
    // Synchronizacja: gdy parent zmieni `value().searchQuery` (np. clearAll), update local model.
    effect(() => {
      const external = this.value().searchQuery;
      if (this.searchInputModel() !== external) {
        this.searchInputModel.set(external);
      }
    });

    // Debounce emit do parent
    this.searchSubject
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((q) => this.emit({ searchQuery: q }));

    effect(() => {
      // Auto-close sheet'a gdy przełączymy się na desktop (np. resize) — pasek desktopowy
      // już ma wszystkie kontrolki inline, sheet trzymany w stanie wprowadzałby chaos.
      if (this.isDesktop() && this.sheetOpen()) {
        this.sheetOpen.set(false);
      }
    });

    effect(() => {
      if (this.sheetOpen()) {
        this.lockBodyScroll();
        const t = setTimeout(() => this.bindClickOutside(), 0);
        this.destroyRef.onDestroy(() => clearTimeout(t));
      } else {
        this.unbindClickOutside();
        this.unlockBodyScroll();
      }
    });

    this.destroyRef.onDestroy(() => {
      this.unbindClickOutside();
      this.unlockBodyScroll();
    });
  }

  protected openSheet(): void {
    this.sheetOpen.set(true);
  }

  protected closeSheet(): void {
    this.sheetOpen.set(false);
  }

  protected onSheetVisibleChange(next: boolean): void {
    if (!next && this.sheetOpen()) this.closeSheet();
  }

  private lockBodyScroll(): void {
    if (this.bodyLocked) return;
    this.bodyLocked = true;
    this.scrollLock.lock();
  }

  private unlockBodyScroll(): void {
    if (!this.bodyLocked) return;
    this.bodyLocked = false;
    this.scrollLock.unlock();
  }

  private bindClickOutside(): void {
    if (this.clickOutsideUnsubscribe) return;
    this.clickOutsideUnsubscribe = this.renderer.listen(
      'document',
      'pointerdown',
      (ev: Event) => {
        if (!this.sheetOpen()) return;
        const target = ev.target as HTMLElement | null;
        if (!target) return;
        // Sheet sam zawiera overlay'e dropdown'a multi-select — bez tej osłony klik
        // w listę PrimeNG zamykałby cały sheet.
        if (
          target.closest('.p-drawer') ||
          target.closest('.p-overlay') ||
          target.closest('.p-multiselect-panel') ||
          target.closest('.p-overlay-mask')
        ) {
          return;
        }
        this.closeSheet();
      },
    );
  }

  private unbindClickOutside(): void {
    this.clickOutsideUnsubscribe?.();
    this.clickOutsideUnsubscribe = undefined;
  }

  // ----------------- Akcje filtrów -----------------

  emit(patch: Partial<CalendarFiltersValue>): void {
    this.valueChange.emit({ ...this.value(), ...patch });
  }

  /** Visual update natychmiast, emit do parent debounced — patrz `searchSubject` w konstruktorze. */
  protected onSearchInput(value: string): void {
    this.searchInputModel.set(value ?? '');
    this.searchSubject.next(value ?? '');
  }

  onViewMode(m: CalendarViewMode): void {
    this.viewModeChange.emit(m);
  }

  toggleStatus(id: AppointmentStatusVariant): void {
    const current = new Set(this.value().statuses);
    if (current.has(id)) current.delete(id);
    else current.add(id);
    this.emit({ statuses: Array.from(current) });
  }

  statusButtonClasses(id: AppointmentStatusVariant): string {
    const isActive = this.value().statuses.includes(id);
    const baseTokens = APPOINTMENT_STATUS_TOKENS[id];
    if (isActive) {
      return `${baseTokens.surfaceClasses} ${baseTokens.textClasses}`;
    }
    return 'bg-white/60 dark:bg-surface-50/45 border-surface-200/70 dark:border-surface-200/70 text-surface-500 dark:text-surface-400 hover:border-primary/45';
  }

  /**
   * Aktywny tryb kalendarza wyróżniamy akcentem marki (primary = amber w light, fiolet w dark),
   * nie samym białym tłem — na jasnym pasku „biały na białym" był praktycznie niewidoczny.
   */
  viewButtonClasses(id: CalendarViewMode): string {
    return this.viewMode() === id
      ? 'bg-primary text-primary-contrast shadow-sm'
      : 'text-surface-600 dark:text-surface-400 hover:text-surface-900 hover:bg-surface-100/70 dark:hover:bg-surface-100/10';
  }

  clearAll(): void {
    this.valueChange.emit({ employeeIds: [], statuses: [], searchQuery: '' });
  }
}
