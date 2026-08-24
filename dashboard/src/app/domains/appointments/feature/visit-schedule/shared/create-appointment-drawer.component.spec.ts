import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import {
  AppointmentsClient,
  AppointmentSlotDto,
  CustomersClient,
  EmployeesClient,
  ServiceCategoriesClient,
  ServicesClient,
} from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { CreateAppointmentDrawerComponent } from './create-appointment-drawer.component';

/**
 * Skupiamy się na trybie „zapis poza grafikiem" (ignoreSchedule). Pełny happy-path tworzenia
 * wizyty pokrywają testy backendu + e2e; tu pilnujemy semantyki przełącznika i flagi w body.
 */
describe('CreateAppointmentDrawerComponent — off-schedule', () => {
  let fixture: ComponentFixture<CreateAppointmentDrawerComponent>;
  let component: CreateAppointmentDrawerComponent;

  let appointmentsClientMock: {
    getAvailableSlots: ReturnType<typeof vi.fn>;
    getMonthAvailability: ReturnType<typeof vi.fn>;
    createAppointment: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    appointmentsClientMock = {
      getAvailableSlots: vi.fn().mockReturnValue(of([] as AppointmentSlotDto[])),
      getMonthAvailability: vi.fn().mockReturnValue(of([])),
      createAppointment: vi.fn().mockReturnValue(of('new-id')),
    };

    const employeesClientMock = {
      getEmployees: vi.fn().mockReturnValue(of([{ id: 'emp-1', firstName: 'Ana', lastName: 'Kowalska' }])),
      getEmployeeServices: vi.fn().mockReturnValue(of([{ serviceId: 'svc-1' }])),
    };
    const servicesClientMock = {
      getServices: vi.fn().mockReturnValue(of([{ id: 'svc-1', name: 'Strzyżenie', durationInMinutes: 30 }])),
    };
    const serviceCategoriesClientMock = {
      getServiceCategories: vi.fn().mockReturnValue(of([])),
    };
    const customersClientMock = {
      getCustomers: vi.fn().mockReturnValue(of([])),
    };
    const authMock = { currentEmployeeId: () => 'emp-1' };
    const messagesMock = { add: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [CreateAppointmentDrawerComponent],
      providers: [
        provideRouter([]),
        { provide: AppointmentsClient, useValue: appointmentsClientMock },
        { provide: EmployeesClient, useValue: employeesClientMock },
        { provide: ServicesClient, useValue: servicesClientMock },
        { provide: ServiceCategoriesClient, useValue: serviceCategoriesClientMock },
        { provide: CustomersClient, useValue: customersClientMock },
        { provide: AuthSessionService, useValue: authMock },
        { provide: MessageService, useValue: messagesMock },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(CreateAppointmentDrawerComponent);
    component = fixture.componentInstance;
  });

  async function open(): Promise<void> {
    fixture.componentRef.setInput('context', { employeeId: 'emp-1', date: '2026-05-20' });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('setOffScheduleMode(true) włącza ręczny picker i wyłącza tryb przeszłości', async () => {
    await open();
    component['setPastMode'](true);
    expect(component['pastMode']()).toBe(true);

    component['setOffScheduleMode'](true);
    expect(component['offScheduleMode']()).toBe(true);
    expect(component['pastMode']()).toBe(false);
    expect(component['manualTimeMode']()).toBe(true);
  });

  it('off-schedule: ręczna godzina pozwala na submit i wysyła ignoreSchedule=true', async () => {
    await open();
    expect(component['serviceIds']()).toEqual(['svc-1']);

    component['setOffScheduleMode'](true);
    const t = new Date();
    t.setHours(21, 0, 0, 0);
    component['onPastTimeChange'](t);
    expect(component['slot']()).toBe('21:00');
    expect(component['canSubmit']()).toBe(true);

    component['onSubmit']();
    expect(appointmentsClientMock.createAppointment).toHaveBeenCalledWith(
      expect.objectContaining({
        employeeId: 'emp-1',
        serviceIds: ['svc-1'],
        startTime: '21:00:00',
        createAsBooked: true,
        ignoreSchedule: true,
      }),
    );
  });

  it('normalny tryb: body NIE zawiera ignoreSchedule', async () => {
    await open();
    component['slot'].set('10:00:00');
    component['onSubmit']();
    const cmd = appointmentsClientMock.createAppointment.mock.calls[0][0];
    expect(cmd.ignoreSchedule).toBeUndefined();
    expect(cmd.createAsBooked).toBe(true);
  });

  it('tryb przeszłości nie ustawia ignoreSchedule', async () => {
    await open();
    component['setPastMode'](true);
    component['date'].set('2020-01-01');
    component['onPastTimeChange'](new Date(2020, 0, 1, 9, 0));
    component['onSubmit']();
    const cmd = appointmentsClientMock.createAppointment.mock.calls[0][0];
    expect(cmd.createAsCompleted).toBe(true);
    expect(cmd.ignoreSchedule).toBeUndefined();
  });

  it('domyślny czas = standardowa suma usług; body NIE zawiera customDurationMinutes', async () => {
    await open();
    expect(component['standardDurationMinutes']()).toBe(30);
    expect(component['effectiveDurationMinutes']()).toBe(30);

    component['slot'].set('10:00:00');
    component['onSubmit']();
    const cmd = appointmentsClientMock.createAppointment.mock.calls[0][0];
    expect(cmd.customDurationMinutes).toBeUndefined();
  });

  it('zmiana czasu wysyła customDurationMinutes; równe standardowi → brak override', async () => {
    await open();

    component['onDurationChange'](45);
    expect(component['customDurationMinutes']()).toBe(45);
    expect(component['effectiveDurationMinutes']()).toBe(45);

    component['slot'].set('10:00:00');
    component['onSubmit']();
    expect(appointmentsClientMock.createAppointment).toHaveBeenCalledWith(
      expect.objectContaining({ customDurationMinutes: 45 }),
    );

    // Ustawienie z powrotem na standard (30) → override kasowany (null).
    component['onDurationChange'](30);
    expect(component['customDurationMinutes']()).toBeNull();
  });
});

/**
 * Podział listy usług na sekcje kategorii. Nagłówki pojawiają się dopiero przy realnym
 * podziale — salon z jedną kategorią ma dostać listę płaską, bez samotnego nagłówka.
 */
describe('CreateAppointmentDrawerComponent — sekcje kategorii usług', () => {
  let fixture: ComponentFixture<CreateAppointmentDrawerComponent>;

  async function setup(
    services: { id: string; name: string; categoryId?: string }[],
    categories: { id: string; name: string; orderIndex: number }[],
  ): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [CreateAppointmentDrawerComponent],
      providers: [
        provideRouter([]),
        {
          provide: AppointmentsClient,
          useValue: {
            getAvailableSlots: vi.fn().mockReturnValue(of([] as AppointmentSlotDto[])),
            getMonthAvailability: vi.fn().mockReturnValue(of([])),
            createAppointment: vi.fn().mockReturnValue(of('new-id')),
          },
        },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployees: vi.fn().mockReturnValue(of([{ id: 'emp-1', firstName: 'Ana' }])),
            // Odwrócona kolejność celowo: `getEmployeeServices` nie ma OrderBy na backendzie,
            // więc kolejność chipów musi wynikać z katalogu, nie z listy przypisań.
            getEmployeeServices: vi
              .fn()
              .mockReturnValue(of([...services].reverse().map((s) => ({ serviceId: s.id })))),
          },
        },
        {
          provide: ServicesClient,
          useValue: {
            getServices: vi
              .fn()
              .mockReturnValue(of(services.map((s) => ({ ...s, durationInMinutes: 30 })))),
          },
        },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of(categories)) },
        },
        { provide: CustomersClient, useValue: { getCustomers: vi.fn().mockReturnValue(of([])) } },
        { provide: AuthSessionService, useValue: { currentEmployeeId: () => 'emp-1' } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CreateAppointmentDrawerComponent);
    fixture.componentRef.setInput('context', { employeeId: 'emp-1', date: '2026-05-20' });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const headers = () =>
    Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="create-service-category"]'),
    ).map((el) => (el as HTMLElement).textContent?.trim());

  const chipLabels = () =>
    Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="create-service-chips"] button'),
    ).map((el) => (el as HTMLElement).textContent?.trim());

  beforeEach(() => TestBed.resetTestingModule());

  it('renderuje nagłówki kategorii w kolejności orderIndex, usługi bez kategorii na końcu', async () => {
    await setup(
      [
        { id: 'svc-brwi', name: 'Stylizacja brwi', categoryId: 'cat-brwi' },
        { id: 'svc-mani', name: 'Manicure', categoryId: 'cat-pazn' },
        { id: 'svc-luz', name: 'Konsultacja' },
      ],
      [
        { id: 'cat-brwi', name: 'Brwi', orderIndex: 2 },
        { id: 'cat-pazn', name: 'Paznokcie', orderIndex: 1 },
      ],
    );

    expect(headers()).toEqual(['Paznokcie', 'Brwi', 'Bez kategorii']);
    expect(chipLabels()).toEqual(['Manicure', 'Stylizacja brwi', 'Konsultacja']);
  });

  it('przy jednej kategorii nie renderuje nagłówka — lista zostaje płaska', async () => {
    await setup(
      [
        { id: 'svc-1', name: 'Strzyżenie', categoryId: 'cat-1' },
        { id: 'svc-2', name: 'Koloryzacja', categoryId: 'cat-1' },
      ],
      [{ id: 'cat-1', name: 'Usługi', orderIndex: 0 }],
    );

    expect(headers()).toEqual([]);
    expect(chipLabels()).toEqual(['Strzyżenie', 'Koloryzacja']);
  });

  it('awaria pobrania kategorii nie gubi usług — lista płaska', async () => {
    await setup([{ id: 'svc-1', name: 'Strzyżenie', categoryId: 'cat-1' }], []);

    expect(headers()).toEqual([]);
    expect(chipLabels()).toEqual(['Strzyżenie']);
  });
});

