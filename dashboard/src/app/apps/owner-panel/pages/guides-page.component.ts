import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { GuideProgressService } from '@core/guides/guide-progress.service';
import { GuideService } from '@core/guides/guide.service';
import { GUIDE_CATEGORY_LABELS, GuideCategory, GuideDef } from '@core/guides/guide.types';
import { guidesForRole } from '@core/guides/guides.registry';

interface GuideGroup {
  category: GuideCategory;
  title: string;
  guides: GuideDef[];
}

/**
 * Katalog przewodników — jedno miejsce, w którym widać wszystkie przewodniki, co już zostało
 * przejdzione i skąd każdy da się odtworzyć.
 *
 * Powstał, bo poprzednie przewodniki dało się uruchomić wyłącznie przypadkiem: trzeba było
 * trafić na właściwą stronę i zauważyć baner, który po jednym kliknięciu znikał na zawsze.
 *
 * Zawartość filtruje rola — pracownik nie zobaczy „Dodajmy usługę", bo katalog usług chroni
 * `staffManagementGuard` i przewodnik odbiłby się od guarda w drugim kroku.
 */
@Component({
  selector: 'app-guides-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="admin-page-shell admin-page-pad-for-bottom-nav">
      <div class="admin-page-container">
        <header class="mb-6">
          <p class="admin-section-label text-primary mb-2">Przewodniki</p>
          <h1 class="admin-page-title text-surface-900 mb-3">Zróbmy to razem</h1>
          <p class="admin-page-lead text-surface-700 dark:text-surface-300 max-w-2xl">
            Każdy przewodnik prowadzi Cię przez prawdziwy panel — krok po kroku, na Twoich danych.
            Kończysz z gotową rzeczą: zapisanym grafikiem, dodaną usługą.
            Możesz je odtwarzać, ile razy chcesz.
          </p>
        </header>

        @if (groups().length === 0) {
          <section
            class="rounded-3xl border-2 border-dashed border-surface-200 dark:border-surface-100 p-12 text-center bg-surface-0 dark:bg-surface-50/60"
          >
            <div
              class="w-16 h-16 rounded-full bg-surface-100 dark:bg-surface-100 flex items-center justify-center mx-auto mb-4"
            >
              <i class="pi pi-compass text-2xl text-surface-400" aria-hidden="true"></i>
            </div>
            <h2 class="text-lg font-bold text-surface-900 mb-2">Brak przewodników</h2>
            <p class="text-surface-600 dark:text-surface-400 text-sm max-w-sm mx-auto">
              Dla Twojej roli nie przygotowaliśmy jeszcze żadnego przewodnika.
            </p>
          </section>
        }

        @for (group of groups(); track group.category) {
          <section class="space-y-3 mb-8">
            <h2 class="admin-section-label text-surface-500 dark:text-surface-400 px-1">
              {{ group.title }}
            </h2>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              @for (guide of group.guides; track guide.id) {
                <article
                  [attr.data-testid]="'guide-card-' + guide.id"
                  class="flex flex-col rounded-3xl border border-surface-200/90 dark:border-surface-100 bg-white/80 dark:bg-surface-50 p-5 sm:p-6 shadow-sm transition-all hover:shadow-md hover:border-primary/50"
                >
                  <div class="flex items-start justify-between gap-3 mb-4">
                    <div
                      class="w-12 h-12 sm:w-14 sm:h-14 rounded-2xl bg-primary/12 text-primary flex items-center justify-center"
                    >
                      <i [class]="guide.icon" class="text-xl sm:text-2xl" aria-hidden="true"></i>
                    </div>

                    @if (isCompleted(guide.id)) {
                      <span
                        class="inline-flex items-center gap-1.5 rounded-full bg-emerald-100 dark:bg-emerald-950/40 px-3 py-1 text-xs font-bold text-emerald-700 dark:text-emerald-300"
                        [attr.data-testid]="'guide-done-' + guide.id"
                      >
                        <i class="pi pi-check text-[10px]" aria-hidden="true"></i> Ukończono
                      </span>
                    }
                  </div>

                  <h3 class="text-lg sm:text-xl font-bold text-surface-900 mb-1">{{ guide.title }}</h3>
                  <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed mb-4 flex-1">
                    {{ guide.summary }}
                  </p>

                  <div class="flex items-center gap-2">
                    <button
                      type="button"
                      class="inline-flex items-center justify-center gap-2 rounded-full px-5 py-2.5 text-sm font-bold bg-primary text-white hover:opacity-90 transition-opacity disabled:opacity-50"
                      [disabled]="running()"
                      (click)="launch(guide)"
                    >
                      <i class="pi" [class.pi-play]="!isCompleted(guide.id)" [class.pi-replay]="isCompleted(guide.id)"></i>
                      {{ isCompleted(guide.id) ? 'Odtwórz' : 'Uruchom' }}
                    </button>

                    @if (isCompleted(guide.id)) {
                      <button
                        type="button"
                        class="inline-flex items-center justify-center rounded-full px-3 py-2 text-sm font-bold text-surface-600 dark:text-surface-300 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
                        (click)="resetProgress(guide)"
                        [attr.aria-label]="'Oznacz jako nieukończony: ' + guide.title"
                      >
                        Wyczyść
                      </button>
                    }
                  </div>
                </article>
              }
            </div>
          </section>
        }
      </div>
    </div>
  `,
})
export class GuidesPageComponent {
  private readonly auth = inject(AuthSessionService);
  private readonly guides = inject(GuideService);
  private readonly progress = inject(GuideProgressService);

  protected readonly running = this.guides.running;

  constructor() {
    void this.progress.load();
  }

  protected readonly groups = computed<GuideGroup[]>(() => {
    const available = guidesForRole(this.auth.currentRole());

    // Kolejność kategorii bierzemy ze słownika etykiet — jest tam ułożona „od czego zacząć".
    return (Object.keys(GUIDE_CATEGORY_LABELS) as GuideCategory[])
      .map((category) => ({
        category,
        title: GUIDE_CATEGORY_LABELS[category],
        guides: available.filter((guide) => guide.category === category),
      }))
      .filter((group) => group.guides.length > 0);
  });

  protected isCompleted(guideId: string): boolean {
    return this.progress.isCompleted(guideId);
  }

  protected launch(guide: GuideDef): void {
    void this.guides.start(guide);
  }

  protected resetProgress(guide: GuideDef): void {
    void this.progress.reset(guide.id);
  }
}
