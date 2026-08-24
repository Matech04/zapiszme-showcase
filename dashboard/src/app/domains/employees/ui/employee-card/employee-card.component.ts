import { Component, computed, inject, input, output, signal } from "@angular/core";
import { Router } from "@angular/router";
import { AuthClient, EmployeeDto } from "@core/api/api-client";
import { ButtonModule } from "primeng/button";
import { MenuItem, MessageService } from "primeng/api";
import { TieredMenuModule } from "primeng/tieredmenu";

@Component({
  selector: 'app-employee-card',
  standalone: true,
  imports: [ButtonModule, TieredMenuModule],
  template: `
    <div
      class="bg-white/85 dark:bg-surface-50/55 rounded-3xl p-6 shadow-[0_16px_34px_-24px_rgba(15,23,42,0.55)] hover:shadow-[0_22px_44px_-26px_rgba(15,23,42,0.6)] transition-all duration-300 border border-white/90 dark:border-surface-200/70 flex flex-col gap-6 h-full"
    >
      <div class="flex items-start justify-between">
        <div class="flex items-center gap-4">
          <div
            class="w-12 h-12 rounded-full bg-primary/10 dark:bg-primary/20 flex items-center justify-center border border-primary/20"
          >
            <i class="pi pi-user text-primary text-xl"></i>
          </div>
          <div class="flex flex-col">
            <span class="text-[10px] font-bold text-surface-500 dark:text-surface-400 uppercase tracking-[0.2em] mb-1"
              >{{ employee().specialization || 'Specjalista' }}</span
            >
            <h3 data-testid="employee-name" class="text-lg font-black tracking-tight text-surface-900 leading-tight">
              {{ employee().firstName }} {{ employee().lastName }}
            </h3>
            @if (roleLabel()) {
              <span data-testid="employee-role" class="mt-1 text-[10px] font-extrabold text-surface-500 dark:text-surface-400 uppercase tracking-widest">
                {{ roleLabel() }}
              </span>
            }
          </div>
        </div>
        <div class="flex items-center gap-2">
          @if (invitePending()) {
            <div class="bg-amber-50 dark:bg-amber-900/30 border border-amber-100 dark:border-amber-800 px-3 py-1.5 rounded-full flex items-center gap-1.5 shadow-sm">
              <i class="pi pi-clock text-[10px] text-amber-600 dark:text-amber-300"></i>
              <span class="text-[10px] font-extrabold text-amber-600 dark:text-amber-300 uppercase tracking-widest">Zaproszony</span>
            </div>
          } @else {
            <div class="bg-primary-50 dark:bg-primary-900/30 border border-primary-100 dark:border-primary-800 px-3 py-1.5 rounded-full flex items-center gap-1.5 shadow-sm">
              <span class="w-1.5 h-1.5 rounded-full bg-primary-500 animate-pulse"></span>
              <span class="text-[10px] font-extrabold text-primary-600 dark:text-primary-300 uppercase tracking-widest">Aktywny</span>
            </div>
          }

          @if (canManage()) {
            <p-button
              icon="pi pi-ellipsis-v"
              [text]="true"
              [plain]="true"
              [rounded]="true"
              (onClick)="menu.toggle($event)"
              styleClass="w-8 h-8 p-0 text-surface-500 hover:text-surface-900 dark:hover:text-surface-900 hover:bg-surface-100 dark:hover:bg-surface-100 transition-colors" />

            <p-tieredmenu #menu [popup]="true" [model]="menuItems()" appendTo="body" />
          }
        </div>
      </div>

      <div class="flex flex-col gap-3">
        <div class="flex items-center gap-3 text-surface-600 dark:text-surface-400">
          <i class="pi pi-envelope text-sm"></i>
          <span data-testid="employee-email" class="text-sm font-sans truncate">{{ employee().email || 'Brak adresu email' }}</span>
        </div>

        @if (invitePending() && canManage()) {
          <p-button
            data-testid="button-resend-invite"
            label="Wyślij zaproszenie ponownie"
            icon="pi pi-send"
            size="small"
            [text]="true"
            [loading]="resending()"
            (onClick)="resendInvite()"
            styleClass="p-button-sm w-full justify-center text-amber-600 dark:text-amber-300 hover:bg-amber-50 dark:hover:bg-amber-900/20"
          />
        }
      </div>

      <div class="mt-auto pt-6 border-t border-surface-200/70 dark:border-surface-200/70 flex justify-between items-center gap-2">
        @if (canManage()) {
          <p-button
            data-testid="button-manage"
            label="Zarządzaj"
            icon="pi pi-cog"
            size="small"
            [text]="false"
            [raised]="false"
            (click)="manage.emit(employee().id)"
            class="font-bold uppercase tracking-widest"
            styleClass="p-button-sm px-4"
          />
        } @else {
          <span></span>
        }

        <div class="flex gap-1">
          <p-button 
            icon="pi pi-calendar" 
            [text]="true" 
            [rounded]="true" 
            severity="secondary" 
            title="Plan dnia — kalendarz wizyt"
            (onClick)="openEmployeeSchedule()"
          />
        </div>
      </div>
    </div>
  `
})
export class EmployeeCardComponent {
  private router = inject(Router);
  private readonly authClient = inject(AuthClient);
  private readonly messageService = inject(MessageService);

