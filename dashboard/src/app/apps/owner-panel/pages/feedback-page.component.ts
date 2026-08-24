import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { FeedbackClient } from '@core/api/api-client';
import { EMPTY, catchError } from 'rxjs';

type FeedbackKind = 'bug' | 'suggestion';

@Component({
  selector: 'app-feedback-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="admin-page-shell admin-page-pad-for-bottom-nav">
      <div class="admin-page-container max-w-2xl">

        <header class="admin-glass-card admin-page-hero">
          <span class="admin-section-label text-primary mb-3 block">Pomoc</span>
          <h1 class="admin-page-title text-surface-900 mb-3">Zgłoś problem lub propozycję</h1>
          <p class="admin-page-lead text-surface-700 dark:text-surface-300">
            Twoja opinia trafia bezpośrednio do twórców aplikacji. Opiszemy każde zgłoszenie.
          </p>
        </header>

        @if (submitted()) {
          <div class="admin-glass-card p-8 flex flex-col items-center text-center gap-4">
            <span class="grid size-16 place-items-center rounded-3xl bg-emerald-100 dark:bg-emerald-950/50 text-emerald-600 dark:text-emerald-400">
              <i class="pi pi-check-circle text-3xl"></i>
            </span>
            <div>
              <p class="text-lg font-bold text-surface-900 mb-1">Dziękujemy!</p>
              <p class="text-sm text-surface-500 dark:text-surface-400">Zgłoszenie zostało wysłane. Odpowiemy najszybciej jak to możliwe.</p>
            </div>
            <div class="flex gap-2 mt-2">
              <button
                type="button"
                (click)="reset()"
                class="px-5 py-2.5 rounded-2xl text-sm font-bold border border-surface-200 dark:border-surface-200 hover:border-primary/50 transition-colors text-surface-700 dark:text-surface-900"
              >
                Wyślij kolejne
              </button>
              <button
                type="button"
                (click)="router.navigate(['/admin'])"
                class="px-5 py-2.5 rounded-2xl text-sm font-bold bg-slate-950 dark:bg-white text-white dark:text-slate-950 hover:opacity-80 transition-all"
              >
                Wróć do panelu
              </button>
            </div>
          </div>
        } @else {
          <div class="admin-glass-card p-6 flex flex-col gap-6">

            <!-- Kind toggle -->
            <div>
              <p class="admin-section-label text-surface-600 dark:text-surface-400 mb-3">Typ zgłoszenia</p>
              <div class="flex gap-2 p-1 bg-surface-100/70 dark:bg-surface-100/50 rounded-2xl">
                <button
                  type="button"
                  (click)="kind.set('bug')"
                  class="flex-1 flex items-center justify-center gap-2 py-3 rounded-xl text-sm font-bold transition-all"
                  [class.bg-white]="kind() === 'bug'"
                  [class.dark:bg-surface-200]="kind() === 'bug'"
                  [class.shadow-sm]="kind() === 'bug'"
                  [class.text-red-600]="kind() === 'bug'"
                  [class.dark:text-red-400]="kind() === 'bug'"
                  [class.text-surface-500]="kind() !== 'bug'"
                >
                  <i class="pi pi-bug"></i>
                  Błąd w aplikacji
                </button>
                <button
                  type="button"
                  (click)="kind.set('suggestion')"
                  class="flex-1 flex items-center justify-center gap-2 py-3 rounded-xl text-sm font-bold transition-all"
                  [class.bg-white]="kind() === 'suggestion'"
                  [class.dark:bg-surface-200]="kind() === 'suggestion'"
                  [class.shadow-sm]="kind() === 'suggestion'"
                  [class.text-primary]="kind() === 'suggestion'"
                  [class.text-surface-500]="kind() !== 'suggestion'"
                >
                  <i class="pi pi-lightbulb"></i>
                  Propozycja / pomysł
                </button>
              </div>
            </div>

            <!-- Title -->
            <div class="flex flex-col gap-2">
              <label class="admin-section-label text-surface-600 dark:text-surface-400">
                {{ kind() === 'bug' ? 'Co nie działa?' : 'Co chcesz zaproponować?' }}
              </label>
              <input
                type="text"
                [(ngModel)]="title"
                maxlength="200"
                [placeholder]="kind() === 'bug' ? 'np. Przycisk zapisu nie reaguje na kliknięcie' : 'np. Eksport listy klientów do pliku CSV'"
                class="w-full rounded-2xl border border-surface-200 dark:border-surface-200 bg-white/80 dark:bg-surface-50/60 px-4 py-3.5 text-sm text-surface-900 placeholder-surface-400 focus:outline-none focus:ring-2 focus:ring-primary/40 transition-shadow"
              />
              <p class="text-xs text-surface-400 text-right">{{ title.length }}/200</p>
            </div>

            <!-- Description -->
            <div class="flex flex-col gap-2">
              <label class="admin-section-label text-surface-600 dark:text-surface-400">
                {{ kind() === 'bug' ? 'Jak odtworzyć problem? Co się stało?' : 'Opisz swój pomysł i kontekst' }}
              </label>
              <textarea
                [(ngModel)]="description"
                maxlength="2000"
                rows="7"
                [placeholder]="kind() === 'bug' ? 'Opisz kroki prowadzące do błędu, co oczekiwałeś i co się faktycznie stało…' : 'Opisz problem, który chcesz rozwiązać, i jak wyobrażasz sobie rozwiązanie…'"
                class="w-full rounded-2xl border border-surface-200 dark:border-surface-200 bg-white/80 dark:bg-surface-50/60 px-4 py-3.5 text-sm text-surface-900 placeholder-surface-400 focus:outline-none focus:ring-2 focus:ring-primary/40 resize-none transition-shadow"
              ></textarea>
              <p class="text-xs text-surface-400 text-right">{{ description.length }}/2000</p>
            </div>

            <!-- Submit -->
            <div class="flex items-center justify-between pt-2 border-t border-surface-200/70 dark:border-surface-200/70">
              <p class="text-xs text-surface-400 max-w-xs">
                Zgłoszenie zostanie wysłane mailem do twórców aplikacji wraz z adresem bieżącej strony.
              </p>
              <button
                type="button"
                (click)="submit()"
                [disabled]="!canSubmit() || loading()"
                class="flex items-center gap-2 px-6 py-3 rounded-2xl text-sm font-bold bg-slate-950 dark:bg-white text-white dark:text-slate-950 hover:opacity-80 disabled:opacity-40 transition-all shrink-0 ml-4"
              >
                @if (loading()) {
                  <i class="pi pi-spin pi-spinner text-xs"></i>
                } @else {
                  <i class="pi pi-send text-xs"></i>
                }
                Wyślij zgłoszenie
              </button>
            </div>

          </div>
        }

      </div>
    </div>
  `,
})
export class FeedbackPageComponent {
  private readonly feedbackClient = inject(FeedbackClient);
  private readonly messageService = inject(MessageService);
  protected readonly router = inject(Router);

  protected readonly kind = signal<FeedbackKind>('bug');
  protected readonly loading = signal(false);
  protected readonly submitted = signal(false);
  protected title = '';
  protected description = '';

  protected canSubmit(): boolean {
    return this.title.trim().length >= 3 && this.description.trim().length >= 5;
  }

  protected reset(): void {
    this.title = '';
    this.description = '';
    this.kind.set('bug');
    this.submitted.set(false);
  }

  protected submit(): void {
    if (!this.canSubmit() || this.loading()) return;
    this.loading.set(true);

    let hadError = false;

    this.feedbackClient
      .submit({
        kind: this.kind(),
        title: this.title.trim(),
        description: this.description.trim(),
        pageUrl: this.router.url,
      })
      .pipe(catchError(() => { hadError = true; return EMPTY; }))
      .subscribe({
        complete: () => {
          this.loading.set(false);
          if (hadError) {
            this.messageService.add({
              severity: 'error',
              summary: 'Błąd',
              detail: 'Nie udało się wysłać zgłoszenia. Spróbuj ponownie.',
              life: 5000,
            });
          } else {
            this.submitted.set(true);
          }
        },
      });
  }
}
