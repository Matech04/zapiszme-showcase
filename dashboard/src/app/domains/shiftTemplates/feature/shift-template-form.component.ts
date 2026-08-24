import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { Router, RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectButtonModule } from 'primeng/selectbutton';
import { MessageService } from 'primeng/api';
import { rxResource } from '@angular/core/rxjs-interop';
import {
  CreateShiftTemplateCommand,
  ShiftTemplateDto,
  ShiftTemplatesClient,
  SlotGenerationMode,
  TimeRangeDto,
  UpdateShiftTemplateRequest,
} from '@core/api/api-client';
import { TimeBlock } from '@domains/employees/feature/availability/weekly-schedule/ui/time-block/time-block';
import { SlotTimeRow } from '@domains/employees/feature/availability/weekly-schedule/ui/slot-time-row/slot-time-row';
import { safeBackWith } from '@core/navigation/safe-back';

@Component({
  selector: 'app-shift-template-form',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, Button, InputTextModule, SelectButtonModule, TimeBlock, SlotTimeRow],
  template: `
    <div class="min-h-full bg-surface-50 dark:bg-surface-950">
      <div class="max-w-3xl mx-auto px-4 sm:px-6 py-8 sm:py-10">
        <nav
          class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1"
        >
          <a routerLink="/admin/resources" class="hover:text-primary transition-colors">Zarządzanie</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <a routerLink="/admin/resources/shift-templates" class="hover:text-primary transition-colors">Szablony zmian</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <span class="text-surface-700 dark:text-surface-300">{{ headingLabel() }}</span>
        </nav>

        <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 mb-8">
          <div>
            <h1 class="text-2xl sm:text-3xl font-bold text-surface-900 tracking-tight mb-2">
              {{ headingLabel() }}
            </h1>
            <p class="text-surface-600 dark:text-surface-400 text-sm sm:text-base max-w-xl leading-relaxed">
              Nadaj nazwę i ustaw godziny. Szablon będzie można wczytać jednym kliknięciem przy
              ustawianiu grafiku oraz dni specjalnych.
            </p>
          </div>

          <div class="flex flex-col sm:flex-row gap-2 sm:gap-3 shrink-0 w-full sm:w-auto">
            <p-button
              label="Anuluj"
              severity="secondary"
              [outlined]="true"
              styleClass="w-full sm:w-auto"
              [disabled]="saving()"
              (onClick)="cancel()"
            />
            <p-button
              label="Zapisz szablon"
              icon="pi pi-check"
              data-testid="shift-template-save"
              styleClass="w-full sm:w-auto font-semibold"
              [loading]="saving()"
              [disabled]="templateData.isLoading()"
              (onClick)="save()"
            />
          </div>
        </div>

        <div class="rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 sm:p-6 shadow-sm flex flex-col gap-6">
          <div class="flex flex-col gap-2">
            <label for="tpl-name" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
              Nazwa szablonu
            </label>
            <input
              pInputText
              id="tpl-name"
              data-testid="shift-template-name"
              placeholder="np. Zmiana poranna"
              [ngModel]="name()"
              (ngModelChange)="name.set($event)"
              class="w-full py-3 px-4 rounded-xl"
            />
          </div>

          <div class="flex flex-col gap-2">
            <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
              Sposób ustalania godzin
            </label>
            <p-selectbutton
              data-testid="shift-template-mode"
              [options]="modeOptions"
              [ngModel]="mode()"
              (ngModelChange)="mode.set($event)"
              optionLabel="label"
              optionValue="value"
              [allowEmpty]="false"
              ariaLabel="Sposób ustalania godzin"
            />
            <p class="text-xs text-surface-500 dark:text-surface-400 px-1 leading-relaxed">
              @if (mode() === SlotGenerationMode.FixedStartTimes) {
                <strong>Ustalone godziny</strong> — klient wybiera tylko z godzin, które sam(a) wpisujesz (np. 9:00, 12:00, 15:00).
              } @else {
                <strong>Elastyczne godziny</strong> — klient wybiera dowolną wolną godzinę w Twoich blokach pracy; sloty tworzą się automatycznie (np. co 30 min między 9:00 a 17:00).
              }
            </p>
          </div>

          @if (mode() === SlotGenerationMode.FixedStartTimes) {
            <div class="flex flex-col gap-3">
              <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                Godziny startu
              </label>
              @if (fixedStartTimes().length === 0) {
                <p class="text-sm text-surface-500 dark:text-surface-400 px-1">Dodaj co najmniej jedną godzinę startu.</p>
              } @else {
                <div class="flex flex-col gap-2.5">
                  @for (t of fixedStartTimes(); track $index) {
                    <app-slot-time-row
                      [time]="t"
                      (changedTime)="updateFixedTime($index, $event)"
                      (deleteTime)="removeFixedTime($index)"
                    />
                  }
                </div>
              }
              <button
                type="button"
                data-testid="shift-template-add-fixed-time"
                class="mt-1 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-primary/35 dark:border-primary/40 bg-transparent py-3 px-3 text-sm font-semibold text-primary hover:bg-primary/5 transition-colors"
                (click)="addFixedTime()"
              >
                <i class="pi pi-plus text-xs" aria-hidden="true"></i>
                Dodaj godzinę
              </button>
            </div>
          } @else {
            <div class="flex flex-col gap-3">
              <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                Bloki pracy
              </label>
              @if (workRanges().length === 0) {
                <p class="text-sm text-surface-500 dark:text-surface-400 px-1">Dodaj co najmniej jeden blok pracy.</p>
              } @else {
                <div class="flex flex-col gap-2.5">
                  @for (block of workRanges(); track $index) {
                    <app-time-block
                      [block]="block"
                      (changedBlock)="updateWorkRange($index, $event)"
                      (deleteBlock)="removeWorkRange($index)"
                    />
                  }
                </div>
              }
              <button
                type="button"
                class="mt-1 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-primary/35 dark:border-primary/40 bg-transparent py-3 px-3 text-sm font-semibold text-primary hover:bg-primary/5 transition-colors"
                (click)="addWorkRange()"
              >
                <i class="pi pi-plus text-xs" aria-hidden="true"></i>
                Dodaj blok pracy
              </button>
            </div>

            <div class="flex flex-col gap-3 pt-2 border-t border-surface-200/70 dark:border-surface-100">
              <label class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
                Przerwy w trakcie zmiany
              </label>
              @if (breaks().length === 0) {
                <p class="text-sm text-surface-500 dark:text-surface-400 px-1">Brak przerw — zmiana ciągła.</p>
              } @else {
                <div class="flex flex-col gap-2.5">
                  @for (br of breaks(); track $index) {
                    <app-time-block
                      [block]="br"
                      (changedBlock)="updateBreak($index, $event)"
                      (deleteBlock)="removeBreak($index)"
                    />
                  }
                </div>
              }
              <button
                type="button"
                class="mt-1 w-full flex items-center justify-center gap-2 rounded-xl border border-dashed border-surface-300/70 dark:border-surface-200 bg-transparent py-3 px-3 text-sm font-semibold text-surface-700 hover:bg-surface-100 dark:hover:bg-surface-100/70 transition-colors"
                (click)="addBreak()"
              >
                <i class="pi pi-plus text-xs" aria-hidden="true"></i>
                Dodaj przerwę
              </button>
            </div>
          }

          @if (formError()) {
            <p class="text-sm text-red-600 dark:text-red-400 px-1">{{ formError() }}</p>
          }
        </div>
      </div>
    </div>
  `,
})
export class ShiftTemplateFormComponent {
  private templatesClient = inject(ShiftTemplatesClient);
  private location = inject(Location);
  private router = inject(Router);
  private messageService = inject(MessageService);

