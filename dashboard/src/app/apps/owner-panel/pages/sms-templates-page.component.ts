import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, debounceTime, map, mergeMap, of, Subject } from 'rxjs';
import {
  NotificationType,
  SmsTemplateDto,
  SmsTemplateStatus,
  SmsTemplatesClient,
} from '@core/api/api-client';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { Textarea } from 'primeng/textarea';
import { gsmRequiresUnicode, smsLimit } from './sms-gsm.util';

/**
 * Panel salonu: personalizacja treści SMS do klienta. Edycja trafia do kolejki akceptacji admina —
 * do czasu akceptacji wysyłany jest poprzedni zatwierdzony lub domyślny szablon.
 * API: GET /api/sms-templates, PUT /api/sms-templates/{notificationType}.
 */
@Component({
  selector: 'app-sms-templates-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Textarea, ButtonModule, TagModule],
  template: `
    <div class="min-h-screen px-4 pb-12 pt-4 sm:px-8 sm:pt-8">
      <div class="max-w-3xl mx-auto space-y-6">
        <header class="admin-glass-card rounded-4xl px-6 py-8 sm:px-8 sm:py-9">
          <span class="admin-section-label text-primary mb-3 block">
            Panel właściciela
          </span>
          <h1
            class="text-3xl sm:text-4xl font-black tracking-tight text-surface-900 leading-tight mb-2"
          >
            Szablony SMS
          </h1>
          <p class="text-surface-700 dark:text-surface-300">
            Spersonalizuj treść SMS-ów wysyłanych do klientów. Zmiany trafiają do akceptacji
            administratora — do tego czasu wysyłana jest poprzednia zatwierdzona lub domyślna treść.
          </p>
        </header>

      @if (templates.isLoading()) {
        <p class="text-surface-500">Wczytywanie…</p>
      }

      <div class="space-y-5">
        @for (t of templates.value() ?? []; track t.notificationType) {
          <div data-tour="sms-template-card" class="rounded-3xl border border-surface-200 dark:border-surface-200 bg-white dark:bg-surface-50 p-5 shadow-sm">
            <div class="flex items-start justify-between gap-3">
              <div>
                <h2 class="text-lg font-bold text-surface-900">{{ t.typeLabel }}</h2>
                <p class="mt-0.5 text-xs text-surface-400">Domyślnie: {{ t.defaultPreview }}</p>
              </div>
              <p-tag
                [value]="statusLabel(t.status)"
                [severity]="statusSeverity(t.status)"
              />
            </div>

            @if (t.status === Status.Rejected && t.rejectionReason) {
              <p class="mt-2 text-sm text-red-600">Odrzucono: {{ t.rejectionReason }}</p>
            }

            <textarea
              pTextarea
              class="mt-3 w-full"
              rows="3"
              [ngModel]="draft(t)"
              (ngModelChange)="setDraft(t, $event)"
              placeholder="Wpisz własną treść lub zostaw puste, by użyć domyślnej…"
            ></textarea>

            <div class="mt-1 flex items-center justify-between text-xs">
              <span [class.text-red-600]="overLimit(t)" class="text-surface-500">
                {{ charCount(t) }}/{{ limit(t) }}
                @if (isUnicode(t)) {
                  · Unicode (polskie znaki / emoji)
                } @else {
                  · GSM-7
                }
              </span>
              <span class="text-surface-400">
                Znaczniki: {{ placeholderHint(t) }}
              </span>
            </div>

            @let pv = previewText(t);
            <div
              class="mt-3 rounded-2xl border border-surface-200 dark:border-surface-200 bg-surface-50 dark:bg-surface-100/40 p-3"
            >
              <p class="mb-1.5 text-[11px] font-bold uppercase tracking-wider text-surface-400">
                <i class="pi pi-mobile mr-1"></i>Podgląd — tak zobaczy klient
              </p>
              @if (pv.text) {
                <p
                  [attr.data-testid]="'sms-preview-' + t.notificationType"
                  class="whitespace-pre-line break-words text-sm text-surface-800 dark:text-surface-200"
                >{{ pv.text }}</p>
                @if (pv.isDefault) {
                  <p class="mt-1.5 text-[11px] text-surface-400">
                    Treść domyślna (pole puste — klient dostanie standardowy SMS).
                  </p>
                } @else {
                  <p
                    class="mt-1.5 text-[11px]"
                    [class.text-amber-600]="pv.unicode"
                    [class.text-surface-400]="!pv.unicode"
                  >
                    {{ pv.text.length }} znaków ·
                    @if (pv.unicode) {
                      Unicode (polskie znaki / emoji) — limit 70, droższy SMS
                    } @else {
                      GSM-7 — limit 140
                    }
                  </p>
                }
              } @else {
                <p class="text-sm text-surface-400">…</p>
              }
            </div>

            <div class="mt-3 flex justify-end">
              <p-button
                data-tour="sms-submit"
                label="Wyślij do akceptacji"
                icon="pi pi-send"
                size="small"
                [disabled]="!canSubmit(t)"
                [loading]="savingType() === t.notificationType"
                (onClick)="submit(t)"
              />
            </div>
          </div>
        }
        </div>
      </div>
    </div>
  `,
})
export class SmsTemplatesPageComponent {
  private readonly client = inject(SmsTemplatesClient);
  private readonly toast = inject(MessageService);

