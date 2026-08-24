import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  AdminCreateSalonRequest,
  AdminCreateSalonStaffMember,
  AuthClient,
  SubscriptionStatus,
  TenantAdminDto,
  TenantEmployeeDto,
  TenantsClient,
} from '@core/api/api-client';
import { ImpersonationService } from '@core/auth/impersonation.service';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { Select } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { CheckboxModule } from 'primeng/checkbox';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { DatePickerModule } from 'primeng/datepicker';

@Component({
  selector: 'app-tenants-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TableModule, ButtonModule, TagModule, DialogModule,
    Select, InputTextModule, InputNumberModule, CheckboxModule,
    FormsModule, DatePickerModule,
  ],
  template: `

    <div class="p-6 lg:p-10 max-w-7xl mx-auto">
      <div class="mb-8 flex items-start justify-between gap-4">
        <div>
          <h1 class="text-3xl font-black tracking-tight text-surface-900">Salony</h1>
          <p class="mt-1 text-sm text-surface-500 dark:text-surface-400">
            {{ tenants.value()?.length ?? 0 }} zarejestrowanych salonów
          </p>
        </div>
        <p-button label="Utwórz salon" icon="pi pi-plus" (onClick)="openCreate()" />
      </div>

      <div class="rounded-3xl border border-surface-200 dark:border-surface-200 overflow-hidden bg-white dark:bg-surface-50 shadow-sm">
        <p-table
          [value]="tenants.value() ?? []"
          [loading]="tenants.isLoading()"
          [globalFilterFields]="['name', 'slug', 'effectiveStatus']"
          styleClass="p-datatable-sm"
          [rowHover]="true"
        >
          <ng-template pTemplate="header">
            <tr>
              <th pSortableColumn="name">Salon <p-sortIcon field="name" /></th>
              <th>Slug</th>
              <th pSortableColumn="effectiveStatus">Status <p-sortIcon field="effectiveStatus" /></th>
              <th>Seats / FM</th>
              <th>Daty</th>
              <th></th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-t>
            <tr>
              <td class="font-semibold">{{ t.name }}</td>
              <td>
                <code class="text-xs bg-surface-100 dark:bg-surface-100 px-2 py-0.5 rounded-lg">
                  {{ t.slug }}
                </code>
              </td>
              <td>
                <p-tag [value]="statusLabel(t)" [severity]="statusSeverity(t)" />
              </td>
              <td class="text-sm">
                <span class="font-bold">{{ t.seats }}</span>
                @if (t.isFoundingMember) {
                  <span class="ml-2 inline-block rounded-md bg-amber-500/15 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide text-amber-700">FM</span>
                }
                <div class="text-xs opacity-60">{{ formatPrice(t.monthlyPriceInGrosze) }} / mies.</div>
              </td>
              <td class="text-sm text-surface-600 dark:text-surface-400">
                @if (t.status === 'Trial') {
                  {{ t.isTrialActive ? 'Trial — ' + t.daysRemainingInTrial + ' dni' : 'Trial wygasł' }}
                  <span class="block text-xs opacity-60">do {{ formatDate(t.trialEndsAt) }}</span>
                } @else if (t.currentPeriodEndsAt) {
                  Okres do {{ formatDate(t.currentPeriodEndsAt) }}
                } @else {
                  —
                }
              </td>
              <td class="text-right">
                <p-button
                  label="Wsparcie"
                  icon="pi pi-life-ring"
                  size="small"
                  severity="danger"
                  [text]="true"
                  (onClick)="openSupport(t)"
                />
                <p-button
                  label="Pracownicy"
                  icon="pi pi-users"
                  size="small"
                  [text]="true"
                  (onClick)="openEmployees(t)"
                />
                <p-button
                  label="Domena"
                  icon="pi pi-globe"
                  size="small"
                  [text]="true"
                  (onClick)="openDomain(t)"
                />
                <p-button
                  label="Przekaż"
                  icon="pi pi-send"
                  size="small"
                  [text]="true"
                  (onClick)="openTransfer(t)"
                />
                <p-button
                  label="Edytuj"
                  icon="pi pi-pencil"
                  size="small"
                  [text]="true"
                  (onClick)="openEdit(t)"
                />
                <p-button
                  label="Usuń"
                  icon="pi pi-trash"
                  size="small"
                  severity="danger"
                  [text]="true"
                  (onClick)="openDelete(t)"
                />
              </td>
            </tr>
          </ng-template>

          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="6" class="text-center py-8 text-surface-400">Brak salonów</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    </div>

    <!-- Dialog edycji subskrypcji -->
    <p-dialog
      [header]="'Subskrypcja — ' + (editing()?.name ?? '')"
      [(visible)]="dialogVisible"
      [modal]="true"
      [style]="{ width: '480px' }"
      [closable]="!saving()"
    >
      @if (editing(); as t) {
        <div class="flex flex-col gap-5 py-2">
          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Status</label>
            <p-select
              [options]="statusOptions"
              [(ngModel)]="selectedStatus"
              optionLabel="label"
              optionValue="value"
              styleClass="w-full"
            />
          </div>

          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Stanowiska</label>
            <p-inputNumber
              [(ngModel)]="seats"
              [min]="1"
              [max]="1000"
              [showButtons]="true"
              styleClass="w-full"
            />
          </div>

          <div class="flex items-center gap-2">
            <p-checkbox [(ngModel)]="isFoundingMember" [binary]="true" inputId="fm" />
            <label for="fm" class="text-sm">Founding Member (49 zł baza)</label>
          </div>

          @if (selectedStatus === SubscriptionStatus.Trial) {
            <div class="flex flex-col gap-2">
              <label class="text-sm font-semibold">Trial ważny do</label>
              <p-datePicker
                [(ngModel)]="trialEndsAt"
                dateFormat="dd.mm.yy"
                [showIcon]="true"
                styleClass="w-full"
              />
            </div>
          }

          @if (selectedStatus === SubscriptionStatus.Active || selectedStatus === SubscriptionStatus.PastDue) {
            <div class="flex flex-col gap-2">
              <label class="text-sm font-semibold">Okres rozliczeniowy do</label>
              <p-datePicker
                [(ngModel)]="currentPeriodEndsAt"
                dateFormat="dd.mm.yy"
                [showIcon]="true"
                styleClass="w-full"
              />
            </div>
          }

          <div class="flex flex-col gap-2 border-t border-surface-200 dark:border-surface-200 pt-4">
            <label class="text-sm font-semibold">Twardy limit SMS / miesiąc (anti-abuse)</label>
            <p-inputNumber
              [(ngModel)]="smsHardCap"
              [min]="0"
              [max]="1000000"
              [showButtons]="true"
              placeholder="puste = limit z planu"
              styleClass="w-full"
            />
            <span class="text-xs opacity-60">
              Plan: {{ t.monthlySmsAllowance }} SMS. Obowiązuje teraz:
              <strong>{{ t.effectiveMonthlySmsCap }}</strong> SMS/mies. Po przekroczeniu blokujemy
              wysyłkę SMS — także OTP rezerwacji. Puste pole = limit z planu.
            </span>
            <div class="flex justify-end">
              <p-button
                label="Zapisz limit SMS"
                icon="pi pi-shield"
                [text]="true"
                [loading]="savingCap()"
                (onClick)="saveSmsCap(t)"
              />
            </div>
          </div>

          <div class="flex justify-end gap-2 pt-2">
            <p-button label="Anuluj" [text]="true" [disabled]="saving()" (onClick)="dialogVisible = false" />
            <p-button label="Zapisz" icon="pi pi-check" [loading]="saving()" (onClick)="save(t)" />
          </div>
        </div>
      }
    </p-dialog>

    <!-- Dialog: przekaż salon nowemu właścicielowi (zmiana e-maila logowania + reset hasła) -->
    <p-dialog
      [header]="'Przekaż salon — ' + (transferTenant()?.name ?? '')"
      [(visible)]="transferDialogVisible"
      [modal]="true"
      [style]="{ width: '480px' }"
      [closable]="!transferring()"
    >
      @if (transferTenant(); as t) {
        <div class="flex flex-col gap-5 py-2">
          <div class="rounded-xl border border-amber-300 bg-amber-50 dark:border-amber-900 dark:bg-amber-950/40 p-4 text-sm text-amber-900 dark:text-amber-200">
            <p class="font-bold mb-1">Przepięcie konta właściciela na nowy adres.</p>
            <p>
              E-mail logowania konta właściciela salonu <strong>{{ t.name }}</strong> zostanie zmieniony
              na podany adres, a właściciel dostanie link do ustawienia hasła. Historia salonu i dane
              pozostają nietknięte — zmienia się tylko login.
            </p>
          </div>

          <div class="flex flex-col gap-2">
            <label for="transferEmail" class="text-sm font-semibold">Nowy e-mail właściciela</label>
            <input
              id="transferEmail"
              pInputText
              type="email"
              [(ngModel)]="transferEmail"
              placeholder="wlascicielka@salon.pl"
              autocomplete="off"
              class="w-full"
            />
          </div>

          <div class="flex justify-end gap-2 pt-2">
            <p-button label="Anuluj" [text]="true" [disabled]="transferring()" (onClick)="transferDialogVisible = false" />
            <p-button
              label="Przekaż i wyślij link"
              icon="pi pi-send"
              [loading]="transferring()"
              [disabled]="!isValidTransferEmail()"
              (onClick)="confirmTransfer(t)"
            />
          </div>
        </div>
      }
    </p-dialog>

    <!-- Dialog trybu wsparcia (support impersonation) -->
    <p-dialog
      [header]="'Tryb wsparcia — ' + (supportTenant()?.name ?? '')"
      [(visible)]="supportDialogVisible"
      [modal]="true"
      [style]="{ width: '480px' }"
      [closable]="!startingSupport()"
    >
      @if (supportTenant(); as t) {
        <div class="flex flex-col gap-5 py-2">
          <p class="text-sm text-surface-500 dark:text-surface-400">
            Wejdziesz w salon <strong>{{ t.name }}</strong> jako support. Sesja jest ograniczona
            czasowo i w pełni audytowana.
          </p>

          <div class="flex flex-col gap-2">
            <label for="supportReason" class="text-sm font-semibold">Powód (wymagany)</label>
            <input
              id="supportReason"
              pInputText
              [(ngModel)]="supportReason"
              placeholder="np. Pomoc z konfiguracją grafiku"
              class="w-full"
            />
          </div>

          <div class="flex items-center gap-2">
            <p-checkbox [(ngModel)]="supportReadOnly" [binary]="true" inputId="supportReadOnly" />
            <label for="supportReadOnly" class="text-sm">Tryb tylko do odczytu (blokuje zapis)</label>
          </div>

          <div class="flex justify-end gap-2 pt-2">
            <p-button label="Anuluj" [text]="true" [disabled]="startingSupport()" (onClick)="supportDialogVisible = false" />
            <p-button
              label="Wejdź jako support"
              icon="pi pi-life-ring"
              severity="danger"
              [loading]="startingSupport()"
              [disabled]="supportReason.trim().length < 5"
              (onClick)="startSupport(t)"
            />
          </div>
        </div>
      }
    </p-dialog>

    <!-- Dialog usuwania salonu (operacja nieodwracalna) -->
    <p-dialog
      [header]="'Usuń salon — ' + (deletingTenant()?.name ?? '')"
      [(visible)]="deleteDialogVisible"
      [modal]="true"
      [style]="{ width: '480px' }"
      [closable]="!deleting()"
    >
      @if (deletingTenant(); as t) {
        <div class="flex flex-col gap-5 py-2">
          <div class="rounded-xl border border-red-300 bg-red-50 dark:border-red-900 dark:bg-red-950/40 p-4 text-sm text-red-800 dark:text-red-200">
            <p class="font-bold mb-1">To działanie jest nieodwracalne.</p>
            <p>
              Trwale usuniesz salon <strong>{{ t.name }}</strong> wraz ze wszystkimi danymi:
              wizytami, klientkami, pracownikami, usługami, powiadomieniami oraz kontami logowania
              powiązanymi wyłącznie z tym salonem. Nie da się tego cofnąć.
            </p>
          </div>

          <div class="flex flex-col gap-2">
            <label for="deleteConfirm" class="text-sm font-semibold">
              Aby potwierdzić, wpisz slug salonu: <code class="bg-surface-100 dark:bg-surface-100 px-1.5 py-0.5 rounded">{{ t.slug }}</code>
            </label>
            <input
              id="deleteConfirm"
              pInputText
              [(ngModel)]="deleteConfirmText"
              [placeholder]="t.slug ?? ''"
              autocomplete="off"
              class="w-full"
            />
          </div>

          <div class="flex justify-end gap-2 pt-2">
            <p-button label="Anuluj" [text]="true" [disabled]="deleting()" (onClick)="deleteDialogVisible = false" />
            <p-button
              label="Usuń salon na zawsze"
              icon="pi pi-trash"
              severity="danger"
              [loading]="deleting()"
              [disabled]="deleteConfirmText.trim() !== t.slug"
              (onClick)="confirmDelete(t)"
            />
          </div>
        </div>
      }
    </p-dialog>

    <!-- Dialog: utwórz salon dla klienta (tenant + właścicielka + pracownice-zasoby + domena) -->
    <p-dialog
      header="Utwórz salon dla klienta"
      [(visible)]="createDialogVisible"
      [modal]="true"
      [style]="{ width: '660px' }"
      [closable]="!creating()"
    >
      <div class="flex flex-col gap-5 py-2">
        <div class="grid grid-cols-2 gap-4">
          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Nazwa salonu</label>
            <input pInputText [(ngModel)]="c.salonName" class="w-full" placeholder="Salon Magdalena Nowak" />
          </div>
          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Slug (publiczny)</label>
            <input pInputText [(ngModel)]="c.slug" class="w-full" placeholder="magdalena-nowak" />
          </div>
          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Strefa czasowa</label>
            <input pInputText [(ngModel)]="c.timeZoneId" class="w-full" />
          </div>
          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Waluta</label>
            <input pInputText [(ngModel)]="c.currency" class="w-full" maxlength="3" />
          </div>
        </div>

        <div class="flex flex-col gap-2">
          <label class="text-sm font-semibold">Domena white-label (opcjonalnie)</label>
          <input pInputText [(ngModel)]="c.customDomain" class="w-full" placeholder="salon-przyklad.pl" />
          <span class="text-xs opacity-60">Rezerwacja pod rezerwacja.&lt;domena&gt;, API pod api.&lt;domena&gt; (DNS-only na nasz serwer).</span>
        </div>

        <div class="rounded-xl border border-surface-200 dark:border-surface-200 p-4 flex flex-col gap-4">
          <p class="text-sm font-bold">Konto właścicielki (Owner)</p>
          <div class="grid grid-cols-2 gap-4">
            <div class="flex flex-col gap-2"><label class="text-sm font-semibold">Imię</label><input pInputText [(ngModel)]="c.ownerFirstName" class="w-full" /></div>
            <div class="flex flex-col gap-2"><label class="text-sm font-semibold">Nazwisko</label><input pInputText [(ngModel)]="c.ownerLastName" class="w-full" /></div>
            <div class="flex flex-col gap-2"><label class="text-sm font-semibold">E-mail</label><input pInputText [(ngModel)]="c.ownerEmail" class="w-full" placeholder="salon@salon-przyklad.pl" /></div>
            <div class="flex flex-col gap-2"><label class="text-sm font-semibold">Hasło (min. 8 znaków)</label><input pInputText [(ngModel)]="c.ownerPassword" class="w-full" placeholder="Password123!" /></div>
          </div>
          <span class="text-xs opacity-60">E-mail od razu potwierdzony — właścicielka może się zalogować tymi danymi (zmienisz je później).</span>
        </div>

        <div class="rounded-xl border border-surface-200 dark:border-surface-200 p-4 flex flex-col gap-3">
          <div class="flex items-center justify-between">
            <p class="text-sm font-bold">Pracowniczki (zasoby — bez logowania)</p>
            <p-button label="Dodaj" icon="pi pi-plus" size="small" [text]="true" (onClick)="addStaff()" />
          </div>
          @for (s of c.staff; track $index) {
            <div class="grid grid-cols-[1fr_1fr_1.3fr_auto] gap-2 items-center">
              <input pInputText [(ngModel)]="s.firstName" placeholder="Imię" class="w-full" />
              <input pInputText [(ngModel)]="s.lastName" placeholder="Nazwisko" class="w-full" />
              <input pInputText [(ngModel)]="s.email" placeholder="e-mail" class="w-full" />
              <p-button icon="pi pi-times" size="small" severity="danger" [text]="true" (onClick)="removeStaff($index)" />
            </div>
          }
          @if (c.staff.length === 0) {
            <span class="text-xs opacity-60">Brak — dodaj stylistki przyciskiem „Dodaj". Konta logowania można dopisać później.</span>
          }
        </div>

        <div class="flex justify-end gap-2 pt-2">
          <p-button label="Anuluj" [text]="true" [disabled]="creating()" (onClick)="createDialogVisible = false" />
          <p-button label="Utwórz salon" icon="pi pi-check" [loading]="creating()" [disabled]="!canCreate()" (onClick)="createSalon()" />
        </div>
      </div>
    </p-dialog>

    <!-- Dialog: white-label domena istniejącego salonu -->
    <p-dialog
      [header]="'Domena white-label — ' + (domainTenant()?.name ?? '')"
      [(visible)]="domainDialogVisible"
      [modal]="true"
      [style]="{ width: '480px' }"
      [closable]="!savingDomain()"
    >
      @if (domainTenant(); as t) {
        <div class="flex flex-col gap-5 py-2">
          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Bazowa domena klienta</label>
            <input pInputText [(ngModel)]="customDomainInput" class="w-full" placeholder="salon-przyklad.pl" [disabled]="loadingDomain()" />
            <span class="text-xs opacity-60">
              Puste pole = wyłącz white-label. Rezerwacja: rezerwacja.&lt;domena&gt;, API: api.&lt;domena&gt;
              (oba jako rekordy DNS-only wskazujące na nasz serwer).
            </span>
          </div>
          <div class="flex justify-end gap-2 pt-2">
            <p-button label="Anuluj" [text]="true" [disabled]="savingDomain()" (onClick)="domainDialogVisible = false" />
            <p-button label="Zapisz domenę" icon="pi pi-check" [loading]="savingDomain()" (onClick)="saveDomain(t)" />
          </div>
        </div>
      }
    </p-dialog>

    <!-- Dialog: pracownicy istniejącego salonu (zasoby kalendarza) -->
    <p-dialog
      [header]="'Pracownicy — ' + (employeesTenant()?.name ?? '')"
      [(visible)]="employeesDialogVisible"
      [modal]="true"
      [style]="{ width: '560px' }"
    >
      @if (employeesTenant(); as t) {
        <div class="flex flex-col gap-5 py-2">
          <div class="rounded-xl border border-surface-200 dark:border-surface-200 divide-y divide-surface-200 dark:divide-surface-200">
            @if (loadingEmployees()) {
              <div class="p-4 text-sm opacity-60">Ładowanie…</div>
            } @else if (employees().length === 0) {
              <div class="p-4 text-sm opacity-60">Brak pracowników. Dodaj pierwszą osobę poniżej.</div>
            } @else {
              @for (e of employees(); track e.id) {
                <div class="flex items-center justify-between p-3">
                  <div>
                    <span class="font-semibold">{{ e.firstName }} {{ e.lastName }}</span>
                    <span class="block text-xs opacity-60">{{ e.email }}</span>
                  </div>
                  <div class="flex items-center gap-3">
                    @if (e.hasAccount) {
                      <span class="rounded-md bg-emerald-500/15 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-emerald-700">konto</span>
                    } @else {
                      <span class="rounded-md bg-surface-200 dark:bg-surface-200 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-surface-600">zasób</span>
                      <p-button
                        label="Włącz logowanie"
                        icon="pi pi-key"
                        size="small"
                        [text]="true"
                        (onClick)="openEnableLogin(e)"
                      />
                    }
                    <p-button
                      label="Anonimizuj"
                      icon="pi pi-eraser"
                      size="small"
                      severity="danger"
                      [text]="true"
                      [loading]="anonymizingId() === e.id"
                      (onClick)="anonymizeEmployee(t, e)"
                    />
                  </div>
                </div>
              }
            }
          </div>

          <div class="rounded-xl border border-surface-200 dark:border-surface-200 p-4 flex flex-col gap-3">
            <p class="text-sm font-bold">Dodaj pracownika (zasób — bez logowania)</p>
            <div class="grid grid-cols-[1fr_1fr_1.3fr] gap-2">
              <input pInputText [(ngModel)]="newEmp.firstName" placeholder="Imię" class="w-full" />
              <input pInputText [(ngModel)]="newEmp.lastName" placeholder="Nazwisko" class="w-full" />
              <input pInputText [(ngModel)]="newEmp.email" placeholder="e-mail" class="w-full" />
            </div>
            <div class="flex justify-end">
              <p-button label="Dodaj" icon="pi pi-plus" [loading]="addingEmployee()" [disabled]="!canAddEmployee()" (onClick)="addEmployee(t)" />
            </div>
          </div>

          <div class="flex justify-end pt-2">
            <p-button label="Zamknij" [text]="true" (onClick)="employeesDialogVisible = false" />
          </div>
        </div>
      }
    </p-dialog>

    <!-- Dialog: włącz logowanie istniejącej pracowniczce-zasobowi (konto + link „ustaw hasło") -->
    <p-dialog
      [header]="'Włącz logowanie — ' + enableLoginName()"
      [(visible)]="enableLoginDialogVisible"
      [modal]="true"
      [style]="{ width: '460px' }"
      [closable]="!enablingLogin()"
    >
      @if (enableLoginEmployee(); as e) {
        <div class="flex flex-col gap-5 py-2">
          <div class="rounded-xl border border-amber-300 bg-amber-50 dark:border-amber-900 dark:bg-amber-950/40 p-4 text-sm text-amber-900 dark:text-amber-200">
            Utworzymy konto logowania dla <strong>{{ e.firstName }} {{ e.lastName }}</strong> i wyślemy na
            podany adres link do ustawienia hasła. Kalendarz i wizyty pracownika zostają bez zmian.
          </div>

          <div class="flex flex-col gap-2">
            <label for="enableLoginEmail" class="text-sm font-semibold">E-mail (login)</label>
            <input
              id="enableLoginEmail"
              pInputText
              type="email"
              [(ngModel)]="enableLoginEmail"
              placeholder="np. anna@salon.pl"
              autocomplete="off"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-2">
            <label class="text-sm font-semibold">Rola</label>
            <p-select
              [options]="enableLoginRoleOptions"
              [(ngModel)]="enableLoginRole"
              optionLabel="label"
              optionValue="value"
              styleClass="w-full"
            />
          </div>

          <div class="flex justify-end gap-2 pt-2">
            <p-button label="Anuluj" [text]="true" [disabled]="enablingLogin()" (onClick)="enableLoginDialogVisible = false" />
            <p-button
              label="Wyślij link"
              icon="pi pi-send"
              [loading]="enablingLogin()"
              [disabled]="!isValidEnableLoginEmail()"
              (onClick)="confirmEnableLogin()"
            />
          </div>
        </div>
      }
    </p-dialog>
  `,
})
export class TenantsPageComponent {
  private readonly client = inject(TenantsClient);
  private readonly auth = inject(AuthClient);
  private readonly toast = inject(MessageService);
  private readonly impersonation = inject(ImpersonationService);

