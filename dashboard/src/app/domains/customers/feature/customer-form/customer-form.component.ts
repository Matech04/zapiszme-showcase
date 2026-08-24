import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { form, FormField, validate } from '@angular/forms/signals';
import {
  CreateCustomerCommand,
  CustomerDto,
  CustomersClient,
  UpdateCustomerCommand,
} from '@core/api/api-client';
import { createFormActions } from '@shared/utils/createFormActions';
import { FormLayoutComponent } from '@shared/ui/forms/form-layout.component';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';
import { HasUnsavedChanges } from '@core/guards/has-unsaved-changes';
import { map } from 'rxjs';

interface CustomerFormModel {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  instagramNick: string;
  generalNotes: string;
}

/** Normalizuje nick IG przed wysyłką: trim + usunięcie wiodącego `@` (backend i tak strzyże, ale walidator odrzuca `@`). */
function normalizeInstagramNick(value: string | undefined): string {
  return (value ?? '').trim().replace(/^@+/, '').trim();
}

@Component({
  selector: 'app-customer-form',
  standalone: true,
  imports: [FormLayoutComponent, FormFieldComponent, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-form-layout
      [title]="isEdit() ? 'Edytuj klienta' : 'Nowy klient'"
      (submit)="FormActions.handleSubmit()"
      (cancel)="FormActions.handleCancel()"
      [isEdit]="isEdit()"
      [confirmOnCancel]="true"
      [hasUnsavedChanges]="FormActions.hasUnsavedChanges()"
      [testId]="'customer-form'"
    >
      <div class="grid grid-cols-1 gap-6">
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-6">
          <app-form-field
            testId="customer-first-name"
            data-tour="customer-name"
            label="Imię (opcjonalnie)"
            id="firstName"
            placeholder="np. Anna"
            [formField]="customerForm.firstName"
          />

          <app-form-field
            testId="customer-last-name"
            label="Nazwisko (opcjonalnie)"
            id="lastName"
            placeholder="np. Nowak"
            [formField]="customerForm.lastName"
          />
        </div>

        @if (identifierError(); as msg) {
          <p
            data-testid="customer-identifier-error"
            class="flex items-center gap-2 text-sm font-medium text-red-600 dark:text-red-400"
          >
            <i class="pi pi-exclamation-circle text-xs"></i>
            {{ msg }}
          </p>
        }

        <app-form-field
          testId="customer-email"
          label="Adres email (opcjonalnie)"
          id="email"
          type="email"
          placeholder="np. anna@example.com"
          [formField]="customerForm.email"
        />

        <app-form-field
          testId="customer-phone"
          data-tour="customer-phone"
          label="Telefon (opcjonalnie)"
          id="phoneNumber"
          type="tel"
          placeholder="np. 500 600 700"
          [formField]="customerForm.phoneNumber"
        />

        <app-form-field
          testId="customer-instagram"
          label="Instagram (opcjonalnie)"
          id="instagramNick"
          placeholder="np. anna.nails"
          [formField]="customerForm.instagramNick"
        />

        <app-form-field
          testId="customer-notes"
          label="Notatki"
          id="generalNotes"
          placeholder="Notatki o kliencie — preferencje, alergie itp. (opcjonalnie)"
          [multiline]="true"
          [rows]="4"
          [formField]="customerForm.generalNotes"
        />
      </div>
    </app-form-layout>
  `,
})
export class CustomerFormComponent implements HasUnsavedChanges {
  id = input<string>();

  isEdit = computed(() => !!this.id());

  private customersClient = inject(CustomersClient);

  customerModel = signal<CustomerFormModel>({
    firstName: '',
    lastName: '',
    email: '',
    phoneNumber: '',
    instagramNick: '',
    generalNotes: '',
  });

  customerForm = form(this.customerModel, (schemaPath) => {
    // Imię/nazwisko nieobowiązkowe — wystarczy dowolny identyfikator (telefon lub e-mail też niosą
    // tożsamość; tak powstają klienci z publicznej rezerwacji). Pusty rekord blokujemy.
    validate(schemaPath, (ctx) => {
      const v = ctx.value();
      const hasIdentifier = [v.firstName, v.lastName, v.email, v.phoneNumber].some(
        (s) => (s ?? '').trim() !== '',
      );
      return hasIdentifier
        ? null
        : { kind: 'noIdentifier', message: 'Podaj imię, nazwisko, telefon lub e-mail.' };
    });
  });

  /** Błąd „brak identyfikatora" pokazujemy dopiero po dotknięciu/próbie zapisu, nie na pustym starcie. */
  protected identifierError = computed(() => {
    const root = this.customerForm();
    if (!root.touched()) return null;
    const err = root.errors().find((e) => (e as { kind?: string }).kind === 'noIdentifier');
    return err ? (err as { message?: string }).message ?? 'Podaj dane klienta.' : null;
  });

  protected FormActions = createFormActions<CustomerFormModel, unknown>(
    {
      create: (data) =>
        this.customersClient.createCustomer({
          firstName: data.firstName,
          lastName: data.lastName,
          email: data.email,
          phoneNumber: data.phoneNumber ?? '',
          instagramNick: normalizeInstagramNick(data.instagramNick) || undefined,
          generalNotes: data.generalNotes ?? '',
        } as CreateCustomerCommand),
      update: (customerId, data) =>
        this.customersClient.updateCustomer(customerId, {
          id: customerId,
          firstName: data.firstName,
          lastName: data.lastName,
          email: data.email,
          phoneNumber: data.phoneNumber ?? '',
          instagramNick: normalizeInstagramNick(data.instagramNick) || undefined,
          generalNotes: data.generalNotes ?? '',
        } as UpdateCustomerCommand),
      delete: (customerId) => this.customersClient.deleteCustomer(customerId),
      get: (customerId) =>
        this.customersClient.getCustomer(customerId).pipe(
          map((data: CustomerDto) => ({
            firstName: data.firstName ?? '',
            lastName: data.lastName ?? '',
            email: data.email ?? '',
            phoneNumber: data.phoneNumber ?? '',
            instagramNick: data.instagramNick ?? '',
            generalNotes: data.generalNotes ?? '',
          })),
        ),
    },
    this.customerForm as never,
    this.customerModel,
    this.id,
    {
      successMessage: 'Klient został zapisany pomyślnie',
      redirectUrl: '/admin/customers',
    },
  );

  hasUnsavedChanges(): boolean {
    return this.FormActions.hasUnsavedChanges();
  }
}