  protected readonly Status = SmsTemplateStatus;

  protected readonly templates = rxResource({
    stream: () => this.client.get(),
  });

  // Lokalne robocze treści per typ (klucz = notificationType).
  private readonly drafts = signal<Record<number, string>>({});
  protected readonly savingType = signal<NotificationType | null>(null);

  // Wyrenderowany podgląd (ASCII, jak w SMS) per typ — z backendu (ta sama ścieżka co wysyłka).
  private readonly previews = signal<Record<number, string>>({});
  private readonly previewRequests = new Subject<{ type: NotificationType; body: string }>();

  constructor() {
    // Live podgląd: debounce na pisaniu; backend renderuje finalną treść (placeholdery +
    // transliteracja ASCII + clip), więc owner widzi DOKŁADNIE to, co dostanie klient.
    this.previewRequests
      .pipe(
        debounceTime(300),
        mergeMap((req) =>
          this.client.preview(req.type, { body: req.body }).pipe(
            map((r) => ({ type: req.type, preview: r.preview ?? '' })),
            catchError(() => of({ type: req.type, preview: '' })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ type, preview }) =>
        this.previews.update((p) => ({ ...p, [type as number]: preview })),
      );

    // Po wczytaniu/reloadzie listy zasiej podgląd dla niepustych zapisanych treści.
    effect(() => {
      const list = this.templates.value() ?? [];
      untracked(() => {
        for (const t of list) {
          const body = this.draft(t).trim();
          if (body.length > 0 && t.notificationType != null) {
            this.previewRequests.next({ type: t.notificationType, body });
          }
        }
      });
    });
  }

  protected draft(t: SmsTemplateDto): string {
    const local = this.drafts()[t.notificationType as number];
    if (local !== undefined) {
      return local;
    }
    // Pokaż oczekującą (jeśli jest) albo zatwierdzoną treść.
    return t.pendingBody ?? t.approvedBody ?? '';
  }

  protected setDraft(t: SmsTemplateDto, value: string): void {
    this.drafts.update((d) => ({ ...d, [t.notificationType as number]: value }));
    const body = value.trim();
    if (body.length > 0 && t.notificationType != null) {
      this.previewRequests.next({ type: t.notificationType, body });
    } else {
      // Pusta treść → klient dostaje domyślny szablon (pokazujemy DefaultPreview).
      this.previews.update((p) => {
        const copy = { ...p };
        delete copy[t.notificationType as number];
        return copy;
      });
    }
  }

  /** Co realnie dostanie klient: custom podgląd (treść niepusta) albo domyślny szablon. */
  protected previewText(t: SmsTemplateDto): { text: string; isDefault: boolean; unicode: boolean } {
    const body = this.draft(t).trim();
    const text =
      body.length === 0
        ? (t.defaultPreview ?? '')
        : (this.previews()[t.notificationType as number] ?? '');
    return { text, isDefault: body.length === 0, unicode: gsmRequiresUnicode(text) };
  }

  protected charCount(t: SmsTemplateDto): number {
    return this.draft(t).length;
  }

  protected isUnicode(t: SmsTemplateDto): boolean {
    return gsmRequiresUnicode(this.draft(t));
  }

  protected limit(t: SmsTemplateDto): number {
    return smsLimit(this.draft(t));
  }

  protected overLimit(t: SmsTemplateDto): boolean {
    return this.charCount(t) > this.limit(t);
  }

  protected canSubmit(t: SmsTemplateDto): boolean {
    const body = this.draft(t).trim();
    return body.length > 0 && !this.overLimit(t) && this.savingType() === null;
  }

  protected placeholderHint(t: SmsTemplateDto): string {
    return (t.allowedPlaceholders ?? []).map((p) => `{${p}}`).join(' ');
  }

  protected statusLabel(status: SmsTemplateStatus | undefined): string {
    switch (status) {
      case SmsTemplateStatus.PendingApproval:
        return 'Oczekuje';
      case SmsTemplateStatus.Approved:
        return 'Zaakceptowany';
      case SmsTemplateStatus.Rejected:
        return 'Odrzucony';
      default:
        return 'Domyślny';
    }
  }

  protected statusSeverity(
    status: SmsTemplateStatus | undefined,
  ): 'success' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case SmsTemplateStatus.PendingApproval:
        return 'warn';
      case SmsTemplateStatus.Approved:
        return 'success';
      case SmsTemplateStatus.Rejected:
        return 'danger';
      default:
        return 'secondary';
    }
  }

  protected submit(t: SmsTemplateDto): void {
    if (!this.canSubmit(t)) {
      return;
    }
    this.savingType.set(t.notificationType ?? null);
    this.client.submit(t.notificationType!, { body: this.draft(t).trim() }).subscribe({
      next: () => {
        this.toast.add({
          severity: 'success',
          summary: 'Wysłano do akceptacji',
          detail: 'Szablon czeka na zatwierdzenie przez administratora.',
        });
        this.savingType.set(null);
        // Wyczyść lokalny draft, by odświeżona lista pokazała stan z serwera.
        this.drafts.update((d) => {
          const copy = { ...d };
          delete copy[t.notificationType as number];
          return copy;
        });
        this.templates.reload();
      },
      error: () => {
        // errorInterceptor pokaże PL toast wg kodu błędu.
        this.savingType.set(null);
      },
    });
  }
}
