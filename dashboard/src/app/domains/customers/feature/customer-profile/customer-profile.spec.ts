import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ConfirmationService, MessageService } from 'primeng/api';
import {
  AppointmentsClient,
  CustomerDataExportDto,
  CustomerDto,
  CustomersClient,
} from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { CustomerProfileComponent } from './customer-profile.component';

/**
 * Akcje RODO (eksport art. 15/20, anonimizacja art. 17) to polityka API `BusinessManagement`
 * → Owner + Admin. Manager ma whitelist (`StaffManagement`), ale RODO już NIE — inaczej
 * zobaczyłby przyciski i dostał 403 z backendu.
 */
describe('CustomerProfileComponent — akcje RODO', () => {
  let fixture: ComponentFixture<CustomerProfileComponent>;
  let component: CustomerProfileComponent;

  const role = signal<string | null>('owner');
  const exportCustomerData = vi.fn();
  const deleteCustomer = vi.fn();
  let navigate: ReturnType<typeof vi.spyOn>;

  const mockCustomer: CustomerDto = {
    id: 'c-1',
    firstName: 'Anna',
    lastName: 'Kowalska',
    email: 'anna@example.com',
    isWhitelisted: false,
  } as CustomerDto;

  const mockExport = {
    id: 'c-1',
    firstName: 'Anna',
    lastName: 'Kowalska',
    appointments: [],
  } as unknown as CustomerDataExportDto;

  beforeEach(async () => {
    vi.clearAllMocks();
    role.set('owner');
    exportCustomerData.mockReturnValue(of(mockExport));
    deleteCustomer.mockReturnValue(of(null));

    await TestBed.configureTestingModule({
      imports: [CustomerProfileComponent],
      providers: [
        provideZonelessChangeDetection(),
        // Realny router — szablon używa `routerLink`, więc RouterLink potrzebuje ActivatedRoute.
        provideRouter([]),
        MessageService,
        {
          provide: CustomersClient,
          useValue: {
            getCustomer: () => of(mockCustomer),
            exportCustomerData,
            deleteCustomer,
          },
        },
        { provide: AppointmentsClient, useValue: { getCustomerAppointments: () => of([]) } },
        { provide: AuthSessionService, useValue: { currentRole: () => role() } },
        // Dialog potwierdzenia: od razu akceptujemy, żeby testować skutek, nie PrimeNG.
        {
          provide: ConfirmationService,
          useValue: { confirm: (opts: { accept: () => void }) => opts.accept() },
        },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(CustomerProfileComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'c-1');
    fixture.detectChanges();
  });

  /** Sedno blokera: przyciski muszą realnie wyrenderować się w profilu, nie tylko istnieć w API. */
  const renderedText = async (): Promise<string> => {
    await fixture.whenStable();
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  };

  it('właścicielka widzi akcje RODO', () => {
    role.set('owner');
    expect(component['canManageRodo']()).toBe(true);
  });

  it('profil właścicielki renderuje oba przyciski RODO', async () => {
    role.set('owner');
    const text = await renderedText();
    expect(text).toContain('Eksportuj dane');
    expect(text).toContain('Usuń klienta');
  });

  it('profil pracownika nie renderuje przycisków RODO', async () => {
    role.set('employee');
    const text = await renderedText();
    expect(text).not.toContain('Eksportuj dane');
    expect(text).not.toContain('Usuń klienta');
  });

  it('admin (tryb wsparcia) widzi akcje RODO', () => {
    role.set('systemAdmin');
    expect(component['canManageRodo']()).toBe(true);
  });

  it('manager NIE widzi akcji RODO, mimo że ma whitelist', () => {
    role.set('manager');
    expect(component['canManageRodo']()).toBe(false);
    expect(component['canManageWhitelist']()).toBe(true);
  });

  it('pracownik NIE widzi akcji RODO', () => {
    role.set('employee');
    expect(component['canManageRodo']()).toBe(false);
  });

  it('eksport pobiera JSON z danymi klienta', async () => {
    const createObjectURL = vi.fn().mockReturnValue('blob:fake');
    const revokeObjectURL = vi.fn();
    URL.createObjectURL = createObjectURL;
    URL.revokeObjectURL = revokeObjectURL;
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    await component.exportData(mockCustomer);

    expect(exportCustomerData).toHaveBeenCalledWith('c-1');
    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(click).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:fake');
    expect(component.exportBusy()).toBe(false);
  });

  it('usunięcie woła API i wraca na listę (profil znika spod query-filtra)', async () => {
    component.confirmDelete(mockCustomer);
    await vi.waitFor(() => expect(navigate).toHaveBeenCalled());

    expect(deleteCustomer).toHaveBeenCalledWith('c-1');
    expect(navigate).toHaveBeenCalledWith(['/admin', 'customers']);
  });

  it('usunięcie bez potwierdzenia nie woła API', () => {
    // Dialog, który odrzuca — accept() nigdy nie leci.
    (component as unknown as { confirmation: { confirm: () => void } }).confirmation = {
      confirm: () => undefined,
    };

    component.confirmDelete(mockCustomer);

    expect(deleteCustomer).not.toHaveBeenCalled();
  });
});
