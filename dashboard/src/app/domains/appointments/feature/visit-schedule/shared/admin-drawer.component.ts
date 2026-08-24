import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  Renderer2,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Drawer } from 'primeng/drawer';
import { BodyScrollLockService } from '@core/services/body-scroll-lock.service';

/**
 * Bazowy wrapper dla right-drawer (desktop) / bottom-sheet (mobile) używany w formularzach.
 *
 * Mechanika UX identyczna z `appointment-detail-sheet`:
 *  - `modal=false` — brak zaciemniającego backdropu PrimeNG
 *  - `dismissible=false` — PrimeNG nie zamyka panelu klikiem poza nim
 *  - ręczna blokada `body { overflow: hidden }` gdy otwarty
 *  - ręczny listener `pointerdown` na dokumencie → zamknięcie po kliknięciu poza drawerem
 *    (z wyjątkiem kliknięć w PrimeNG overlaye: Select, DatePicker, Toast, itp.)
 *  - `setTimeout(0)` przy bindowaniu listenera, żeby klik otwierający nie od razu zamknął
 *
 * Użycie:
 * ```html
 * <app-admin-drawer [isOpen]="isOpen()" [isDesktop]="isDesktop()" title="Tytuł"
 *                   [closeable]="!isSubmitting()" (closeRequested)="onClose()">
 *   <div drawer-body> ... </div>
 *   <div drawer-footer> ... </div>
 * </app-admin-drawer>
 * ```
 */
@Component({
  selector: 'app-admin-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, Drawer],
  template: `
    <p-drawer
      [visible]="isOpen()"
      (visibleChange)="onVisibleChange($event)"
      [position]="isDesktop() ? 'right' : 'bottom'"
      [modal]="false"
      [showCloseIcon]="false"
      [dismissible]="false"
      [styleClass]="drawerClass()"
      [ariaCloseLabel]="'Zamknij'"
    >
      <header class="px-5 pt-5 pb-4 border-b border-surface-200/70 dark:border-surface-200/70 shrink-0">
        @if (!isDesktop()) {
          <div class="flex justify-center pb-2 -mt-2" aria-hidden="true">
            <span class="block w-12 h-1.5 rounded-full bg-surface-300 dark:bg-surface-600"></span>
          </div>
        }
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0">
            @if (label()) {
              <p class="admin-section-label">{{ label() }}</p>
            }
            <h2 class="text-xl font-black text-surface-900 leading-tight">
              {{ title() }}
            </h2>
          </div>
          <button
            type="button"
            (click)="onCloseClick()"
            [disabled]="!closeable()"
            aria-label="Zamknij"
            class="w-8 h-8 shrink-0 rounded-full grid place-items-center text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors disabled:opacity-40"
          >
            <i class="pi pi-times text-sm" aria-hidden="true"></i>
          </button>
        </div>
      </header>

      <div class="flex-1 min-h-0 overflow-y-auto overscroll-contain px-5 py-5 flex flex-col gap-5">
        <ng-content select="[drawer-body]" />
      </div>

      <footer class="px-5 py-4 border-t border-surface-200/70 dark:border-surface-200/70 shrink-0 pb-[max(1rem,env(safe-area-inset-bottom))] sm:pb-4">
        <ng-content select="[drawer-footer]" />
      </footer>
    </p-drawer>
  `,
})
export class AdminDrawerComponent {
  readonly isOpen = input<boolean>(false);
  readonly isDesktop = input<boolean>(false);
  readonly title = input<string>('');
  /** Mały napis nad tytułem (np. "Kalendarz"). */
  readonly label = input<string>('');
  /** Gdy false — przycisk X i klik poza drawerem są ignorowane (np. w trakcie submitu). */
  readonly closeable = input<boolean>(true);

  readonly closeRequested = output<void>();

  private readonly renderer = inject(Renderer2);
  private readonly destroyRef = inject(DestroyRef);
  private readonly scrollLock = inject(BodyScrollLockService);

  private clickOutsideUnsubscribe?: () => void;
  private bodyLocked = false;

  /**
   * `admin-drawer-shell` to klasa-marker (patrz `styles.css`). PrimeNG v21 nie ma
   * `contentStyleClass`, a wewnętrzny `.p-drawer-content` (wrapper wokół naszej
   * treści) domyślnie sam scrolluje i nie jest flex-kolumną — przez co `flex-1`
   * na body nie działało, a dotyk w środku panelu nie przewijał (scroll łapał
   * tylko krawędź). Globalny CSS pod tym markerem robi z `.p-drawer-content`
   * flex-kolumnę bez własnego scrolla, dzięki czemu nasz `<div drawer-body>`
   * staje się JEDYNYM, pełnoszerokościowym kontenerem przewijania.
   */
  protected readonly drawerClass = computed(() => {
    const base = 'admin-drawer-shell !bg-surface-0 dark:!bg-surface-50 !border-l-0 !border-t-0';
    return this.isDesktop()
      ? `${base} !w-[480px] !max-w-[92vw] !border-l !border-surface-200 dark:!border-surface-200`
      : `${base} !h-auto !max-h-[92dvh] !rounded-t-3xl shadow-2xl`;
  });

  constructor() {
    effect(() => {
      if (this.isOpen()) {
        this.lockBodyScroll();
        // setTimeout(0): klik otwierający drawer propaguje do document — bez opóźnienia
        // listener załapałby go i natychmiast zamknął panel.
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

  protected onCloseClick(): void {
    if (!this.closeable()) return;
    this.closeRequested.emit();
  }

  protected onVisibleChange(next: boolean): void {
    if (!next && this.isOpen() && this.closeable()) {
      this.closeRequested.emit();
    }
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
        if (!this.isOpen() || !this.closeable()) return;
        const target = ev.target as HTMLElement | null;
        if (!target || this.isInsideOverlay(target)) return;
        this.closeRequested.emit();
      },
    );
  }

  private unbindClickOutside(): void {
    this.clickOutsideUnsubscribe?.();
    this.clickOutsideUnsubscribe = undefined;
  }

  private isInsideOverlay(target: HTMLElement): boolean {
    return !!(
      target.closest('.p-drawer') ||
      target.closest('.p-dialog') ||
      target.closest('.p-confirm-dialog') ||
      target.closest('.p-overlay-mask') ||
      target.closest('.p-overlay') ||
      target.closest('.p-datepicker-panel') ||
      target.closest('.p-toast') ||
      target.closest('.p-tooltip')
    );
  }
}
