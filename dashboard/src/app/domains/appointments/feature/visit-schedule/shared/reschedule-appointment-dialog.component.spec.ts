import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { MessageService } from 'primeng/api';
import {
  AppointmentDto,
  AppointmentPreviewDto,
  AppointmentsClient,
  AppointmentSlotDto,
  EmployeesClient,
  ServiceCategoriesClient,
  ServicesClient,
} from '@core/api/api-client';
import { RescheduleAppointmentDialogComponent } from './reschedule-appointment-dialog.component';

describe('RescheduleAppointmentDialogComponent', () => {
  let fixture: ComponentFixture<RescheduleAppointmentDialogComponent>;
  let component: RescheduleAppointmentDialogComponent;

  let appointmentsClientMock: {
    getAppointmentById: ReturnType<typeof vi.fn>;
    getAvailableSlots: ReturnType<typeof vi.fn>;
    getMonthAvailability: ReturnType<typeof vi.fn>;
    rescheduleAppointment: ReturnType<typeof vi.fn>;
  };
  let employeesClientMock: {
    getEmployees: ReturnType<typeof vi.fn>;
    getEmployeeServices: ReturnType<typeof vi.fn>;
  };
  let servicesClientMock: {
    getServices: ReturnType<typeof vi.fn>;
  };
  let messagesMock: { add: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    appointmentsClientMock = {
      // Preview-dto nie ma `serviceId` — dialog fetchuje pełen AppointmentDto, by serwis dla
      // `getAvailableSlots` + `rescheduleAppointment` był znany.
      getAppointmentById: vi.fn().mockReturnValue(
        of({ id: 'a1', employeeId: 'emp-1', serviceId: 'svc-1' } as AppointmentDto),
      ),
      getAvailableSlots: vi.fn().mockReturnValue(
        of([
          { slot: '10:00:00', isPreferred: false },
          { slot: '11:30:00', isPreferred: true },
        ] as AppointmentSlotDto[]),
      ),
      // Konsumowany przez `app-appointment-date-picker` — w testach pustą listą zaspokajamy
      // rxResource bez efektów ubocznych.
      getMonthAvailability: vi.fn().mockReturnValue(of([])),
      rescheduleAppointment: vi.fn().mockReturnValue(of('a1')),
    };

    // emp-1 oferuje svc-1 (oryginalna usługa) + svc-extra; emp-2 — tylko svc-2.
    // Pozwala to przetestować preselect oryginalnej usługi i fallback na pierwszą po zmianie pracownika.
    employeesClientMock = {
      getEmployees: vi.fn().mockReturnValue(
        of([
          { id: 'emp-1', firstName: 'Ana', lastName: 'Kowalska' },
          { id: 'emp-2', firstName: 'Bartek', lastName: 'Nowak' },
        ]),
      ),
      getEmployeeServices: vi.fn().mockImplementation((empId: string) => {
        if (empId === 'emp-1') {
          // Odwrotnie do katalogu celowo: `getEmployeeServices` nie ma OrderBy na backendzie,
          // więc kolejność chipów musi wynikać z katalogu, nie z listy przypisań.
          return of([{ serviceId: 'svc-extra' }, { serviceId: 'svc-1' }]);
        }
        if (empId === 'emp-2') {
          return of([{ serviceId: 'svc-2' }]);
        }
        return of([]);
      }),
    };

    servicesClientMock = {
      getServices: vi.fn().mockReturnValue(
        of([
          { id: 'svc-1', name: 'Strzyżenie', durationInMinutes: 30 },
          { id: 'svc-extra', name: 'Modelowanie', durationInMinutes: 20 },
          { id: 'svc-2', name: 'Pasemka', durationInMinutes: 40 },
        ]),
      ),
    };

    messagesMock = { add: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [RescheduleAppointmentDialogComponent],
      providers: [
        { provide: AppointmentsClient, useValue: appointmentsClientMock },
        { provide: EmployeesClient, useValue: employeesClientMock },
        { provide: ServicesClient, useValue: servicesClientMock },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
        },
        { provide: MessageService, useValue: messagesMock },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(RescheduleAppointmentDialogComponent);
    component = fixture.componentInstance;
  });

  function setAppointment(p: Partial<AppointmentPreviewDto> | null): void {
    fixture.componentRef.setInput('appointment', p as AppointmentPreviewDto | null);
    fixture.detectChanges();
  }

  it('isVisible false gdy appointment null', () => {
    setAppointment(null);
    expect(
      (component as unknown as { isVisible: () => boolean }).isVisible(),
    ).toBe(false);
  });

  it('po otwarciu fetchuje detail i sloty', async () => {
    setAppointment({
      id: 'a1',
      employeeId: 'emp-1',
      date: '2026-05-20' as unknown as Date,
      startTime: '09:00:00',
      endTime: '10:00:00',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(appointmentsClientMock.getAppointmentById).toHaveBeenCalledWith('a1');
    expect(appointmentsClientMock.getAvailableSlots).toHaveBeenCalled();
    const slots = component['slotsResource'].value();
    expect(slots).toHaveLength(2);
  });

  it('po otwarciu pre-selektuje oryginalnego pracownika i usługę', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['employeeId']()).toBe('emp-1');
    expect(component['serviceIds']()).toEqual(['svc-1']);
  });

  /**
   * REGRESJA: kolejność chipów idzie za katalogiem (OrderIndex, Name), nie za listą przypisań —
   * mock dla emp-1 zwraca je odwrotnie. Sekcje kategorii: patrz `service-category-groups.util`.
   */
  it('renderuje chipy w kolejności katalogu mimo odwrotnych przypisań', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();

    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="reschedule-service-chips"] button'),
    ).map((el) => (el as HTMLElement).textContent?.trim());

    expect(labels).toEqual(['Strzyżenie', 'Modelowanie']);
  });

  it('zmiana pracownika czyści usługi i auto-wybiera pierwszą z nowych opcji', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['onEmployeeChange']('emp-2');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['employeeId']()).toBe('emp-2');
    expect(component['serviceIds']()).toEqual(['svc-2']);
    expect(component['selectedSlot']()).toBeNull();
  });

  it('combo: pre-selektuje istniejący skład i pozwala go edytować (reguła grup)', async () => {
    // emp-1 oferuje svc-1 + svc-extra → obie usługi combo są w opcjach.
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

    // Pre-select = pełny istniejący skład combo.
    expect(component['serviceIds']()).toEqual(['svc-1', 'svc-extra']);

    // Edytowalność: usunięcie i ponowne dodanie usługi.
    component['toggleService']('svc-extra');
    expect(component['serviceIds']()).toEqual(['svc-1']);
    component['toggleService']('svc-extra');
    expect(component['serviceIds']()).toEqual(['svc-1', 'svc-extra']);

    // Submit wysyła pełny (edytowany) skład.
    component['selectedSlot'].set('10:00:00');
    component['onSubmit']();
    expect(appointmentsClientMock.rescheduleAppointment).toHaveBeenCalledWith(
      'a1',
      expect.objectContaining({ serviceIds: ['svc-1', 'svc-extra'] }),
    );
  });

  it('prefill niestandardowego czasu i wysyłka customDurationMinutes', async () => {
    appointmentsClientMock.getAppointmentById.mockReturnValue(
      of({
        id: 'a1',
        employeeId: 'emp-1',
        serviceId: 'svc-1',
        services: [{ serviceId: 'svc-1', durationMinutes: 30, position: 0 }],
        customDurationMinutes: 45,
      } as AppointmentDto),
    );
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();

    // Standard = 30 (z opcji usług), override prefill = 45 → efektywny 45.
    expect(component['standardDurationMinutes']()).toBe(30);
    expect(component['customDurationMinutes']()).toBe(45);
    expect(component['effectiveDurationMinutes']()).toBe(45);

    component['selectedSlot'].set('10:00:00');
    component['onSubmit']();
    expect(appointmentsClientMock.rescheduleAppointment).toHaveBeenCalledWith(
      'a1',
      expect.objectContaining({ customDurationMinutes: 45 }),
    );
  });

  it('selectSlot zapisuje wybrany slot i pozwala na submit', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '10:00:00' } as AppointmentSlotDto);
    expect(component['selectedSlot']()).toBe('10:00:00');
    expect(component['canSubmit']()).toBe(true);
  });

  it('canSubmit false bez selected slot', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['canSubmit']()).toBe(false);
  });

  it('onSubmit wywołuje rescheduleAppointment z poprawnym body', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '11:30:00' } as AppointmentSlotDto);
    let emittedId: string | null = null;
    component.success.subscribe((id) => (emittedId = id));
    component['onSubmit']();
    expect(appointmentsClientMock.rescheduleAppointment).toHaveBeenCalledWith(
      'a1',
      expect.objectContaining({
        employeeId: 'emp-1',
        serviceIds: ['svc-1'],
        startTime: '11:30:00',
        // Backend DateOnly oczekuje `yyyy-MM-dd` — wysyłamy stringa, NIE Date
        // (toISOString daje pełen ISO timestamp z UTC-offsetem → walidacja 400).
        date: '2026-05-20',
      }),
    );
    expect(emittedId).toBe('a1');
    expect(messagesMock.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success' }),
    );
  });

  it('onSubmit po zmianie pracownika+usługi wysyła NOWE wartości', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['onEmployeeChange']('emp-2');
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '10:00:00' } as AppointmentSlotDto);
    component['onSubmit']();
    expect(appointmentsClientMock.rescheduleAppointment).toHaveBeenCalledWith(
      'a1',
      expect.objectContaining({
        employeeId: 'emp-2',
        serviceIds: ['svc-2'],
        startTime: '10:00:00',
        date: '2026-05-20',
      }),
    );
  });

  it('błąd 409 ustawia komunikat o zajętym slocie', async () => {
    appointmentsClientMock.rescheduleAppointment.mockReturnValue(
      throwError(() => ({ status: 409 })),
    );
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '10:00:00' } as AppointmentSlotDto);
    component['onSubmit']();
    expect(component['submitError']()).toContain('zajęty');
  });

  it('onSlotPicked z slot-picker-a ustawia selectedSlot', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    (component as unknown as { onSlotPicked: (v: string) => void }).onSlotPicked('11:30:00');
    expect(component['selectedSlot']()).toBe('11:30:00');
    expect(component['canSubmit']()).toBe(true);
  });

  it('zmiana daty resetuje wybrany slot', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '10:00:00' } as AppointmentSlotDto);
    expect(component['selectedSlot']()).toBe('10:00:00');
    component['onDateChange']('2026-05-21');
    expect(component['selectedSlot']()).toBeNull();
  });

  it('pusty string z child-pickera czyści internal newDate i slot', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '10:00:00' } as AppointmentSlotDto);
    component['onDateChange']('');
    expect(component['newDate']()).toBe('');
    expect(component['selectedSlot']()).toBeNull();
  });

  it('tryb poza grafikiem: ręczna godzina pozwala na submit i czyści wcześniejszy slot', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '10:00:00' } as AppointmentSlotDto);

    // Wejście w tryb czyści slot z grafiku.
    component['setOffScheduleMode'](true);
    expect(component['offScheduleMode']()).toBe(true);
    expect(component['selectedSlot']()).toBeNull();
    expect(component['canSubmit']()).toBe(false);

    // Ręczne wpisanie godziny (21:00) — poza godzinami pracy.
    const t = new Date();
    t.setHours(21, 0, 0, 0);
    component['onManualTimeChange'](t);
    expect(component['selectedSlot']()).toBe('21:00');
    expect(component['canSubmit']()).toBe(true);
  });

  it('tryb poza grafikiem: onSubmit wysyła ignoreSchedule=true', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['setOffScheduleMode'](true);
    const t = new Date();
    t.setHours(21, 30, 0, 0);
    component['onManualTimeChange'](t);
    component['onSubmit']();
    expect(appointmentsClientMock.rescheduleAppointment).toHaveBeenCalledWith(
      'a1',
      expect.objectContaining({ startTime: '21:30', ignoreSchedule: true }),
    );
  });

  it('w normalnym trybie body NIE zawiera ignoreSchedule', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectSlot']({ slot: '10:00:00' } as AppointmentSlotDto);
    component['onSubmit']();
    const body = appointmentsClientMock.rescheduleAppointment.mock.calls[0][1];
    expect(body.ignoreSchedule).toBeUndefined();
  });

  it('reset trybu poza grafikiem przy zmianie wizyty', async () => {
    setAppointment({ id: 'a1', employeeId: 'emp-1', date: '2026-05-20' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    component['setOffScheduleMode'](true);
    expect(component['offScheduleMode']()).toBe(true);
    setAppointment({ id: 'a2', employeeId: 'emp-1', date: '2026-05-22' as unknown as Date });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['offScheduleMode']()).toBe(false);
  });
});