  /** Z route param `:id` (tryb edycji). Brak = tworzenie nowego. */
  id = input<string | undefined>(undefined);

  protected readonly SlotGenerationMode = SlotGenerationMode;
  protected readonly modeOptions = [
    { label: 'Elastyczne godziny', value: SlotGenerationMode.Grid },
    { label: 'Ustalone godziny', value: SlotGenerationMode.FixedStartTimes },
  ];

  saving = signal(false);
  formError = signal<string | null>(null);

  protected mode$ = computed<'create' | 'edit'>(() => (this.id() ? 'edit' : 'create'));
  protected headingLabel = computed(() => (this.mode$() === 'edit' ? 'Edytuj szablon' : 'Nowy szablon zmiany'));

  templateData = rxResource({
    params: () => this.id(),
    stream: ({ params }) => (params ? this.templatesClient.getShiftTemplateById(params) : of(undefined)),
  });

  private loaded = computed<ShiftTemplateDto | undefined>(() => this.templateData.value() ?? undefined);

  name = linkedSignal<ShiftTemplateDto | undefined, string>({
    source: () => this.loaded(),
    computation: (t) => t?.name ?? '',
  });

  mode = linkedSignal<ShiftTemplateDto | undefined, SlotGenerationMode>({
    source: () => this.loaded(),
    computation: (t) => t?.slotGenerationMode ?? SlotGenerationMode.Grid,
  });