  /** Trwa ponowna wysyłka linku zaproszenia (spinner na przycisku). */
  protected readonly resending = signal(false);

  employee = input.required<EmployeeDto>();
  /** Akcje zarządcze (menu edit/usługi/urlopy/usuń + „Zarządzaj"). False = roster tylko-do-odczytu (pracownik). */
  canManage = input<boolean>(true);
  manage = output<string | undefined>();
  edit = output<string>();
  delete = output<string>();
  openLeaveDashboard = output<string | undefined>();

  /** Etykieta roli konta (z Identity). null = pracownik bez konta logowania (zasób kalendarza). */
  roleLabel = computed<string | null>(() => {
    switch (this.employee().role) {
      case 'Owner':
        return 'Właściciel';
      case 'Manager':
        return 'Manager';
      case 'Employee':
        return 'Pracownik';
      case 'Kiosk':
        return 'Recepcja';
      default:
        return null;
    }
  });

  /** Zaproszenie wysłane, konto jeszcze nieaktywowane. */
  invitePending = computed(() => this.employee().invitePending === true);

  openEmployeeSchedule(): void {
    const id = this.employee().id;
    if (id) {
      void this.router.navigate(['/admin', 'schedule', id]);
    }
  }

  /** Ponawia wysyłkę linku aktywacyjnego dla niepotwierdzonego konta. Błędy pokazuje errorInterceptor. */
  resendInvite(): void {
    const id = this.employee().id;
    if (!id || this.resending()) {
      return;
    }
    this.resending.set(true);
    this.authClient.resendEmployeeInvite(id).subscribe({
      next: () => {
        this.resending.set(false);
        this.messageService.add({
          severity: 'success',
          summary: 'Zaproszenie wysłane ponownie',
          detail: `Nowy link (ważny 24 h) poszedł na ${this.employee().email}.`,
          life: 6000,
        });
      },
      error: () => this.resending.set(false),
    });
  }

  menuItems = computed<MenuItem[]>(() => [
    {
      label: 'Edytuj',
      icon: 'pi pi-pencil',
      command: () => this.edit.emit(this.employee().id!)
    },
    {
      label: 'Usługi',
      icon: 'pi pi-briefcase',
      command: () => {
        const id = this.employee().id;
        if (id) {
          void this.router.navigate(['/admin', 'resources', 'employees', id, 'services']);
        }
      },
    },
    {
      label: 'Urlopy',
      icon: 'pi pi-calendar-minus',
      command: () => this.openLeaveDashboard.emit(this.employee().id)
    },
    {
      label: 'Usuń',
      icon: 'pi pi-trash',
      styleClass: 'text-red-500',
      command: () => this.delete.emit(this.employee().id!)
    }
  ]);
}
