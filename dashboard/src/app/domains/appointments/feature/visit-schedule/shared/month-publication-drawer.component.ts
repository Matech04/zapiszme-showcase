import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DatePickerModule } from 'primeng/datepicker';
import { SelectButtonModule } from 'primeng/selectbutton';

import { EmployeesClient, MonthPublicationDto } from '@core/api/api-client';
import { FormDrawerShellComponent } from '@shared/ui/drawer/form-drawer-shell.component';

type PublicationMode = 'open' | 'opensOn' | 'closed';

/**
 * Ustawienie otwarcia zapisów na JEDEN miesiąc dla JEDNEGO pracownika.
 *
 * Trzy stany, bo „zamknięte" i „otworzy się samo w dniu X" to różne decyzje biznesowe:
 * data otwarcia znosi konieczność pamiętania o kliknięciu w terminie, a zamknięcie bezterminowe
 * zostawia kontrolę u salonu. Brak wpisu (`open`) oznacza „otwarte w granicach horyzontu salonu",
 * a nie „otwarte na zawsze".
 */
@Component({
  selector: 'app-month-publication-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    DatePickerModule,
    SelectButtonModule,
    FormDrawerShellComponent,
  ],
  template: `
    <app-form-drawer-shell
      [isOpen]="isOpen()"
      title="Otwarcie zapisów"
      [label]="monthLabel()"
      submitLabel="Zapisz"
      [submitting]="saving()"
      [submitDisabled]="submitDisabled()"
      (submitClicked)="save()"
      (closeRequested)="closed.emit()"
    >
      <div drawer-body class="flex flex-col gap-4">
        <p class="text-sm text-surface-600 dark:text-surface-300">
          Decyduje, czy <strong>klienci</strong> mogą rezerwować terminy w tym miesiącu.
          Ty i personel wpisujecie wizyty w kalendarzu niezależnie od tego ustawienia.
        </p>

        <div class="flex flex-col gap-2">
          <label class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 px-0.5">
            Zapisy na {{ monthLabel() }}
          </label>
          <p-selectbutton
            data-testid="month-publication-mode"
            [options]="modeOptions"
            [ngModel]="mode()"
            (ngModelChange)="setMode($event)"
            optionLabel="label"
            optionValue="value"
            [allowEmpty]="false"
            ariaLabel="Zapisy na ten miesiąc"
          />
        </div>

        @if (mode() === 'opensOn') {
          <div class="flex flex-col gap-2">
            <label class="text-xs font-bold uppercase tracking-wider text-surface-600 dark:text-surface-300 px-0.5">
              Otwórz zapisy w dniu
            </label>
            <p-date-picker
              data-testid="month-publication-date"
              [ngModel]="opensOnDate()"
              (ngModelChange)="opensOnDate.set($event)"
              dateFormat="dd.mm.yy"
              [showIcon]="true"
              [readonlyInput]="true"
              [fluid]="true"
              appendTo="body"
            />
            <p class="text-xs text-surface-500 dark:text-surface-400 px-0.5">
              Miesiąc otworzy się sam tego dnia — nie musisz o tym pamiętać.
            </p>
          </div>
        }

        <p class="text-xs text-surface-500 dark:text-surface-400 px-0.5">
          {{ hint() }}
        </p>

        @if (error()) {
          <p class="text-sm font-semibold text-red-500" role="alert" data-testid="month-publication-error">
            {{ error() }}
          </p>
        }
      </div>
    </app-form-drawer-shell>
  `,
})
export class MonthPublicationDrawerComponent {
  private readonly employees = inject(EmployeesClient);

  readonly isOpen = input<boolean>(false);
  readonly employeeId = input.required<string>();
  readonly year = input.required<number>();
  readonly month = input.required<number>();
  /** Aktualny wpis publikacji (jeśli istnieje) — źródło stanu początkowego formularza. */
  readonly publication = input<MonthPublicationDto | null>(null);

  readonly closed = output<void>();
  readonly saved = output<void>();

  protected readonly modeOptions = [
    { label: 'Otwarte', value: 'open' as const },
    { label: 'Otwórz w dniu…', value: 'opensOn' as const },
    { label: 'Zamknięte', value: 'closed' as const },
  ];

  protected readonly mode = signal<PublicationMode>('open');
  protected readonly opensOnDate = signal<Date | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Przy każdym otwarciu drawera odtwarzamy stan z zapisanego wpisu — inaczej po zamknięciu
    // bez zapisu formularz pokazywałby porzucony wybór przy następnym otwarciu.
    effect(() => {
      if (!this.isOpen()) return;

      const current = this.publication();
      this.error.set(null);

      if (!current) {
        this.mode.set('open');
        this.opensOnDate.set(null);
        return;
      }

      if (!current.opensOn) {
        this.mode.set('closed');
        this.opensOnDate.set(null);
        return;
      }

      this.mode.set('opensOn');
      this.opensOnDate.set(new Date(current.opensOn));
    });
  }

  protected readonly monthLabel = computed(() =>
    new Intl.DateTimeFormat('pl-PL', { month: 'long', year: 'numeric' }).format(
      new Date(this.year(), this.month() - 1, 1),
    ),
  );

  protected readonly submitDisabled = computed(
    () => this.saving() || (this.mode() === 'opensOn' && !this.opensOnDate()),
  );

  protected readonly hint = computed(() => {
    switch (this.mode()) {
      case 'closed':
        return 'Klienci nie zobaczą żadnych terminów w tym miesiącu, dopóki go nie otworzysz.';
      case 'opensOn':
        return 'Do tego dnia miesiąc jest niewidoczny dla klientów.';
      default:
        return 'Terminy są widoczne dla klientów na tyle naprzód, na ile pozwala ustawienie salonu.';
    }
  });

  protected setMode(next: PublicationMode): void {
    this.mode.set(next);
    if (next === 'opensOn' && !this.opensOnDate()) {
      // Domyślnie pierwszy dzień ustawianego miesiąca — najczęstszy przypadek („wrzesień od 1.09").
      this.opensOnDate.set(new Date(this.year(), this.month() - 1, 1));
    }
  }

  protected save(): void {
    if (this.submitDisabled()) return;

    this.saving.set(true);
    this.error.set(null);

    const done = {
      next: () => {
        this.saving.set(false);
        this.saved.emit();
        this.closed.emit();
      },
      error: () => {
        this.saving.set(false);
        this.error.set('Nie udało się zapisać ustawienia. Spróbuj ponownie.');
      },
    };

    if (this.mode() === 'open') {
      // Kasujemy wpis, a nie zapisujemy „otwarte" — brak wpisu oznacza powrót pod horyzont salonu.
      this.employees
        .removeMonthPublication(this.employeeId(), this.year(), this.month())
        .subscribe(done);
      return;
    }

    const opensOn = this.mode() === 'opensOn' ? this.toIsoDate(this.opensOnDate()!) : undefined;

    this.employees
      .setMonthPublication(this.employeeId(), {
        year: this.year(),
        month: this.month(),
        opensOn: opensOn as unknown as Date | undefined,
      } as MonthPublicationDto)
      .subscribe(done);
  }

  /** Lokalna data → `YYYY-MM-DD` bez konwersji na UTC (toISOString cofnąłby dzień przed północą). */
  private toIsoDate(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }
}