  workRanges = linkedSignal<ShiftTemplateDto | undefined, TimeRangeDto[]>({
    source: () => this.loaded(),
    computation: (t) => {
      if (!t) return [{ startTime: '09:00', endTime: '17:00' }];
      return (t.workRanges ?? [])
        .filter((r) => !!r?.startTime && !!r?.endTime)
        .map((r) => ({ startTime: this.trim(r.startTime), endTime: this.trim(r.endTime) }));
    },
  });

  breaks = linkedSignal<ShiftTemplateDto | undefined, TimeRangeDto[]>({
    source: () => this.loaded(),
    computation: (t) =>
      (t?.breaks ?? [])
        .filter((b) => !!b?.startTime && !!b?.endTime)
        .map((b) => ({ startTime: this.trim(b.startTime), endTime: this.trim(b.endTime) })),
  });

  fixedStartTimes = linkedSignal<ShiftTemplateDto | undefined, string[]>({
    source: () => this.loaded(),
    computation: (t) => (t?.fixedStartTimes ?? []).filter((x) => !!x).map((x) => this.trim(x)),
  });

  // ── grid editors ──
  addWorkRange() {
    this.workRanges.update((list) => [...list, { startTime: '09:00', endTime: '17:00' }]);
  }
  updateWorkRange(index: number, updated: TimeRangeDto) {
    this.workRanges.update((list) =>
      list.map((b, i) => (i === index ? { startTime: this.trim(updated.startTime), endTime: this.trim(updated.endTime) } : b)),
    );
  }
  removeWorkRange(index: number) {
    this.workRanges.update((list) => list.filter((_, i) => i !== index));
  }
  addBreak() {
    this.breaks.update((list) => [...list, { startTime: '12:00', endTime: '12:30' }]);
  }
  updateBreak(index: number, updated: TimeRangeDto) {
    this.breaks.update((list) =>
      list.map((b, i) => (i === index ? { startTime: this.trim(updated.startTime), endTime: this.trim(updated.endTime) } : b)),
    );
  }
  removeBreak(index: number) {
    this.breaks.update((list) => list.filter((_, i) => i !== index));
  }

  // ── fixed editors ──
  addFixedTime() {
    this.fixedStartTimes.update((list) => [...list, '12:00']);
  }
  updateFixedTime(index: number, time: string) {
    this.fixedStartTimes.update((list) => list.map((t, i) => (i === index ? time : t)));
  }
  removeFixedTime(index: number) {
    this.fixedStartTimes.update((list) => list.filter((_, i) => i !== index));
  }

  cancel() {
    safeBackWith(this.location, this.router, '/admin/resources/shift-templates');
  }

