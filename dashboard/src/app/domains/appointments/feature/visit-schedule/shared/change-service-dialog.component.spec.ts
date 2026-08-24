import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { MessageService } from 'primeng/api';
import {
  AppointmentDto,
  AppointmentPreviewDto,
  AppointmentsClient,
  EmployeesClient,
  ServiceCategoriesClient,
  ServicesClient,
} from '@core/api/api-client';
import { ChangeServiceDialogComponent } from './change-service-dialog.component';

describe('ChangeServiceDialogComponent', () => {
  let fixture: ComponentFixture<ChangeServiceDialogComponent>;
  let component: ChangeServiceDialogComponent;

  let appointmentsClientMock: {
    getAppointmentById: ReturnType<typeof vi.fn>;
    changeAppointmentServices: ReturnType<typeof vi.fn>;
  };
  let employeesClientMock: { getEmployeeServices: ReturnType<typeof vi.fn> };
  let servicesClientMock: { getServices: ReturnType<typeof vi.fn> };
  let serviceCategoriesClientMock: { getServiceCategories: ReturnType<typeof vi.fn> };
  let messagesMock: { add: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    appointmentsClientMock = {
      getAppointmentById: vi.fn().mockReturnValue(
        of({ id: 'a1', employeeId: 'emp-1', serviceId: 'svc-1' } as AppointmentDto),
      ),
      changeAppointmentServices: vi.fn().mockReturnValue(of('a1')),
    };

    // emp-1 oferuje svc-1 (oryginalna) + svc-extra (combo) + svc-3 (alternatywa).
    employeesClientMock = {
      getEmployeeServices: vi.fn().mockReturnValue(
        of([{ serviceId: 'svc-1' }, { serviceId: 'svc-extra' }, { serviceId: 'svc-3' }]),
      ),
    };

    servicesClientMock = {
      getServices: vi.fn().mockReturnValue(
        of([
          { id: 'svc-1', name: 'Strzyżenie' },
          { id: 'svc-extra', name: 'Modelowanie' },
          { id: 'svc-3', name: 'Koloryzacja' },
        ]),
      ),
    };

    serviceCategoriesClientMock = {
      getServiceCategories: vi.fn().mockReturnValue(of([])),
    };

    messagesMock = { add: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [ChangeServiceDialogComponent],
      providers: [
        { provide: AppointmentsClient, useValue: appointmentsClientMock },
        { provide: EmployeesClient, useValue: employeesClientMock },
        { provide: ServicesClient, useValue: servicesClientMock },
        { provide: ServiceCategoriesClient, useValue: serviceCategoriesClientMock },
        { provide: MessageService, useValue: messagesMock },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ChangeServiceDialogComponent);
    component = fixture.componentInstance;
  });

  function setAppointment(p: Partial<AppointmentPreviewDto> | null): void {
    fixture.componentRef.setInput('appointment', p as AppointmentPreviewDto | null);
    fixture.detectChanges();
  }

  it('isVisible false gdy appointment null', () => {
    setAppointment(null);
    expect((component as unknown as { isVisible: () => boolean }).isVisible()).toBe(false);
  });

  it('po otwarciu fetchuje pełen detail wizyty', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(appointmentsClientMock.getAppointmentById).toHaveBeenCalledWith('a1');
    expect(employeesClientMock.getEmployeeServices).toHaveBeenCalledWith('emp-1');
  });

  it('pre-selektuje bieżącą usługę wizyty', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['serviceIds']()).toEqual(['svc-1']);
    expect(component['canSubmit']()).toBe(true);
  });

  it('pre-selektuje pełny istniejący skład combo', async () => {
    appointmentsClientMock.getAppointmentById.mockReturnValue(
      of({
        id: 'a1',
        employeeId: 'emp-1',
        serviceId: 'svc-1',
        services: [
          { serviceId: 'svc-1', serviceName: 'Strzyżenie', durationMinutes: 30, position: 0 },
          { serviceId: 'svc-extra', serviceName: 'Modelowanie', durationMinutes: 20, position: 1 },
        ],
      } as AppointmentDto),
    );
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['serviceIds']()).toEqual(['svc-1', 'svc-extra']);
  });

  it('toggleService podmienia usługę i wysyła nowy skład', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();

    // Podmiana zabiegu: usuń oryginalną, dodaj inną.
    component['toggleService']('svc-1');
    component['toggleService']('svc-3');
    expect(component['serviceIds']()).toEqual(['svc-3']);

    let emittedId: string | null = null;
    component.success.subscribe((id) => (emittedId = id));
    component['onSubmit']();
    expect(appointmentsClientMock.changeAppointmentServices).toHaveBeenCalledWith(
      'a1',
      expect.objectContaining({ serviceIds: ['svc-3'] }),
    );
    expect(emittedId).toBe('a1');
    expect(messagesMock.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success' }),
    );
  });

  it('błąd 409 ustawia komunikat z podpowiedzią o zmianie terminu', async () => {
    appointmentsClientMock.changeAppointmentServices.mockReturnValue(
      throwError(() => ({ status: 409 })),
    );
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['onSubmit']();
    expect(component['submitError']()).toContain('Zmień termin');
  });

  it('canSubmit false gdy brak wybranej usługi', async () => {
    // Pracownik bez usług → brak preselectu, brak opcji.
    employeesClientMock.getEmployeeServices.mockReturnValue(of([]));
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['serviceIds']()).toEqual([]);
    expect(component['canSubmit']()).toBe(false);
  });
});