  readonly SubscriptionStatus = SubscriptionStatus;

  // Tworzenie salonu dla klienta (tenant + właścicielka + pracownice-zasoby + domena)
  createDialogVisible = false;
  creating = signal(false);
  c = this.emptyCreateModel();

  // White-label domena istniejącego salonu
  domainDialogVisible = false;
  domainTenant = signal<TenantAdminDto | null>(null);
  customDomainInput = '';
  savingDomain = signal(false);
  loadingDomain = signal(false);

  // Pracownicy istniejącego salonu (zasoby kalendarza)
  employeesDialogVisible = false;
  employeesTenant = signal<TenantAdminDto | null>(null);
  employees = signal<TenantEmployeeDto[]>([]);
  loadingEmployees = signal(false);
  newEmp = { firstName: '', lastName: '', email: '' };
  addingEmployee = signal(false);
  anonymizingId = signal<string | null>(null);

  // Włącz logowanie istniejącej pracowniczce-zasobowi (konto Identity + link „ustaw hasło")
  enableLoginDialogVisible = false;
  enableLoginTenant = signal<TenantAdminDto | null>(null);
  enableLoginEmployee = signal<TenantEmployeeDto | null>(null);
  enableLoginEmail = '';
  enableLoginRole = 'Employee';
  enablingLogin = signal(false);
  readonly enableLoginRoleOptions = [
    { label: 'Pracownik', value: 'Employee' },
    { label: 'Manager', value: 'Manager' },
  ];

