import { Component, computed, input, output } from '@angular/core';
import { CustomerDto } from '@core/api/api-client';
import { ButtonModule } from 'primeng/button';
import { MenuItem } from 'primeng/api';
import { TieredMenuModule } from 'primeng/tieredmenu';
import { Tooltip } from 'primeng/tooltip';

@Component({
  selector: 'app-customer-card',
  standalone: true,
  imports: [ButtonModule, TieredMenuModule, Tooltip],
  template: `
    <div
      class="bg-white/85 dark:bg-surface-50/55 rounded-3xl p-6 shadow-[0_16px_34px_-24px_rgba(15,23,42,0.55)] hover:shadow-[0_22px_44px_-26px_rgba(15,23,42,0.6)] transition-all duration-300 border border-white/90 dark:border-surface-200/70 flex flex-col gap-6 h-full"
    >
      <div class="flex items-start justify-between gap-3">
        <div class="flex items-center gap-4 min-w-0">
          <div
            class="w-12 h-12 rounded-full bg-primary/10 dark:bg-primary/20 flex items-center justify-center border border-primary/20 shrink-0"
          >
            <i class="pi pi-id-card text-primary text-xl"></i>
          </div>
          <div class="flex flex-col min-w-0">
            <span
              class="text-[10px] font-bold text-surface-500 dark:text-surface-400 uppercase tracking-[0.2em] mb-1"
              >Klient</span
            >
            <h3
              class="text-lg font-black tracking-tight text-surface-900 leading-tight truncate"
            >
              {{ customer().firstName }} {{ customer().lastName }}
            </h3>
          </div>
        </div>
        <div class="flex items-center gap-2 shrink-0">
          @if (customer().isWhitelisted) {
            <span
              class="inline-flex items-center justify-center w-7 h-7 rounded-full bg-emerald-100 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 border border-emerald-300/60"
              pTooltip="Klient na whitelist (może rezerwować w trybie tylko zaproszeni)"
              tooltipPosition="left"
            >
              <i class="pi pi-check-circle text-xs"></i>
            </span>
          }
          <p-button
            icon="pi pi-ellipsis-v"
            [text]="true"
            [plain]="true"
            [rounded]="true"
            (onClick)="menu.toggle($event)"
            styleClass="w-8 h-8 p-0 text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors"
          />

          <p-tieredmenu #menu [popup]="true" [model]="menuItems()" appendTo="body" />
        </div>
      </div>

      <div class="flex flex-col gap-3">
        <div class="flex items-center gap-3 text-surface-600 dark:text-surface-400 min-w-0">
          <i class="pi pi-envelope text-sm shrink-0"></i>
          <span class="text-sm font-sans truncate">{{ customer().email || '—' }}</span>
        </div>
        <div class="flex items-center gap-3 text-surface-600 dark:text-surface-400 min-w-0">
          <i class="pi pi-phone text-sm shrink-0"></i>
          <span class="text-sm font-sans truncate">{{ customer().phoneNumber || '—' }}</span>
        </div>
      </div>

      <div class="mt-auto pt-6 border-t border-surface-200/70 dark:border-surface-200/70 flex justify-start items-center">
        <p-button
          label="Profil"
          icon="pi pi-user"
          size="small"
          [text]="false"
          (onClick)="openProfile.emit(customer().id)"
          class="font-bold uppercase tracking-widest"
          styleClass="p-button-sm px-4"
        />
      </div>
    </div>
  `,
})
export class CustomerCardComponent {
  customer = input.required<CustomerDto>();
  selectable = input<boolean>(false);
  selected = input<boolean>(false);
  /** Czy pokazywać akcje zarządcze (whitelist, usuń) — tylko właściciel. Pracownik: tylko Profil/Edytuj. */
  /** Usunięcie klienta — API `BusinessManagement` (tylko właścicielka). */
  canManage = input<boolean>(true);
  /**
   * Whitelist — API `StaffManagement` (właścicielka i manager). Szersze niż `canManage`.
   * Default fail-closed: konsument, który zapomni podać, nie pokaże akcji zamiast pokazać ją
   * pracownikowi, który dostałby 403.
   */
  canManageWhitelist = input<boolean>(false);
  openProfile = output<string | undefined>();
  edit = output<string>();
  delete = output<string>();
  toggleWhitelist = output<{ id: string; whitelisted: boolean }>();
  toggleSelection = output<string>();

  menuItems = computed<MenuItem[]>(() => {
    const c = this.customer();
    const items: MenuItem[] = [
      {
        label: 'Profil',
        icon: 'pi pi-eye',
        command: () => this.openProfile.emit(c.id),
      },
      {
        label: 'Edytuj',
        icon: 'pi pi-pencil',
        command: () => this.edit.emit(c.id!),
      },
    ];
    if (this.canManageWhitelist()) {
      items.push({
        label: c.isWhitelisted ? 'Usuń z whitelisty' : 'Dodaj do whitelisty',
        icon: c.isWhitelisted ? 'pi pi-user-minus' : 'pi pi-user-plus',
        command: () =>
          this.toggleWhitelist.emit({ id: c.id!, whitelisted: !c.isWhitelisted }),
      });
    }
    if (this.canManage()) {
      items.push({
        label: 'Usuń',
        icon: 'pi pi-trash',
        styleClass: 'text-red-500',
        command: () => this.delete.emit(c.id!),
      });
    }
    return items;
  });
}
