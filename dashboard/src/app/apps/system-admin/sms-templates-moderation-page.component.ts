import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  AdminSmsTemplatesClient,
  PendingSmsTemplateDto,
} from '@core/api/api-client';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { Textarea } from 'primeng/textarea';

/**
 * Panel admin: moderacja custom szablonów SMS (cross-tenant). Tylko `systemAdmin` (route guard).
 * API: /api/admin/sms-templates/pending (GET), /{id}/approve, /{id}/reject.
 */
@Component({
  selector: 'app-sms-templates-moderation-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, TableModule, ButtonModule, DialogModule, ConfirmDialogModule,
    Textarea, InputTextModule,
  ],
  providers: [ConfirmationService],
  template: `
    <p-confirmDialog />

    <div class="p-6 lg:p-10 max-w-6xl mx-auto">
      <div class="mb-8">
        <h1 class="text-3xl font-black tracking-tight text-surface-900">Moderacja szablonów SMS</h1>
        <p class="mt-1 text-sm text-surface-500 dark:text-surface-400">
          {{ pending.value()?.length ?? 0 }} szablonów oczekuje na akceptację.
        </p>
      </div>

      <div class="rounded-3xl border border-surface-200 dark:border-surface-200 overflow-hidden bg-white dark:bg-surface-50 shadow-sm">
        <p-table [value]="pending.value() ?? []" [loading]="pending.isLoading()" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>Salon</th>
              <th>Typ</th>
              <th>Treść</th>
              <th>Podgląd (przykład)</th>
              <th style="width: 14rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td class="font-semibold">{{ row.tenantName }}</td>
              <td>{{ row.typeLabel }}</td>
              <td class="max-w-xs whitespace-pre-wrap text-sm">{{ row.pendingBody }}</td>
              <td class="max-w-xs text-sm text-surface-500">{{ row.pendingPreview }}</td>
              <td>
                <div class="flex gap-2">
                  <p-button
                    label="Zatwierdź"
                    icon="pi pi-check"
                    size="small"
                    severity="success"
                    [loading]="busyId() === row.templateId"
                    (onClick)="approve(row)"
                  />
                  <p-button
                    label="Odrzuć"
                    icon="pi pi-times"
                    size="small"
                    severity="danger"
                    [outlined]="true"
                    [disabled]="busyId() !== null"
                    (onClick)="openReject(row)"
                  />
                </div>
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="5" class="p-6 text-center text-surface-400">Brak szablonów do moderacji.</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    </div>

    <p-dialog [(visible)]="rejectVisible" header="Odrzuć szablon" [modal]="true" [style]="{ width: '32rem' }">
      <p class="mb-2 text-sm text-surface-600">
        Podaj powód odrzucenia (zobaczy go salon). Poprzednia zatwierdzona treść pozostanie aktywna.
      </p>
      <textarea pTextarea class="w-full" rows="3" [(ngModel)]="rejectReason"></textarea>
      <ng-template pTemplate="footer">
        <p-button label="Anuluj" [text]="true" (onClick)="rejectVisible.set(false)" />
        <p-button label="Odrzuć" severity="danger" [loading]="busyId() !== null" (onClick)="confirmReject()" />
      </ng-template>
    </p-dialog>
  `,
})
export class SmsTemplatesModerationPageComponent {
  private readonly client = inject(AdminSmsTemplatesClient);
  private readonly toast = inject(MessageService);

  protected readonly pending = rxResource({
    stream: () => this.client.pending(),
  });

  protected readonly busyId = signal<string | null>(null);
  protected readonly rejectVisible = signal(false);
  protected rejectReason = '';
  private rejectTarget: PendingSmsTemplateDto | null = null;

  protected approve(row: PendingSmsTemplateDto): void {
    this.busyId.set(row.templateId ?? null);
    this.client.approve(row.templateId!).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Zatwierdzono szablon' });
        this.busyId.set(null);
        this.pending.reload();
      },
      error: () => this.busyId.set(null),
    });
  }

  protected openReject(row: PendingSmsTemplateDto): void {
    this.rejectTarget = row;
    this.rejectReason = '';
    this.rejectVisible.set(true);
  }

  protected confirmReject(): void {
    if (!this.rejectTarget) {
      return;
    }
    this.busyId.set(this.rejectTarget.templateId ?? null);
    this.client.reject(this.rejectTarget.templateId!, { reason: this.rejectReason.trim() || undefined }).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Odrzucono szablon' });
        this.busyId.set(null);
        this.rejectVisible.set(false);
        this.pending.reload();
      },
      error: () => this.busyId.set(null),
    });
  }
}
