import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Dialog } from 'primeng/dialog';
import { CustomersClient, SearchCustomerDto } from '@core/api/api-client';
import { Subject, debounceTime, switchMap, of, catchError } from 'rxjs';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';

interface PaletteAction {
  id: string;
  label: string;
  description: string;
  icon: string;
  keywords: string[];
  run: () => void;
}

@Component({
  selector: 'app-command-palette',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, Dialog],
  template: `
    <p-dialog
      [(visible)]="visibleModel"
      [modal]="true"
      [closable]="true"
      [dismissableMask]="true"
      [showHeader]="false"
      styleClass="!w-[42rem] !max-w-[92vw] !rounded-3xl !shadow-2xl admin-glass-card !border-none"
      contentStyleClass="!p-0 !rounded-3xl"
    >
      <div class="flex flex-col">
        <header class="px-5 py-4 border-b border-surface-200/70 dark:border-surface-200/70 flex items-center gap-3">
          <i class="pi pi-search text-surface-500 dark:text-surface-400" aria-hidden="true"></i>
          <input
            type="text"
            [value]="query()"
            (input)="onQueryInput($event)"
            placeholder="Szukaj klientów, akcji…"
            class="flex-1 bg-transparent outline-none text-base text-surface-900 placeholder:text-surface-400"
            autofocus
            aria-label="Szukaj"
          />
          <kbd
            class="hidden sm:inline-flex items-center gap-1 px-2 py-1 rounded-md border border-surface-200/70 dark:border-surface-200/70 text-[10px] font-bold uppercase tracking-wider text-surface-500"
          >
            ESC
          </kbd>
        </header>

        <div class="max-h-[60vh] overflow-y-auto">
          @if (filteredActions().length > 0) {
            <p class="admin-section-label text-surface-500 px-5 pt-4 pb-2">Akcje</p>
            <ul role="listbox" class="px-2">
              @for (a of filteredActions(); track a.id) {
                <li>
                  <button
                    type="button"
                    (click)="runAction(a)"
                    class="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-left hover:bg-surface-100/70 dark:hover:bg-surface-100/40 transition-colors"
                  >
                    <span class="grid size-9 place-items-center rounded-xl bg-amber-100/80 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300 shrink-0">
                      <i [class]="a.icon" class="text-sm"></i>
                    </span>
                    <span class="flex-1 min-w-0">
                      <span class="block text-sm font-bold text-surface-900 truncate">
                        {{ a.label }}
                      </span>
                      <span class="block text-xs text-surface-500 dark:text-surface-400 truncate">
                        {{ a.description }}
                      </span>
                    </span>
                  </button>
                </li>
              }
            </ul>
          }

          @if (customers().length > 0) {
            <p class="admin-section-label text-surface-500 px-5 pt-4 pb-2">Klienci</p>
            <ul role="listbox" class="px-2 pb-3">
              @for (c of customers(); track c.id) {
                <li>
                  <button
                    type="button"
                    (click)="openCustomer(c)"
                    class="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-left hover:bg-surface-100/70 dark:hover:bg-surface-100/40 transition-colors"
                  >
                    <span class="grid size-9 place-items-center rounded-xl bg-slate-900 text-white dark:bg-white dark:text-slate-900 shrink-0 text-[10px] font-black uppercase">
                      {{ initialsFor(c) }}
                    </span>
                    <span class="flex-1 min-w-0">
                      <span class="block text-sm font-bold text-surface-900 truncate">
                        {{ nameOf(c) }}
                      </span>
                      <span class="block text-xs text-surface-500 dark:text-surface-400 truncate">
                        {{ c.phoneNumber || 'Brak telefonu' }}
                      </span>
                    </span>
                    <i class="pi pi-arrow-right text-surface-400 text-xs"></i>
                  </button>
                </li>
              }
            </ul>
          }

          @if (
            filteredActions().length === 0 &&
            customers().length === 0 &&
            query().length >= 2 &&
            !loading()
          ) {
            <div class="px-5 py-10 text-center">
              <i class="pi pi-search text-2xl text-surface-400 mb-2 block"></i>
              <p class="text-sm text-surface-500">Brak wyników dla „{{ query() }}".</p>
            </div>
          }
        </div>

        <footer class="px-5 py-3 border-t border-surface-200/70 dark:border-surface-200/70 flex items-center justify-between text-[11px] text-surface-500 dark:text-surface-400">
          <span class="flex items-center gap-1.5">
            <kbd class="px-1.5 py-0.5 rounded border border-surface-200/70 dark:border-surface-200/70 font-mono">⌘</kbd>
            <kbd class="px-1.5 py-0.5 rounded border border-surface-200/70 dark:border-surface-200/70 font-mono">K</kbd>
            otwiera tę paletę
          </span>
          @if (loading()) {
            <span class="flex items-center gap-1.5">
              <i class="pi pi-spin pi-spinner text-xs"></i>
              Wyszukiwanie…
            </span>
          }
        </footer>
      </div>
    </p-dialog>
  `,
})
export class CommandPaletteComponent {
  private readonly customersClient = inject(CustomersClient);
  private readonly router = inject(Router);

