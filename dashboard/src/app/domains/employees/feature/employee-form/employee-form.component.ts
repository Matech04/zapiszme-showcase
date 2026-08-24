import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { AuthClient, EmployeeDto, EmployeesClient, InviteEmployeeRequest, UpdateEmployeeRequest } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { createFormActions } from '@shared/utils/createFormActions';
import { HasUnsavedChanges } from '@core/guards/has-unsaved-changes';
import { FormLayoutComponent } from "@shared/ui/forms/form-layout.component";
import { FormFieldComponent } from "@shared/ui/forms/form-field-component";
import { FormFieldSelectComponent, SelectOption } from '@shared/ui/forms/form-field-select.component';
import { concat, map, Observable, tap } from 'rxjs';

interface EmployeeInterface {
  firstName: string;
  lastName: string;
  email: string;
  displayName: string;
  role: 'Employee' | 'Manager';
  specialization: string;
}

@Component({
  selector: 'app-employee-form',
  standalone: true,
  imports: [FormLayoutComponent, FormFieldComponent, FormField, FormFieldSelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
  <app-form-layout
    [title]="isEdit() ? 'Edytuj pracownika' : 'Nowy pracownik'"
    (submit)="FormActions.handleSubmit()"
    (cancel)="FormActions.handleCancel()"
    [isEdit]="isEdit()"
    [confirmOnCancel]="true"
    [hasUnsavedChanges]="FormActions.hasUnsavedChanges()"
    [testId]="'employee-form'"
  >
    @if (!isEdit()) {
      <div
        class="mb-4 flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50/70 dark:bg-amber-950/30 px-4 py-3"
      >
        <i class="pi pi-info-circle text-amber-600 dark:text-amber-400 mt-0.5"></i>
        <div class="text-sm text-amber-900 dark:text-amber-100">
          <p class="font-bold mb-0.5">Pracownik dostanie e-mail z linkiem do utworzenia hasła.</p>
          <p class="text-xs leading-relaxed">
            Link do aktywacji konta jest ważny przez 24 godziny. Jeśli wygaśnie, użyj „Wyślij zaproszenie ponownie” na karcie pracownika w zakładce Zespół.
          </p>
        </div>
      </div>
    }

    <div class="grid grid-cols-1 gap-6">
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-6">
        <app-form-field
          testId="first-name-input"
          label="Imię"
          id="firstName"
          placeholder="np. Jan"
          [formField]="employeeForm.firstName"
        />

        <app-form-field
          testId="last-name-input"
          label="Nazwisko"
          id="lastName"
          placeholder="np. Kowalski"
          [formField]="employeeForm.lastName"
        />
      </div>

      @if (showEmailField()) {
        <app-form-field
          data-tour="employee-email"
          testId="email-input"
          label="Adres email"
          id="email"
          type="email"
          placeholder="np. jan.kowalski@example.com"
          [formField]="employeeForm.email"
        />
      }

      <app-form-field
        testId="specialization-input"
        label="Specjalizacja"
        id="specialization"
        placeholder="np. Barber, Stylistka, Kosmetolog"
        [formField]="employeeForm.specialization"
      />

      @if (!isEdit()) {
        <app-form-field
          testId="display-name-input"
          label="Nazwa wyświetlana"
          id="displayName"
          placeholder="np. Jan Kowalski"
          [formField]="employeeForm.displayName"
        />
      }

      @if (showRoleField()) {
        <app-form-field-select
          testId="role-input"
          label="Rola"
          id="role"
          [options]="roleOptions"
          [formField]="employeeForm.role"
        />
      }

      @if (isEdit() && editedRole() === 'Owner') {
        <p class="text-xs text-surface-500 dark:text-surface-400 px-1">
          To konto właściciela — roli nie zmienia się tutaj. Aby przekazać salon, użyj przekazania konta.
        </p>
      }
    </div>
  </app-form-layout>
  `
})
export class EmployeeForm implements HasUnsavedChanges {
  id = input<string>();
  /** Skąd otwarto formularz (query param) — powrót po zapisie/anulowaniu. Domyślnie „Zespół". */
  returnUrl = input<string>();

  isEdit = computed(() => !!this.id());

  private employeesClient = inject(EmployeesClient);
  private authClient = inject(AuthClient);
  private auth = inject(AuthSessionService);

  /** Zmiana roli i e-maila to operacje Identity — tylko właściciel (BusinessManagement). */
  protected readonly isOwner = computed(() => this.auth.currentRole() === 'owner');

  /** Rola edytowanego pracownika (z Identity). null/undefined = brak konta. */
  protected readonly editedRole = signal<string | undefined>(undefined);

  /** Wartości wyjściowe do wykrycia realnej zmiany (nie wołamy endpointów bez potrzeby). */
  private readonly originalEmail = signal<string>('');
  private readonly originalRole = signal<'Employee' | 'Manager' | undefined>(undefined);

  /** E-mail edytowalny: przy tworzeniu zawsze; przy edycji tylko właściciel. */
  protected readonly showEmailField = computed(() => !this.isEdit() || this.isOwner());

  /** Selektor roli: przy tworzeniu zawsze; przy edycji tylko właściciel i nie dla konta właściciela. */
  protected readonly showRoleField = computed(
    () => !this.isEdit() || (this.isOwner() && this.editedRole() !== 'Owner'),
  );

  protected readonly roleOptions: SelectOption[] = [
    { label: 'Pracownik', value: 'Employee' },
    { label: 'Manager', value: 'Manager' },
  ];

  employeeModel = signal<EmployeeInterface>({
    firstName: '',
    lastName: '',
    email: '',
    displayName: '',
    role: 'Employee',
    specialization: '',
  });

  employeeForm = form(
    this.employeeModel,
    (schemaPath) => {
      required(schemaPath.firstName, { message: 'Imię jest wymagane' });
      required(schemaPath.lastName, { message: 'Nazwisko jest wymagane' });
      required(schemaPath.email, { message: 'Email jest wymagany' });
    }
  );

  protected FormActions = createFormActions<EmployeeInterface, any>(
    {
      create: (data) => this.authClient.inviteEmployee(this.toInviteEmployeeRequest(data)),
      update: (id, data) => this.buildUpdate(id, data),
      delete: (id) => this.employeesClient.deleteEmployee(id),
      get: (id) => this.employeesClient.getEmployee(id).pipe(
        tap((data: EmployeeDto) => {
          this.editedRole.set(data.role);
          this.originalEmail.set(data.email || '');
          this.originalRole.set(data.role === 'Manager' ? 'Manager' : 'Employee');
        }),
        map((data: EmployeeDto) => ({
          firstName: data.firstName || '',
          lastName: data.lastName || '',
          email: data.email || '',
          displayName: '',
          role: (data.role === 'Manager' ? 'Manager' : 'Employee') as 'Employee' | 'Manager',
          specialization: data.specialization || '',
        }))
      ),
    },
    this.employeeForm as any,
    this.employeeModel,
    this.id,
    {
      redirectUrl: () => this.returnUrl() || '/admin/team',
      successMessage: (data, isUpdate) =>
        isUpdate
          ? 'Pracownik został zaktualizowany pomyślnie.'
          : `Wysłaliśmy zaproszenie na adres ${data.email}. Pracownik utworzy hasło z linka w wiadomości (ważny 24 h).`,
    }
  );

  /**
   * Zapis edycji. Dane osobowe idą zawsze przez EmployeesClient (StaffManagement + self).
   * Zmianę e-maila/roli (Identity, Owner-only) doklejamy tylko gdy właściciel i wartość realnie
   * się zmieniła — sekwencyjnie, żeby błąd któregokolwiek kroku zatrzymał zapis.
   */
  private buildUpdate(id: string, data: EmployeeInterface): Observable<unknown> {
    const calls: Observable<unknown>[] = [
      this.employeesClient.updateEmployee(id, {
        firstName: data.firstName,
        lastName: data.lastName,
        specialization: data.specialization,
      } as UpdateEmployeeRequest),
    ];

    if (this.isOwner()) {
      const email = (data.email ?? '').trim();
      if (email && email !== this.originalEmail()) {
        calls.push(this.authClient.changeEmployeeEmail(id, { email }));
      }

      if (this.editedRole() !== 'Owner' && data.role && data.role !== this.originalRole()) {
        calls.push(this.authClient.changeEmployeeRole(id, { role: data.role }));
      }
    }

    return concat(...calls);
  }

  private toInviteEmployeeRequest(data: EmployeeInterface): InviteEmployeeRequest {
    const displayName = data.displayName?.trim() || `${data.firstName} ${data.lastName}`.trim();
    return {
      email: data.email,
      displayName,
      firstName: data.firstName,
      lastName: data.lastName,
      role: data.role,
    };
  }

  hasUnsavedChanges(): boolean {
    return this.FormActions.hasUnsavedChanges();
  }
}
