import { Injectable, computed, inject, signal } from '@angular/core';
import { GuidesClient } from '@core/api/api-client';

/**
 * Stan „które przewodniki ten użytkownik już przeszedł".
 *
 * Trzymany w BAZIE, nie w localStorage — inaczej katalog `/admin/guides` odhaczałby kafelki
 * inaczej na telefonie i inaczej na laptopie tej samej osoby, a przewodnik dla nowego salonu
 * wyskakiwałby w kółko po każdym zalogowaniu w innej przeglądarce. To ta sama zasada, którą
 * kod kreatora onboardingu zapisał wprost: po utworzeniu konta stanem jest baza, nie przeglądarka.
 *
 * Zapis jest optymistyczny: użytkownik widzi „Ukończono" natychmiast po zamknięciu przewodnika,
 * a nieudany PUT cofa znacznik. Backend traktuje zapis jako idempotentny, więc nie sprawdzamy
 * stanu przed wysłaniem.
 */
@Injectable({ providedIn: 'root' })
export class GuideProgressService {
  private readonly client = inject(GuidesClient);

  private readonly completedIds = signal<ReadonlySet<string>>(new Set<string>());
  private readonly loaded = signal(false);
  private loadInFlight: Promise<void> | null = null;

  /** True dopiero po pierwszym udanym odczycie — katalog do tego czasu nie rysuje odznak. */
  readonly isLoaded = this.loaded.asReadonly();

  /**
   * Zbiór przejdzionych przewodników. Poza `isCompleted()` (pytanie o jeden) potrzebny tam, gdzie
   * postęp jest WEJŚCIEM do obliczeń — lista „Zacznij tutaj" odsiewa nim pozycje i znika, gdy
   * zejdzie do zera.
   */
  readonly completed = this.completedIds.asReadonly();

  readonly completedCount = computed(() => this.completedIds().size);

  isCompleted(guideId: string): boolean {
    return this.completedIds().has(guideId);
  }

  /**
   * Ładuje postęp z serwera. Bezpieczne do wielokrotnego wołania — równoległe wywołania
   * dzielą jedno żądanie, a kolejne po udanym odczycie są pomijane.
   */
  async load(): Promise<void> {
    if (this.loaded()) return;
    if (this.loadInFlight) return this.loadInFlight;

    this.loadInFlight = (async () => {
      try {
        const ids = await this.fetchCompletions();
        this.completedIds.set(new Set(ids));
        this.loaded.set(true);
      } catch {
        // Brak postępu nie może blokować panelu — katalog pokaże wszystko jako nieukończone.
        // Kolejne wejście spróbuje ponownie, bo `loaded` zostaje false.
      } finally {
        this.loadInFlight = null;
      }
    })();

    return this.loadInFlight;
  }

  /** Odnotowuje ukończenie. Optymistycznie, z cofnięciem przy błędzie sieci. */
  async markCompleted(guideId: string): Promise<void> {
    if (this.isCompleted(guideId)) return;

    this.mutate((set) => set.add(guideId));

    try {
      await this.client.markCompleted(guideId).toPromise();
    } catch {
      this.mutate((set) => set.delete(guideId));
    }
  }

  /** Cofa „ukończono" (akcja „zresetuj postęp" w katalogu). */
  async reset(guideId: string): Promise<void> {
    if (!this.isCompleted(guideId)) return;

    this.mutate((set) => set.delete(guideId));

    try {
      await this.client.resetCompletion(guideId).toPromise();
    } catch {
      this.mutate((set) => set.add(guideId));
    }
  }

  private async fetchCompletions(): Promise<string[]> {
    const ids = await this.client.getCompletions().toPromise();
    return ids ?? [];
  }

  private mutate(change: (set: Set<string>) => void): void {
    const next = new Set(this.completedIds());
    change(next);
    this.completedIds.set(next);
  }
}