  tenants = rxResource({ stream: () => this.client.getTenantsAdmin() });

  // Przekazanie salonu nowemu właścicielowi (zmiana e-maila logowania + reset hasła)
  transferDialogVisible = false;
  transferTenant = signal<TenantAdminDto | null>(null);
  transferEmail = '';
  transferring = signal(false);

  // Tryb wsparcia (support impersonation)
  supportDialogVisible = false;
  supportTenant = signal<TenantAdminDto | null>(null);
  supportReason = '';
  supportReadOnly = true;
  startingSupport = signal(false);

  dialogVisible = false;
  editing = signal<TenantAdminDto | null>(null);
  saving = signal(false);

  // Usuwanie salonu (nieodwracalne — wymaga wpisania sluga)
  deleteDialogVisible = false;
  deletingTenant = signal<TenantAdminDto | null>(null);
  deleteConfirmText = '';
  deleting = signal(false);

  selectedStatus = SubscriptionStatus.Trial;
  seats = 1;
  isFoundingMember = false;
  trialEndsAt: Date = this.defaultTrialEnd();
  currentPeriodEndsAt: Date = this.defaultPeriodEnd();
  smsHardCap: number | null = null;
  savingCap = signal(false);

  readonly statusOptions = [
    { label: 'Trial', value: SubscriptionStatus.Trial },
    { label: 'Active', value: SubscriptionStatus.Active },
    { label: 'PastDue', value: SubscriptionStatus.PastDue },
    { label: 'Canceled', value: SubscriptionStatus.Canceled },
  ];