  readonly visible = signal(false);
  readonly query = signal('');
  readonly loading = signal(false);

  /** Two-way bridge for `[(visible)]`. */
  get visibleModel(): boolean {
    return this.visible();
  }
  set visibleModel(v: boolean) {
    this.visible.set(v);
    if (!v) {
      this.query.set('');
    }
  }

  private readonly searchSubject = new Subject<string>();
  private readonly searchResults = toSignal(
    this.searchSubject.pipe(
      debounceTime(220),
      switchMap((q) =>
        q.trim().length < 2
          ? of([] as SearchCustomerDto[])
          : this.customersClient.searchCustomers(q.trim()).pipe(
              catchError(() => of([] as SearchCustomerDto[])),
            ),
      ),
      takeUntilDestroyed(),
    ),
    { initialValue: [] as SearchCustomerDto[] },
  );

  readonly customers = computed(() => this.searchResults() ?? []);

  protected readonly actions: PaletteAction[] = [
    {
      id: 'new-appointment',
      label: 'Nowa wizyta',
      description: 'Dodaj wizytę dla wybranego klienta',
      icon: 'pi pi-calendar-plus',
      keywords: ['wizyta', 'nowa', 'rezerwacja', 'add', 'new'],
      run: () => void this.router.navigate(['/admin/schedule'], { queryParams: { new: '1' } }),
    },
    {
      id: 'go-calendar',
      label: 'Otwórz kalendarz',
      description: 'Plan dnia, tydzień, miesiąc',
      icon: 'pi pi-calendar',
      keywords: ['kalendarz', 'plan', 'grafik'],
      run: () => this.go('/admin/schedule'),
    },
    {
      id: 'add-customer',
      label: 'Dodaj klienta',
      description: 'Nowa karta klienta',
      icon: 'pi pi-user-plus',
      keywords: ['klient', 'dodaj', 'nowy'],
      run: () => this.go('/admin/customers/new'),
    },
    {
      id: 'go-team',
      label: 'Zespół',
      description: 'Lista pracowników i grafiki',
      icon: 'pi pi-users',
      keywords: ['zespol', 'pracownicy', 'team'],
      run: () => this.go('/admin/team'),
    },
    {
      id: 'go-services',
      label: 'Usługi',
      description: 'Katalog usług i kategorie',
      icon: 'pi pi-briefcase',
      keywords: ['uslugi', 'usługi', 'oferta', 'katalog'],
      run: () => this.go('/admin/services'),
    },
    {
      id: 'go-settings',
      label: 'Ustawienia salonu',
      description: 'Marka, godziny, VAT, motyw',
      icon: 'pi pi-cog',
      keywords: ['ustawienia', 'settings'],
      run: () => this.go('/admin/settings'),
    },
  ];

  readonly filteredActions = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.actions.slice(0, 5);
    return this.actions.filter((a) => {
      if (a.label.toLowerCase().includes(q)) return true;
      if (a.description.toLowerCase().includes(q)) return true;
      return a.keywords.some((k) => k.toLowerCase().includes(q));
    });
  });

  constructor() {
    effect(() => {
      const q = this.query();
      const trimmed = q.trim();
      this.loading.set(trimmed.length >= 2);
      this.searchSubject.next(trimmed);
    });
  }

  open(): void {
    this.visible.set(true);
  }

  close(): void {
    this.visible.set(false);
  }

  toggle(): void {
    this.visible.update((v) => !v);
  }

  onQueryInput(e: Event): void {
    this.query.set((e.target as HTMLInputElement).value);
  }

  runAction(a: PaletteAction): void {
    a.run();
    this.close();
  }

  openCustomer(c: SearchCustomerDto): void {
    if (!c.id) return;
    void this.router.navigate(['/admin/customers', c.id]);
    this.close();
  }

  private go(url: string): void {
    void this.router.navigateByUrl(url);
  }

  protected nameOf(c: SearchCustomerDto): string {
    const line = [c.firstName, c.lastName].filter((s): s is string => !!s && s.trim() !== '').join(' ');
    return line || 'Klient';
  }

  protected initialsFor(c: SearchCustomerDto): string {
    const fn = (c.firstName ?? '').trim();
    const ln = (c.lastName ?? '').trim();
    if (fn && ln) return (fn[0] + ln[0]).toUpperCase();
    if (fn) return fn.slice(0, 2).toUpperCase();
    if (ln) return ln.slice(0, 2).toUpperCase();
    return 'KL';
  }

  @HostListener('window:keydown', ['$event'])
  onKeydown(e: KeyboardEvent): void {
    const key = e.key?.toLowerCase();
    if ((e.metaKey || e.ctrlKey) && key === 'k') {
      e.preventDefault();
      this.toggle();
    }
  }
}