/**
 * REGRESJA: kolejność chipów musi iść za katalogiem usług (OrderIndex, Name), a nie za
 * listą przypisań pracownika — `getEmployeeServices` nie ma OrderBy po stronie backendu.
 */
describe('CreateAppointmentDrawerComponent — kolejność usług', () => {
  it('zachowuje kolejność katalogu, gdy przypisania przychodzą w innej', async () => {
    TestBed.resetTestingModule();

    const catalog = [
      { id: 'svc-a', name: 'Pierwsza', categoryId: 'cat-1', durationInMinutes: 30 },
      { id: 'svc-b', name: 'Druga', categoryId: 'cat-1', durationInMinutes: 30 },
      { id: 'svc-c', name: 'Trzecia', categoryId: 'cat-1', durationInMinutes: 30 },
    ];

    await TestBed.configureTestingModule({
      imports: [CreateAppointmentDrawerComponent],
      providers: [
        provideRouter([]),
        {
          provide: AppointmentsClient,
          useValue: {
            getAvailableSlots: vi.fn().mockReturnValue(of([] as AppointmentSlotDto[])),
            getMonthAvailability: vi.fn().mockReturnValue(of([])),
            createAppointment: vi.fn().mockReturnValue(of('new-id')),
          },
        },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployees: vi.fn().mockReturnValue(of([{ id: 'emp-1', firstName: 'Ana' }])),
            // Backend zwraca przypisania bez OrderBy — tu skrajny przypadek: odwrotnie.
            getEmployeeServices: vi
              .fn()
              .mockReturnValue(of([{ serviceId: 'svc-c' }, { serviceId: 'svc-a' }, { serviceId: 'svc-b' }])),
          },
        },
        { provide: ServicesClient, useValue: { getServices: vi.fn().mockReturnValue(of(catalog)) } },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
        },
        { provide: CustomersClient, useValue: { getCustomers: vi.fn().mockReturnValue(of([])) } },
        { provide: AuthSessionService, useValue: { currentEmployeeId: () => 'emp-1' } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CreateAppointmentDrawerComponent);
    fixture.componentRef.setInput('context', { employeeId: 'emp-1', date: '2026-05-20' });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="create-service-chips"] button'),
    ).map((el) => (el as HTMLElement).textContent?.trim());

    expect(labels).toEqual(['Pierwsza', 'Druga', 'Trzecia']);
    // Auto-zaznaczenie bierze pierwszą z katalogu, nie pierwszą z przypisań.
    expect(fixture.componentInstance['serviceIds']()).toEqual(['svc-a']);
  });
});
