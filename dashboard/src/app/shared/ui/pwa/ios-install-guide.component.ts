import { ChangeDetectionStrategy, Component, DOCUMENT, inject, signal } from '@angular/core';
import { Dialog } from 'primeng/dialog';
import { PwaInstallService } from '@core/pwa/pwa-install.service';

/**
 * Animowany przewodnik „jak zainstalować aplikację" dla iOS/iPadOS Safari (brak natywnego promptu).
 * W 100% CSS/SVG — mock ekranu z pętlą: podświetlenie „Udostępnij" → wysunięcie arkusza z „Do ekranu
 * początkowego". Wskaźnik/toolbar zmienia pozycję zależnie od urządzenia (iPad = góra, iPhone = dół).
 * Widoczność steruje `PwaInstallService.iosGuideOpen`.
 */
@Component({
  selector: 'app-ios-install-guide',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Dialog],
  template: `
    <p-dialog
      [visible]="install.iosGuideOpen()"
      (visibleChange)="onVisibleChange($event)"
      [modal]="true"
      [dismissableMask]="true"
      [showHeader]="false"
      [draggable]="false"
      [resizable]="false"
      [style]="{ width: '22rem', maxWidth: '92vw' }"
      styleClass="!rounded-[28px] admin-glass-card !border-none overflow-hidden"
      contentStyleClass="!p-0"
    >
      <div class="guide" [class.guide--top]="share() === 'top'" data-testid="ios-install-guide">
        <header class="px-6 pt-6 text-center">
          <img src="icons/icon-192.png" alt="" class="mx-auto size-16 rounded-2xl shadow-md" />
          <h2 class="mt-3 text-lg font-black text-surface-900">Zainstaluj zapisz.me</h2>
          <p class="mt-1 text-sm leading-relaxed text-surface-500 dark:text-surface-400">
            @if (variant() === 'safari') {
              Dodaj do ekranu początkowego — szybszy dostęp i powiadomienia.
            } @else {
              Na iPhonie aplikację dodaje się do ekranu głównego tylko z przeglądarki Safari.
            }
          </p>
        </header>

        @if (variant() === 'safari') {
        <!-- Animowany mock: telefon/tablet z paskiem Safari i wysuwanym arkuszem udostępniania -->
        <div class="stage" aria-hidden="true">
          <div class="device">
            <div class="toolbar">
              <span class="url">zapisz.me</span>
              <span class="shareBtn">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M12 15V4" />
                  <path d="M8.5 7.5 12 4l3.5 3.5" />
                  <path d="M6 12v6a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1v-6" />
                </svg>
              </span>
            </div>

            <span class="pointer">
              <svg viewBox="0 0 24 24" fill="currentColor"><path d="M9 2a1.5 1.5 0 0 1 3 0v7.5l1.6-.5a3 3 0 0 1 3.8 2l.9 3.2a4 4 0 0 1-1 3.9l-1.8 1.8a3 3 0 0 1-2.1.9H10a3 3 0 0 1-2.6-1.5l-2.6-4.4a1.6 1.6 0 0 1 2.3-2.1L9 14V2z"/></svg>
            </span>

            <div class="sheet">
              <span class="grabber"></span>
              <div class="row">Kopiuj</div>
              <div class="row row--hl">
                <span>Do ekranu początkowego</span>
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="4" y="4" width="16" height="16" rx="4" />
                  <path d="M12 8.5v7M8.5 12h7" />
                </svg>
              </div>
            </div>
          </div>
        </div>

        <ol class="steps px-6 pt-1">
          <li>
            <span class="n">1</span>
            <span>
              Dotknij
              <svg class="inl" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M12 15V4" /><path d="M8.5 7.5 12 4l3.5 3.5" /><path d="M6 12v6a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1v-6" />
              </svg>
              „Udostępnij" {{ share() === 'top' ? 'w prawym górnym rogu' : 'na dolnym pasku' }}
            </span>
          </li>
          <li><span class="n">2</span><span>Wybierz „Do ekranu początkowego"</span></li>
          <li><span class="n">3</span><span>Potwierdź „Dodaj"</span></li>
        </ol>
        } @else {
        <!-- Inna przeglądarka/in-app na iOS — „Dodaj do ekranu głównego" jest tylko w Safari. -->
        <div class="mx-6 mt-4 flex items-start gap-3 rounded-2xl border border-primary/30 bg-primary/5 px-4 py-3 text-sm leading-relaxed text-surface-700 dark:text-surface-200">
          <i class="pi pi-apple text-primary text-lg mt-0.5"></i>
          <span>Ta przeglądarka nie pozwala dodać aplikacji do ekranu głównego. Otwórz stronę w <b>Safari</b>.</span>
        </div>
        <ol class="steps px-6 pt-3">
          <li><span class="n">1</span><span>Skopiuj adres tej strony (przycisk poniżej)</span></li>
          <li><span class="n">2</span><span>Otwórz <b>Safari</b> i wklej adres</span></li>
          <li><span class="n">3</span><span>Dotknij „Udostępnij" → „Do ekranu początkowego"</span></li>
        </ol>
        <div class="px-6 pt-3">
          <button type="button" (click)="copyLink()" data-testid="ios-guide-copy"
            class="w-full rounded-2xl border border-primary/40 bg-primary/10 px-4 py-2.5 text-sm font-bold text-primary hover:bg-primary/15 transition-colors">
            {{ copied() ? 'Skopiowano ✓' : 'Kopiuj adres strony' }}
          </button>
        </div>
        }

        <div class="px-6 pb-6 pt-3 flex flex-col gap-2">
          <button type="button" (click)="install.closeIosGuide()" data-testid="ios-guide-close"
            class="w-full rounded-2xl bg-primary px-4 py-3 text-sm font-bold text-primary-contrast hover:opacity-95 transition-opacity">
            Rozumiem
          </button>
          <button type="button" (click)="install.dismissIosGuideForever()"
            class="w-full rounded-2xl px-4 py-2 text-xs font-semibold text-surface-500 dark:text-surface-400 hover:text-surface-700 transition-colors">
            Nie pokazuj ponownie
          </button>
        </div>
      </div>
    </p-dialog>
  `,
  styles: [
    `
      .stage {
        margin: 0.75rem 1.5rem 0.25rem;
        display: grid;
        place-items: center;
      }
      .device {
        position: relative;
        width: 210px;
        height: 132px;
        border-radius: 20px;
        overflow: hidden;
        background: linear-gradient(160deg, color-mix(in srgb, var(--p-primary-color) 22%, transparent), transparent 60%),
          var(--p-surface-100, #f1f5f9);
        border: 1px solid color-mix(in srgb, var(--p-surface-500, #64748b) 25%, transparent);
      }
      /* Pasek Safari — domyślnie na dole (iPhone), na górze dla iPada (.guide--top) */
      .toolbar {
        position: absolute;
        left: 10px;
        right: 10px;
        bottom: 10px;
        height: 26px;
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 0 8px;
        border-radius: 13px;
        background: var(--p-surface-0, #fff);
        box-shadow: 0 1px 4px rgba(0, 0, 0, 0.12);
      }
      .guide--top .toolbar { bottom: auto; top: 10px; }
      .url {
        flex: 1;
        font-size: 11px;
        font-weight: 600;
        color: var(--p-surface-500, #64748b);
        text-align: center;
      }
      .shareBtn {
        display: grid;
        place-items: center;
        width: 20px;
        height: 20px;
        border-radius: 7px;
        color: var(--p-primary-color, #7c3aed);
        animation: ringPulse 4.5s ease-out infinite;
      }
      .shareBtn svg { width: 15px; height: 15px; }
      .pointer {
        position: absolute;
        right: 6px;
        bottom: 30px;
        width: 26px;
        height: 26px;
        color: var(--p-surface-900, #0f172a);
        filter: drop-shadow(0 2px 3px rgba(0, 0, 0, 0.25));
        animation: pointerPhase 4.5s ease-in-out infinite;
      }
      .guide--top .pointer { bottom: auto; top: 30px; }
      .sheet {
        position: absolute;
        left: 8px;
        right: 8px;
        bottom: 8px;
        padding: 8px;
        border-radius: 16px;
        background: var(--p-surface-0, #fff);
        box-shadow: 0 -6px 18px rgba(0, 0, 0, 0.16);
        transform: translateY(115%);
        opacity: 0;
        animation: sheetRise 4.5s ease-in-out infinite;
      }
      .grabber {
        display: block;
        width: 32px;
        height: 4px;
        margin: 0 auto 7px;
        border-radius: 2px;
        background: var(--p-surface-300, #cbd5e1);
      }
      .row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        padding: 7px 9px;
        border-radius: 10px;
        font-size: 11px;
        font-weight: 600;
        color: var(--p-surface-700, #334155);
      }
      .row svg { width: 15px; height: 15px; color: var(--p-primary-color, #7c3aed); }
      .row--hl { animation: rowHl 4.5s ease-in-out infinite; }

      .steps { display: flex; flex-direction: column; gap: 8px; list-style: none; margin: 0; }
      .steps li {
        display: flex;
        align-items: flex-start;
        gap: 10px;
        font-size: 13px;
        line-height: 1.35;
        color: var(--p-surface-700, #334155);
      }
      .steps .n {
        flex: none;
        display: grid;
        place-items: center;
        width: 20px;
        height: 20px;
        margin-top: 1px;
        border-radius: 999px;
        background: color-mix(in srgb, var(--p-primary-color) 16%, transparent);
        color: var(--p-primary-color, #7c3aed);
        font-size: 11px;
        font-weight: 800;
      }
      .steps .inl {
        display: inline-block;
        width: 14px;
        height: 14px;
        vertical-align: -3px;
        color: var(--p-primary-color, #7c3aed);
      }

      @keyframes sheetRise {
        0%, 44% { transform: translateY(115%); opacity: 0; }
        56%, 90% { transform: translateY(0); opacity: 1; }
        100% { transform: translateY(115%); opacity: 0; }
      }
      @keyframes pointerPhase {
        0% { opacity: 0; transform: translateY(7px); }
        10% { opacity: 1; }
        18% { transform: translateY(-2px); }
        28% { transform: translateY(7px); }
        38% { transform: translateY(-2px); }
        44% { opacity: 1; transform: translateY(7px); }
        50%, 100% { opacity: 0; }
      }
      @keyframes ringPulse {
        0% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--p-primary-color) 55%, transparent); }
        30% { box-shadow: 0 0 0 10px color-mix(in srgb, var(--p-primary-color) 0%, transparent); }
        44%, 100% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--p-primary-color) 0%, transparent); }
      }
      @keyframes rowHl {
        0%, 58% { background: transparent; }
        70%, 86% { background: color-mix(in srgb, var(--p-primary-color) 15%, transparent); }
        94%, 100% { background: transparent; }
      }
      @media (prefers-reduced-motion: reduce) {
        .shareBtn, .pointer, .sheet, .row--hl { animation: none; }
        .sheet { transform: translateY(0); opacity: 1; }
        .pointer { opacity: 1; }
      }
    `,
  ],
})
export class IosInstallGuideComponent {
  protected readonly install = inject(PwaInstallService);
  private readonly document = inject(DOCUMENT);
  protected readonly copied = signal(false);

  protected share(): 'top' | 'bottom' {
    return this.install.sharePosition();
  }

  protected variant(): 'safari' | 'other' {
    return this.install.iosVariant();
  }

  async copyLink(): Promise<void> {
    const win = this.document.defaultView;
    const url = win?.location?.href;
    if (!url) {
      return;
    }
    try {
      await win?.navigator?.clipboard?.writeText(url);
      this.copied.set(true);
      win?.setTimeout(() => this.copied.set(false), 2500);
    } catch {
      // Brak dostępu do schowka (np. bez HTTPS) — po cichu; użytkownik i tak widzi adres w pasku.
    }
  }

  onVisibleChange(open: boolean): void {
    // Zamknięcie przez tło/Esc/„x” traktujemy jak „na tę sesję” (nie zapamiętujemy na stałe).
    if (!open) {
      this.install.closeIosGuide();
    }
  }
}
