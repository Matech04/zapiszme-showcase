import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  API_BASE_URL,
  AppointmentsClient,
  CustomersClient,
  DepositsClient,
  EmployeesClient,
  SalonSettingsClient,
  ServiceCategoriesClient,
  ServicesClient,
  TenantDto,
} from '@core/api/api-client';
import { VisitScheduleComponent } from './visit-schedule.component';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { GuideProgressService } from '@core/guides/guide-progress.service';
import { LastScheduleEmployeeStore } from '@domains/appointments/data-access/last-schedule-employee.store';
import { ConfirmationService, MessageService } from 'primeng/api';

/**
 * Link „Kalendarz" w nawigacji prowadzi na gołe `/admin/schedule` (bez `:employeeId`), więc po
 * wyjściu do ustawień i powrocie komponent musiał wybrać pracownika sam. Brał pierwszego z listy,
 * gubiąc wybór użytkownika. Teraz wraca do ostatnio oglądanego — o ile ten nadal jest na liście.
 */
describe('VisitScheduleComponent — zapamiętany pracownik', () => {
  // Zimna kompilacja JIT ciężkiego komponentu pod obciążeniem bywa wolna (patrz deeplink.spec).
  vi.setConfig({ testTimeout: 15000 });

  let fixture: ComponentFixture<VisitScheduleComponent>;
  let routerMock: { navigate: ReturnType<typeof vi.fn>; events: Observable<unknown>; url: string };

  const EMPLOYEES = [
    { id: 'emp-1', firstName: 'Jan', lastName: 'Kowalski', email: 'jan@test.pl' },
    { id: 'emp-2', firstName: 'Ada', lastName: 'Nowak', email: 'ada@test.pl' },
  ];

  async function setup(opts: {
    employeeIdParam?: string | null;
    remembered?: string | null;
    employees?: typeof EMPLOYEES;
    scopedEmployeeId?: string | null;
  }): Promise<void> {
    globalThis.localStorage?.clear();
    // `events` jest wymagane przez pigułkę „Przewodnik" w nagłówku kalendarza.
    routerMock = { navigate: vi.fn().mockResolvedValue(true), events: of(), url: '/admin/schedule' };

    if (opts.remembered) {
      new LastScheduleEmployeeStore().save('user-1', opts.remembered);
    }

    const paramMap = convertToParamMap(
      opts.employeeIdParam == null ? {} : { employeeId: opts.employeeIdParam },
    );

    await TestBed.configureTestingModule({
      imports: [VisitScheduleComponent],
      providers: [
        { provide: HttpClient, useValue: { get: vi.fn().mockReturnValue(of([])) } },
        { provide: API_BASE_URL, useValue: 'http://test-api' },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployees: vi.fn().mockReturnValue(of(opts.employees ?? EMPLOYEES)),
            getEmployeeSchedules: vi.fn().mockReturnValue(of([])),
            getScheduleOverrides: vi.fn().mockReturnValue(of([])),
            getMonthPublications: vi.fn().mockReturnValue(of([])),
            getEmployeeLeaves: vi.fn().mockReturnValue(of([])),
          },
        },
        {
          provide: SalonSettingsClient,
          useValue: { get: vi.fn().mockReturnValue(of({ appointmentSlotStepMinutes: 10 } as TenantDto)) },
        },
        {
          provide: AppointmentsClient,
          useValue: {
            updateAppointmentStatus: vi.fn().mockReturnValue(of({})),
            updateAppointmentNote: vi.fn().mockReturnValue(of({})),
            getAppointmentById: vi.fn().mockReturnValue(of(null)),
            hasAnyAppointment: vi.fn().mockReturnValue(of(false)),
          },
        },
        {
          // Postęp przewodników na karcie „Zacznij tutaj" — mock SERWISU, nie klienta HTTP:
          // prawdziwy `load()` ustawiał sygnał po zniszczeniu fixture (NG0406 w innych plikach).
          provide: GuideProgressService,
          useValue: {
            load: vi.fn().mockResolvedValue(undefined),
            completed: signal(new Set<string>()),
            isLoaded: signal(true),
          },
        },
        {
          // Karta „Zacznij tutaj" bierze stąd slug — pusty stan trzyma link poza DOM-em.
          provide: OnboardingStateService,
          useValue: { state: signal(null) },
        },
        { provide: ServicesClient, useValue: { getServices: vi.fn().mockReturnValue(of([])) } },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
        },
        { provide: CustomersClient, useValue: { getCustomers: vi.fn().mockReturnValue(of([])) } },
        { provide: DepositsClient, useValue: { generateLink: () => of(null), send: () => of(null) } },
        {
          provide: AuthSessionService,
          useValue: {
            // 'employee' + brak team-policy = scoped; 'owner' widzi zespół.
            currentRole: vi.fn().mockReturnValue(opts.scopedEmployeeId ? 'employee' : 'owner'),
            currentEmployeeId: vi.fn().mockReturnValue(opts.scopedEmployeeId ?? null),
            currentUserId: vi.fn().mockReturnValue('user-1'),
            isHydrated: vi.fn().mockReturnValue(true),
            isDemo: vi.fn().mockReturnValue(false),
          },
        },
        { provide: MessageService, useValue: { add: vi.fn() } },
        { provide: ConfirmationService, useValue: { confirm: vi.fn() } },
        { provide: Router, useValue: routerMock },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(paramMap),
            queryParams: of({}),
            queryParamMap: of(convertToParamMap({})),
            snapshot: { queryParams: {} },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VisitScheduleComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const redirectTarget = (): string | undefined => {
    const call = routerMock.navigate.mock.calls.find(
      (c) => Array.isArray(c[0]) && c[0][0] === '/admin' && c[0][1] === 'schedule',
    );
    return call?.[0]?.[2] as string | undefined;
  };

  beforeEach(() => localStorage.clear());

  it('bez zapamiętanego wyboru wchodzi na pierwszego pracownika', async () => {
    await setup({ employeeIdParam: null });
    expect(redirectTarget()).toBe('emp-1');
  });

  it('goły /admin/schedule wraca do ostatnio oglądanego pracownika', async () => {
    await setup({ employeeIdParam: null, remembered: 'emp-2' });
    expect(redirectTarget()).toBe('emp-2');
  });

  it('utrwala wybór, gdy trasa niesie prawidłowy employeeId', async () => {
    await setup({ employeeIdParam: 'emp-2' });

    // Trasa jest już poprawna — nie przekierowujemy...
    expect(redirectTarget()).toBeUndefined();
    // ...ale zapamiętujemy wybór na następne wejście przez goły link.
    expect(new LastScheduleEmployeeStore().read('user-1')).toBe('emp-2');
  });

  it('pomija zapamiętanego pracownika, którego nie ma już na liście', async () => {
    // Np. zdeaktywowany albo z innego salonu (sesja wsparcia).
    await setup({ employeeIdParam: null, remembered: 'emp-nieistnieje' });
    expect(redirectTarget()).toBe('emp-1');
  });

  it('pracownik scoped nie zapisuje ani nie czyta wyboru (widzi tylko siebie)', async () => {
    await setup({ employeeIdParam: 'emp-1', scopedEmployeeId: 'emp-1' });
    expect(new LastScheduleEmployeeStore().read('user-1')).toBeNull();
  });
});