  save() {
    const name = this.name().trim();
    if (!name) {
      this.formError.set('Nazwa szablonu jest wymagana.');
      return;
    }

    const isFixed = this.mode() === SlotGenerationMode.FixedStartTimes;
    let workRangesIso: TimeRangeDto[] = [];
    let breaksIso: TimeRangeDto[] = [];
    let fixedIso: string[] | undefined;

    if (isFixed) {
      const times = Array.from(
        new Set(this.fixedStartTimes().map((t) => t?.trim()).filter((t): t is string => !!t)),
      ).sort((a, b) => a.localeCompare(b));
      if (!times.length) {
        this.formError.set('Dodaj co najmniej jedną godzinę startu.');
        return;
      }
      if (times.some((t) => this.toMinutesOrNull(t) === null)) {
        this.formError.set('Nieprawidłowy format godziny.');
        return;
      }
      fixedIso = times.map((t) => this.toIso(t));
    } else {
      const workRanges = this.workRanges();
      if (workRanges.length === 0) {
        this.formError.set('Dodaj co najmniej jeden blok pracy.');
        return;
      }
      for (const r of workRanges) {
        if (!r.startTime || !r.endTime) {
          this.formError.set('Uzupełnij godziny każdego bloku pracy.');
          return;
        }
        if (this.toMinutes(r.startTime) >= this.toMinutes(r.endTime)) {
          this.formError.set('Godzina zakończenia musi być późniejsza niż rozpoczęcia w każdym bloku pracy.');
          return;
        }
      }
      const sorted = workRanges
        .map((r) => ({ start: this.toMinutes(r.startTime!), end: this.toMinutes(r.endTime!) }))
        .sort((a, b) => a.start - b.start);
      for (let i = 0; i < sorted.length - 1; i++) {
        if (sorted[i].end > sorted[i + 1].start) {
          this.formError.set('Bloki pracy nie mogą się nakładać.');
          return;
        }
      }
      const breaks = this.breaks();
      for (const br of breaks) {
        if (!br.startTime || !br.endTime) {
          this.formError.set('Uzupełnij godziny każdej przerwy.');
          return;
        }
        if (this.toMinutes(br.startTime) >= this.toMinutes(br.endTime)) {
          this.formError.set('Każda przerwa musi mieć poprawny zakres godzin.');
          return;
        }
        const bStart = this.toMinutes(br.startTime);
        const bEnd = this.toMinutes(br.endTime);
        const fits = workRanges.some(
          (r) => this.toMinutes(r.startTime!) <= bStart && bEnd <= this.toMinutes(r.endTime!),
        );
        if (!fits) {
          this.formError.set('Przerwa musi mieścić się w którymś z bloków pracy.');
          return;
        }
      }
      workRangesIso = workRanges.map((r) => ({ startTime: this.toIso(r.startTime!), endTime: this.toIso(r.endTime!) }));
      breaksIso = breaks.map((b) => ({ startTime: this.toIso(b.startTime!), endTime: this.toIso(b.endTime!) }));
    }

    this.formError.set(null);
    this.saving.set(true);

    const id = this.id();
    const done = (detail: string) => {
      this.saving.set(false);
      this.messageService.add({ severity: 'success', summary: 'Zapisano', detail, life: 4000 });
      void this.router.navigate(['/admin/resources/shift-templates']);
    };
    const fail = () => this.saving.set(false);

    if (id) {
      const payload: UpdateShiftTemplateRequest = {
        name,
        slotGenerationMode: this.mode(),
        workRanges: workRangesIso,
        breaks: breaksIso,
        fixedStartTimes: fixedIso,
      };
      this.templatesClient.updateShiftTemplate(id, payload).subscribe({
        next: () => done('Szablon został zaktualizowany.'),
        error: fail,
      });
    } else {
      const payload: CreateShiftTemplateCommand = {
        name,
        slotGenerationMode: this.mode(),
        workRanges: workRangesIso,
        breaks: breaksIso,
        fixedStartTimes: fixedIso,
      };
      this.templatesClient.createShiftTemplate(payload).subscribe({
        next: () => done('Szablon zmiany został utworzony.'),
        error: fail,
      });
    }
  }

  private trim(t: string | undefined): string {
    if (!t) return '';
    return t.substring(0, 5);
  }

  private toMinutes(hhmm: string): number {
    const [h, m] = hhmm.split(':').map(Number);
    return (h ?? 0) * 60 + (m ?? 0);
  }

  private toMinutesOrNull(hhmm: string): number | null {
    return /^([01]\d|2[0-3]):([0-5]\d)$/.test(hhmm ?? '') ? this.toMinutes(hhmm) : null;
  }

  private toIso(hhmm: string): string {
    if (!hhmm) return '00:00:00';
    const parts = hhmm.split(':');
    const h = (parts[0] ?? '0').padStart(2, '0');
    const m = (parts[1] ?? '0').padStart(2, '0');
    const s = (parts[2] ?? '0').padStart(2, '0');
    return `${h}:${m}:${s}`;
  }
}
