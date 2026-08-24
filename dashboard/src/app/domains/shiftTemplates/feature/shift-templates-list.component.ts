import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { rxResource } from '@angular/core/rxjs-interop';
import { ShiftTemplateDto, ShiftTemplatesClient, SlotGenerationMode } from '@core/api/api-client';
import { safeBackWith } from '@core/navigation/safe-back';

@Component({
  selector: 'app-shift-templates-list',
  standalone: true,
  imports: [CommonModule, RouterLink, Button],
  template: `
    <div class="min-h-full bg-surface-50 dark:bg-surface-950">
      <div class="max-w-3xl mx-auto px-4 sm:px-6 py-8 sm:py-10">
        <nav
          class="text-xs sm:text-sm text-surface-500 dark:text-surface-400 mb-4 flex flex-wrap items-center gap-x-2 gap-y-1"
        >
          <a routerLink="/admin/resources" class="hover:text-primary transition-colors">Zarządzanie</a>
          <span class="text-surface-300 dark:text-surface-600" aria-hidden="true">/</span>
          <span class="text-surface-700 dark:text-surface-300">Szablony zmian</span>
        </nav>

        <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 mb-8">
          <div>
            <h1
              class="text-2xl sm:text-3xl font-bold text-surface-900 tracking-tight mb-2"
            >
              Szablony zmian
            </h1>
            <p class="text-surface-600 dark:text-surface-400 text-sm sm:text-base max-w-xl leading-relaxed">
              Nazwane wzorce godzin — elastyczne bloki pracy z przerwami albo ustalone godziny startu —
              które wczytasz jednym kliknięciem przy ustawianiu grafików i dni specjalnych.
            </p>
          </div>

          <div class="flex flex-col sm:flex-row gap-2 sm:gap-3 shrink-0 w-full sm:w-auto">
            <p-button
              label="Wróć"
              severity="secondary"
              [outlined]="true"
              styleClass="w-full sm:w-auto"
              [disabled]="templatesData.isLoading()"
              (onClick)="goBack()"
            />
            <p-button
              label="Nowy szablon"
              icon="pi pi-plus"
              styleClass="w-full sm:w-auto font-semibold"
              [disabled]="templatesData.isLoading()"
              (onClick)="goToNew()"
            />
          </div>
        </div>

        @if (templatesData.isLoading()) {
          <div
            class="rounded-2xl border border-surface-200 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-12 text-center text-surface-500"
          >
            Wczytywanie…
          </div>
        } @else if (sortedTemplates().length === 0) {
          <div
            class="rounded-2xl border-2 border-dashed border-surface-200 dark:border-surface-100 p-12 text-center bg-surface-0 dark:bg-surface-50/60"
          >
            <div class="w-16 h-16 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mx-auto mb-4">
              <i class="pi pi-th-large text-2xl text-surface-400"></i>
            </div>
            <h3 class="text-lg font-bold text-surface-900 mb-2">Brak szablonów</h3>
            <p class="text-surface-600 dark:text-surface-400 text-sm max-w-sm mx-auto mb-6">
              Utwórz pierwszy szablon zmiany — zaoszczędzi czas przy konfigurowaniu kolejnych grafików.
            </p>
            <p-button label="Utwórz szablon" icon="pi pi-plus" (onClick)="goToNew()" />
          </div>
        } @else {
          <div class="flex flex-col gap-3 sm:gap-4">
            @for (tpl of sortedTemplates(); track tpl.id) {
              <div
                class="group rounded-2xl border border-surface-200/90 dark:border-surface-100 bg-surface-0 dark:bg-surface-50 p-4 shadow-sm transition-[box-shadow,border-color] hover:shadow-md dark:hover:border-surface-700"
              >
                <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
                  <div class="min-w-0 space-y-1">
                    <div class="flex items-center gap-2 flex-wrap">
                      <p class="text-base sm:text-lg font-semibold text-surface-900">
                        {{ tpl.name }}
                      </p>
                      <span
                        class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-md bg-primary/10 text-primary border border-primary/20"
                      >
                        {{ modeLabel(tpl) }}
                      </span>
                    </div>
                    <p class="text-sm text-surface-600 dark:text-surface-400">
                      {{ formatDetail(tpl) }}
                    </p>
                  </div>
                  <div class="flex items-center gap-2 shrink-0">
                    <p-button
                      icon="pi pi-pencil"
                      [rounded]="true"
                      [text]="true"
                      severity="secondary"
                      [disabled]="removingId() !== null"
                      styleClass="!w-9 !h-9 !p-0"
                      (onClick)="goToEdit(tpl)"
                      [attr.aria-label]="'Edytuj szablon: ' + tpl.name"
                    />
                    <p-button
                      icon="pi pi-trash"
                      [rounded]="true"
                      [text]="true"
                      severity="danger"
                      [disabled]="removingId() !== null"
                      [loading]="removingId() === tpl.id"
                      styleClass="!w-9 !h-9 !p-0"
                      (onClick)="confirmRemove(tpl)"
                      [attr.aria-label]="'Usuń szablon: ' + tpl.name"
                    />
                  </div>
                </div>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
})
export class ShiftTemplatesListComponent {
  private templatesClient = inject(ShiftTemplatesClient);
  private location = inject(Location);
  private router = inject(Router);
  private confirmationService = inject(ConfirmationService);
  private messageService = inject(MessageService);

  removingId = signal<string | null>(null);

  templatesData = rxResource({
    stream: () => this.templatesClient.getShiftTemplates(),
    defaultValue: [] as ShiftTemplateDto[],
  });

  sortedTemplates = computed(() => {
    const list = this.templatesData.value() ?? [];
    return [...list].sort((a, b) => (a.name ?? '').localeCompare(b.name ?? '', 'pl'));
  });

  goBack() {
    safeBackWith(this.location, this.router, '/admin/resources');
  }

  goToNew() {
    void this.router.navigate(['/admin/resources/shift-templates/new']);
  }

  goToEdit(tpl: ShiftTemplateDto) {
    if (!tpl.id) return;
    void this.router.navigate(['/admin/resources/shift-templates', tpl.id, 'edit']);
  }

  modeLabel(tpl: ShiftTemplateDto): string {
    return tpl.slotGenerationMode === SlotGenerationMode.FixedStartTimes ? 'Ustalone godziny' : 'Elastyczne godziny';
  }

  confirmRemove(tpl: ShiftTemplateDto) {
    if (!tpl.id) return;
    this.confirmationService.confirm({
      message: `Czy na pewno usunąć szablon "${tpl.name}"?`,
      header: 'Usuwanie szablonu',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Tak',
      rejectLabel: 'Nie',
      accept: () => {
        const id = tpl.id!;
        this.removingId.set(id);
        this.templatesClient.deleteShiftTemplate(id).subscribe({
          next: () => {
            this.removingId.set(null);
            this.messageService.add({
              severity: 'success',
              summary: 'Usunięto',
              detail: 'Szablon został usunięty.',
              life: 4000,
            });
            this.templatesData.reload();
          },
          error: () => {
            this.removingId.set(null);
          },
        });
      },
    });
  }

  formatDetail(tpl: ShiftTemplateDto): string {
    if (tpl.slotGenerationMode === SlotGenerationMode.FixedStartTimes) {
      const times = (tpl.fixedStartTimes ?? [])
        .filter((t) => !!t)
        .map((t) => this.trim(t))
        .sort((a, b) => a.localeCompare(b));
      return times.length ? times.join(', ') : '—';
    }
    const works = (tpl.workRanges ?? []).filter((r) => !!r?.startTime && !!r?.endTime);
    if (!works.length) return '—';
    const segments = works
      .slice()
      .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''))
      .map((r) => `${this.trim(r.startTime)}–${this.trim(r.endTime)}`);
    const breaks = (tpl.breaks ?? [])
      .filter((b) => !!b?.startTime && !!b?.endTime)
      .map((b) => `${this.trim(b.startTime)}–${this.trim(b.endTime)}`);
    const breaksLabel = breaks.length ? ` (przerwy: ${breaks.join(', ')})` : '';
    return `${segments.join(', ')}${breaksLabel}`;
  }

  private trim(t: string | undefined): string {
    if (!t) return '';
    return t.substring(0, 5);
  }
}