  openSupport(t: TenantAdminDto): void {
    this.supportTenant.set(t);
    this.supportReason = '';
    this.supportReadOnly = true;
    this.supportDialogVisible = true;
  }

  startSupport(t: TenantAdminDto): void {
    if (!t.id || this.supportReason.trim().length < 5) return;
    this.startingSupport.set(true);
    this.impersonation.start(t.id, this.supportReason.trim(), this.supportReadOnly).subscribe({
      next: () => {
        // Cookie sesji ustawione — pełny reload, by wejść w kontekst salonu jako Owner.
        window.location.href = '/';
      },
      error: () => {
        this.toast.add({ severity: 'error', summary: 'Błąd', detail: 'Nie udało się rozpocząć sesji wsparcia.', life: 4000 });
        this.startingSupport.set(false);
      },
    });
  }

  openTransfer(t: TenantAdminDto): void {
    this.transferTenant.set(t);
    this.transferEmail = '';
    this.transferDialogVisible = true;
  }

  isValidTransferEmail(): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.transferEmail.trim());
  }

  confirmTransfer(t: TenantAdminDto): void {
    if (!t.id || !this.isValidTransferEmail()) return;
    this.transferring.set(true);
    this.auth.transferOwnership({ tenantId: t.id, newEmail: this.transferEmail.trim() }).subscribe({
      next: () => {
        this.toast.add({
          severity: 'success',
          summary: 'Przekazano',
          detail: `Konto właściciela salonu ${t.name} przepięte na ${this.transferEmail.trim()}. Wysłaliśmy link do ustawienia hasła.`,
          life: 5000,
        });
        this.transferDialogVisible = false;
      },
      error: () => {
        // errorInterceptor pokaże toast (np. e-mail zajęty, brak właściciela).
      },
      complete: () => this.transferring.set(false),
    });
  }

  openEdit(t: TenantAdminDto): void {
    this.editing.set(t);
    this.selectedStatus = this.parseStatus(t.status);
    this.seats = t.seats ?? 1;
    this.isFoundingMember = t.isFoundingMember ?? false;
    this.trialEndsAt = t.trialEndsAt ? new Date(t.trialEndsAt) : this.defaultTrialEnd();
    this.currentPeriodEndsAt = t.currentPeriodEndsAt ? new Date(t.currentPeriodEndsAt) : this.defaultPeriodEnd();
    this.smsHardCap = t.monthlySmsHardCap ?? null;
    this.dialogVisible = true;
  }

  openDelete(t: TenantAdminDto): void {
    this.deletingTenant.set(t);
    this.deleteConfirmText = '';
    this.deleteDialogVisible = true;
  }

  confirmDelete(t: TenantAdminDto): void {
    if (!t.id || this.deleteConfirmText.trim() !== t.slug) return;
    this.deleting.set(true);
    this.client.deleteTenant(t.id).subscribe({
      next: () => {
        this.toast.add({
          severity: 'success',
          summary: 'Usunięto',
          detail: `Salon ${t.name} został trwale usunięty.`,
          life: 4000,
        });
        this.deleteDialogVisible = false;
        this.tenants.reload();
      },
      error: () => {
        this.toast.add({
          severity: 'error',
          summary: 'Błąd',
          detail: 'Nie udało się usunąć salonu.',
          life: 4000,
        });
      },
      complete: () => this.deleting.set(false),
    });
  }

  saveSmsCap(t: TenantAdminDto): void {
    if (!t.id) return;
    this.savingCap.set(true);
    this.client.setSmsHardCap(t.id, { hardCap: this.smsHardCap ?? undefined }).subscribe({
      next: () => {
        this.toast.add({
          severity: 'success',
          summary: 'Zapisano',
          detail:
            this.smsHardCap == null
              ? `Limit SMS salonu ${t.name} ustawiony na limit z planu.`
              : `Limit SMS salonu ${t.name}: ${this.smsHardCap}/mies.`,
          life: 3500,
        });
        this.tenants.reload();
      },
      error: () => {
        this.toast.add({ severity: 'error', summary: 'Błąd', detail: 'Nie udało się zapisać limitu SMS.', life: 4000 });
      },
      complete: () => this.savingCap.set(false),
    });
  }

  save(t: TenantAdminDto): void {
    if (!t.id) return;
    this.saving.set(true);
    this.client.setSubscription(t.id, {
      status: this.selectedStatus,
      seats: this.seats,
      isFoundingMember: this.isFoundingMember,
      trialEndsAt: this.selectedStatus === SubscriptionStatus.Trial ? this.trialEndsAt : undefined,
      currentPeriodEndsAt:
        this.selectedStatus === SubscriptionStatus.Active || this.selectedStatus === SubscriptionStatus.PastDue
          ? this.currentPeriodEndsAt
          : undefined,
    }).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Zapisano', detail: `Subskrypcja salonu ${t.name} zaktualizowana.`, life: 3000 });
        this.dialogVisible = false;
        this.tenants.reload();
      },
      error: () => {
        this.toast.add({ severity: 'error', summary: 'Błąd', detail: 'Nie udało się zapisać.', life: 4000 });
      },
      complete: () => this.saving.set(false),
    });
  }

  openCreate(): void {
    this.c = this.emptyCreateModel();
    this.createDialogVisible = true;
  }

  addStaff(): void {
    this.c.staff.push({ firstName: '', lastName: '', email: '' });
  }

  removeStaff(index: number): void {
    this.c.staff.splice(index, 1);
  }

  canCreate(): boolean {
    const c = this.c;
    return !!(
      c.salonName.trim() &&
      c.slug.trim() &&
      c.ownerEmail.trim() &&
      c.ownerPassword.length >= 8 &&
      c.ownerFirstName.trim() &&
      c.ownerLastName.trim()
    );
  }

  createSalon(): void {
    if (!this.canCreate()) return;
    this.creating.set(true);
    const c = this.c;
    const req: AdminCreateSalonRequest = {
      salonName: c.salonName.trim(),
      salonSlug: c.slug.trim(),
      timeZoneId: c.timeZoneId.trim() || 'Europe/Warsaw',
      currency: (c.currency.trim() || 'PLN').toUpperCase(),
      ownerEmail: c.ownerEmail.trim(),
      ownerPassword: c.ownerPassword,
      ownerFirstName: c.ownerFirstName.trim(),
      ownerLastName: c.ownerLastName.trim(),
      customDomain: c.customDomain.trim() || undefined,
      staff: c.staff
        .filter((s) => s.firstName.trim() && s.lastName.trim() && s.email.trim())
        .map<AdminCreateSalonStaffMember>((s) => ({
          firstName: s.firstName.trim(),
          lastName: s.lastName.trim(),
          email: s.email.trim(),
        })),
    };
    this.auth.adminCreateSalon(req).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Utworzono', detail: `Salon ${req.salonName} został utworzony.`, life: 4000 });
        this.createDialogVisible = false;
        this.tenants.reload();
      },
      error: () => {
        // errorInterceptor pokaże szczegółowy toast (np. zajęty slug/email/domena).
      },
      complete: () => this.creating.set(false),
    });
  }

  openDomain(t: TenantAdminDto): void {
    this.domainTenant.set(t);
    this.customDomainInput = '';
    this.domainDialogVisible = true;
    if (t.id) {
      this.loadingDomain.set(true);
      this.client.getTenant(t.id).subscribe({
        next: (dto) => { this.customDomainInput = dto.customDomain ?? ''; },
        error: () => {},
        complete: () => this.loadingDomain.set(false),
      });
    }
  }

  saveDomain(t: TenantAdminDto): void {
    if (!t.id) return;
    this.savingDomain.set(true);
    const domain = this.customDomainInput.trim();
    this.client.setCustomDomain(t.id, { customDomain: domain || undefined }).subscribe({
      next: () => {
        this.toast.add({
          severity: 'success',
          summary: 'Zapisano',
          detail: domain ? `Domena salonu ${t.name}: ${domain}.` : `White-label dla ${t.name} wyłączony.`,
          life: 3500,
        });
        this.domainDialogVisible = false;
      },
      error: () => {
        // errorInterceptor pokaże toast (np. domena zajęta przez inny salon).
      },
      complete: () => this.savingDomain.set(false),
    });
  }

  openEmployees(t: TenantAdminDto): void {
    this.employeesTenant.set(t);
    this.employees.set([]);
    this.newEmp = { firstName: '', lastName: '', email: '' };
    this.employeesDialogVisible = true;
    this.reloadEmployees(t);
  }

  canAddEmployee(): boolean {
    const e = this.newEmp;
    return !!(e.firstName.trim() && e.lastName.trim() && e.email.trim());
  }

  addEmployee(t: TenantAdminDto): void {
    if (!t.id || !this.canAddEmployee()) return;
    this.addingEmployee.set(true);
    const name = `${this.newEmp.firstName.trim()} ${this.newEmp.lastName.trim()}`;
    this.client.addTenantEmployee(t.id, {
      firstName: this.newEmp.firstName.trim(),
      lastName: this.newEmp.lastName.trim(),
      email: this.newEmp.email.trim(),
    }).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Dodano', detail: `${name} dodana do salonu ${t.name}.`, life: 3000 });
        this.newEmp = { firstName: '', lastName: '', email: '' };
        this.reloadEmployees(t);
      },
      error: () => {
        // errorInterceptor pokaże toast.
      },
      complete: () => this.addingEmployee.set(false),
    });
  }

  anonymizeEmployee(t: TenantAdminDto, e: TenantEmployeeDto): void {
    if (!t.id || !e.id) return;
    const ok = window.confirm(
      `Zanonimizować dane pracownicy ${e.firstName} ${e.lastName}? RODO art. 17 — operacja nieodwracalna (imię, nazwisko, e-mail zostaną usunięte).`,
    );
    if (!ok) return;
    this.anonymizingId.set(e.id);
    this.client.anonymizeTenantEmployee(t.id, e.id).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Zanonimizowano', detail: 'Dane osobowe pracownicy zostały usunięte.', life: 3500 });
        this.reloadEmployees(t);
      },
      error: () => {
        // errorInterceptor pokaże toast.
      },
      complete: () => this.anonymizingId.set(null),
    });
  }

  enableLoginName(): string {
    const e = this.enableLoginEmployee();
    return e ? `${e.firstName} ${e.lastName}` : '';
  }

  openEnableLogin(e: TenantEmployeeDto): void {
    this.enableLoginTenant.set(this.employeesTenant());
    this.enableLoginEmployee.set(e);
    this.enableLoginEmail = '';
    this.enableLoginRole = 'Employee';
    this.enableLoginDialogVisible = true;
  }

  isValidEnableLoginEmail(): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.enableLoginEmail.trim());
  }

  confirmEnableLogin(): void {
    const t = this.enableLoginTenant();
    const e = this.enableLoginEmployee();
    if (!t?.id || !e?.id || !this.isValidEnableLoginEmail()) return;
    this.enablingLogin.set(true);
    const email = this.enableLoginEmail.trim();
    this.auth.enableEmployeeLogin(t.id, e.id, { email, role: this.enableLoginRole }).subscribe({
      next: () => {
        this.toast.add({
          severity: 'success',
          summary: 'Włączono logowanie',
          detail: `Wysłaliśmy link do ustawienia hasła na ${email}.`,
          life: 4500,
        });
        this.enableLoginDialogVisible = false;
        this.reloadEmployees(t);
      },
      error: () => {
        // errorInterceptor pokaże toast (np. e-mail zajęty).
      },
      complete: () => this.enablingLogin.set(false),
    });
  }

  private reloadEmployees(t: TenantAdminDto): void {
    if (!t.id) return;
    this.loadingEmployees.set(true);
    this.client.getTenantEmployees(t.id).subscribe({
      next: (list) => this.employees.set(list ?? []),
      error: () => {},
      complete: () => this.loadingEmployees.set(false),
    });
  }

  private emptyCreateModel() {
    return {
      salonName: '',
      slug: '',
      timeZoneId: 'Europe/Warsaw',
      currency: 'PLN',
      customDomain: '',
      ownerFirstName: '',
      ownerLastName: '',
      ownerEmail: '',
      ownerPassword: '',
      staff: [] as { firstName: string; lastName: string; email: string }[],
    };
  }

  statusLabel(t: TenantAdminDto): string {
    if (t.status === 'Trial') return t.isTrialActive ? 'Trial' : 'Trial (wygasł)';
    return t.effectiveStatus ?? '—';
  }

  statusSeverity(t: TenantAdminDto): 'success' | 'warn' | 'info' | 'danger' | 'secondary' {
    if (t.effectiveStatus === 'Active') return 'success';
    if (t.effectiveStatus === 'Trial') return 'info';
    if (t.effectiveStatus === 'PastDue') return 'warn';
    if (t.effectiveStatus === 'Canceled') return 'danger';
    return 'secondary';
  }

  formatDate(d: Date | undefined): string {
    if (!d) return '—';
    return new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(d));
  }

  formatPrice(grosze: number | undefined): string {
    if (grosze == null) return '—';
    return new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN', maximumFractionDigits: 0 }).format(grosze / 100);
  }

  private parseStatus(status: string | undefined): SubscriptionStatus {
    if (status === 'Active') return SubscriptionStatus.Active;
    if (status === 'PastDue') return SubscriptionStatus.PastDue;
    if (status === 'Canceled') return SubscriptionStatus.Canceled;
    return SubscriptionStatus.Trial;
  }

  private defaultTrialEnd(): Date {
    const d = new Date();
    d.setDate(d.getDate() + 30);
    return d;
  }

  private defaultPeriodEnd(): Date {
    const d = new Date();
    d.setMonth(d.getMonth() + 1);
    return d;
  }
}
