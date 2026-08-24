import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
  API_BASE_URL,
  AppointmentPreviewDto,
  AppointmentsClient,
  CustomersClient,
  DepositsClient,
  EmployeesClient,
  SalonSettingsClient,
  ServiceCategoriesClient,
  ServicesClient,
  SlotGenerationMode,
  StaffCalendarVisibilityPolicy,
  TenantDto,
} from '@core/api/api-client';
import { VisitScheduleComponent } from './visit-schedule.component';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { GuideProgressService } from '@core/guides/guide-progress.service';
import { UserRole } from '@core/services/NavigationService';
import { ConfirmationService, MessageService } from 'primeng/api';

describe('VisitScheduleComponent', () => {
  let fixture: ComponentFixture<VisitScheduleComponent>;
  let component: VisitScheduleComponent;

  let employeesClientMock: {
    getEmployees: ReturnType<typeof vi.fn>;
    getEmployeeSchedules: ReturnType<typeof vi.fn>;
    getScheduleOverrides: ReturnType<typeof vi.fn>;
    getMonthPublications: ReturnType<typeof vi.fn>;
    getEmployeeLeaves: ReturnType<typeof vi.fn>;
  };
  let salonClientMock: { get: ReturnType<typeof vi.fn> };
  let appointmentsClientMock: {
    updateAppointmentStatus: ReturnType<typeof vi.fn>;
    updateAppointmentNote: ReturnType<typeof vi.fn>;
    getAppointmentById: ReturnType<typeof vi.fn>;
    hasAnyAppointment: ReturnType<typeof vi.fn>;
  };
  let guideProgressMock: {
    load: ReturnType<typeof vi.fn>;
    completed: WritableSignal<ReadonlySet<string>>;
    isLoaded: WritableSignal<boolean>;
  };
  // `currentRole` musi być SYGNAŁEM, nie vi.fn — komponent czyta go w `computed()` (canSeeTeam),
  // a zwykły mock nie unieważnia cache'u, więc podmiana roli po utworzeniu komponentu nic nie dawała.
  let roleSignal: WritableSignal<UserRole>;
  let authSessionMock: {
    currentRole: () => UserRole;
    currentEmployeeId: ReturnType<typeof vi.fn>;
    currentUserId: ReturnType<typeof vi.fn>;
    isHydrated: ReturnType<typeof vi.fn>;
  };
  let httpMock: { get: ReturnType<typeof vi.fn> };
  let routerMock: { navigate: ReturnType<typeof vi.fn>; events: Observable<unknown>; url: string };

  beforeEach(async () => {
    // F3.5: CalendarStateService persystuje view/statuses w localStorage. Bez czyszczenia
    // wartość zapisana przez wcześniejszy test trzymałaby się i wymuszała inny `view`.
    globalThis.localStorage?.clear();
    employeesClientMock = {
      getEmployees: vi
        .fn()
        .mockReturnValue(
          of([{ id: 'emp-1', firstName: 'Jan', lastName: 'Kowalski', email: 'jan@test.pl' }]),
        ),
      // Realny api-client.ts:2199 — komponent woła to w rxResource (visit-schedule.component.ts:866).
      // Stary `getWeeklySchedule` został zastąpiony tym endpointem.
      getEmployeeSchedules: vi.fn().mockReturnValue(of([])),
      // Dni specjalne + urlopy używane w `resolveWorkingRangesForDate` — bez mocków
      // rxResource leci na realny client (brak metody).
      getScheduleOverrides: vi.fn().mockReturnValue(of([])),
      getMonthPublications: vi.fn().mockReturnValue(of([])),
      getEmployeeLeaves: vi.fn().mockReturnValue(of([])),
    };
    salonClientMock = {
      get: vi.fn().mockReturnValue(
        of({
          appointmentSlotStepMinutes: 10,
        } as TenantDto),
      ),
    };
    appointmentsClientMock = {
      updateAppointmentStatus: vi.fn().mockReturnValue(of({})),
      updateAppointmentNote: vi.fn().mockReturnValue(of({})),
      getAppointmentById: vi.fn().mockReturnValue(of(null)),
      // Karta „Zacznij tutaj" (montowana na górze kalendarza dla owner/manager) pyta o to.
      hasAnyAppointment: vi.fn().mockReturnValue(of(false)),
    };
    guideProgressMock = {
      load: vi.fn().mockResolvedValue(undefined),
      completed: signal(new Set<string>()),
      isLoaded: signal(true),
    };
    roleSignal = signal<UserRole>('employee');
    authSessionMock = {
      currentRole: roleSignal,
      currentEmployeeId: vi.fn().mockReturnValue('emp-1'),
      currentUserId: vi.fn().mockReturnValue('user-1'),
      // Sesja zahydratowana — inaczej `canFetchScheduleConfigFor` jest fail-closed.
      isHydrated: vi.fn().mockReturnValue(true),
    };
    httpMock = {
      get: vi.fn().mockReturnValue(of([])),
    };
    // `events` jest wymagane przez pigułkę „Przewodnik" w nagłówku kalendarza — przelicza
    // dostępne przewodniki po każdej nawigacji. Pusty strumień = brak przeliczeń w teście.
    routerMock = { navigate: vi.fn().mockResolvedValue(true), events: of(), url: '/admin/schedule' };

    await TestBed.configureTestingModule({
      imports: [VisitScheduleComponent],
      providers: [
        { provide: HttpClient, useValue: httpMock },
        { provide: API_BASE_URL, useValue: 'http://test-api' },
        { provide: EmployeesClient, useValue: employeesClientMock },
        { provide: SalonSettingsClient, useValue: salonClientMock },
        { provide: AppointmentsClient, useValue: appointmentsClientMock },
        // Karta „Zacznij tutaj" bierze stąd slug i wybór grafiku z kreatora. Pusty stan =
        // brak linku; mockujemy serwis, nie `OnboardingClient`, żeby nie wciągać HTTP do fixture.
        { provide: OnboardingStateService, useValue: { state: signal(null) } },
        // Postęp przewodników mockujemy na poziomie SERWISU, nie GuidesClient: prawdziwy
        // `load()` rozwiązuje się asynchronicznie i ustawiał sygnał po zniszczeniu fixture,
        // wywracając NG0406 w kolejnych plikach testowych.
        { provide: GuideProgressService, useValue: guideProgressMock },
        // CreateAppointmentDrawer (rendered w template) injektuje ServicesClient + CustomersClient
        // — bez mocków template-fixture wywala NG0201 przy createComponent.
        { provide: ServicesClient, useValue: { getServices: vi.fn().mockReturnValue(of([])) } },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
        },
        { provide: CustomersClient, useValue: { getCustomers: vi.fn().mockReturnValue(of([])) } },
        // AppointmentDetailSheet (w template) injektuje DepositsClient — bez mocka NG0201.
        { provide: DepositsClient, useValue: { generateLink: () => of(null), send: () => of(null) } },
        { provide: AuthSessionService, useValue: authSessionMock },
        { provide: MessageService, useValue: { add: vi.fn() } },
        // Sheet wywołuje `confirmationService.confirm()` przy „Anuluj wizytę" — mock.
        { provide: ConfirmationService, useValue: { confirm: vi.fn() } },
        { provide: Router, useValue: routerMock },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ employeeId: 'emp-1' })),
            // CalendarStateService synchronizuje stan z query params — wymaga obu pól.
            queryParams: of({}),
            // visit-schedule.component.ts:812 — `openNewParam` czyta query param `new`.
            queryParamMap: of(convertToParamMap({})),
            snapshot: { queryParams: {} },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VisitScheduleComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('tworzy komponent', () => {
    expect(component).toBeTruthy();
  });

  // Regresja incydentu 2026-06: kafel „Zatwierdź"/„Anuluj" sąsiadują; po szybkim potwierdzeniu
  // „Anuluj" wskakiwał na miejsce zniknionego „Zatwierdź" i podwójny tap anulował świeżo
  // potwierdzoną wizytę (SMS potwierdzenia + anulowania w tej samej sekundzie, zwolniony termin).
  describe('quickCancel — potwierdzenie przed anulowaniem', () => {
    const confirmMock = () =>
      TestBed.inject(ConfirmationService) as unknown as { confirm: ReturnType<typeof vi.fn> };

    it('nie anuluje od razu — pokazuje dialog, anuluje dopiero po akceptacji (status Canceled=5)', () => {
      const confirm = confirmMock();

      component.quickCancel('apt-1');

      expect(confirm.confirm).toHaveBeenCalledTimes(1);
      expect(appointmentsClientMock.updateAppointmentStatus).not.toHaveBeenCalled();

      const opts = confirm.confirm.mock.calls[0][0] as { accept: () => void };
      opts.accept();
      expect(appointmentsClientMock.updateAppointmentStatus).toHaveBeenCalledWith('apt-1', 5);
    });

    it('odrzucenie dialogu (brak akceptacji) NIE anuluje wizyty', () => {
      const confirm = confirmMock();
      component.quickCancel('apt-1');
      expect(confirm.confirm).toHaveBeenCalledTimes(1);
      // Nie wołamy accept() → nic się nie dzieje.
      expect(appointmentsClientMock.updateAppointmentStatus).not.toHaveBeenCalled();
    });

    it('quickConfirm zatwierdza od razu bez dialogu (status Booked=2)', () => {
      const confirm = confirmMock();
      component.quickConfirm('apt-1');
      expect(confirm.confirm).not.toHaveBeenCalled();
      expect(appointmentsClientMock.updateAppointmentStatus).toHaveBeenCalledWith('apt-1', 2);
    });
  });

  describe('pendingScope — baner „do potwierdzenia"', () => {
    const scope = () =>
      (component as unknown as { pendingScope(): string | undefined }).pendingScope();

    it('pracownik: liczy tylko własne wizyty', () => {
      expect(scope()).toBe('emp-1');
    });

    it('właścicielka: też tylko własne (spójnie z osobistym dzwonkiem)', () => {
      roleSignal.set('owner');
      expect(scope()).toBe('emp-1');
    });

    it('kiosk: cały salon (sentinel, bez employeeId)', () => {
      roleSignal.set('kiosk');
      expect(scope()).toBe('__desk__');
    });

    it('przed hydratacją sesji nie pyta wcale', () => {
      authSessionMock.isHydrated.mockReturnValue(false);
      expect(scope()).toBeUndefined();
    });

    // Przypadek „konto bez powiązanego pracownika" pokrywa NotificationCenterService.spec —
    // tu `currentEmployeeId` idzie przez `computed()`, który cache'uje wartość z czasu tworzenia
    // komponentu, więc podmiana mocka po fakcie nic nie zmienia.
  });

  describe('canFetchScheduleConfigFor — konfiguracja grafiku (403 guard)', () => {
    const canFetch = (id: string | undefined) =>
      (component as unknown as { canFetchScheduleConfigFor(id: string | undefined): boolean })
        .canFetchScheduleConfigFor(id);

    /** `canSeeTeam` zależy od zasobu `salonSettings` — przeliczamy go po podmianie polityki. */
    const applyPolicy = async (policy: StaffCalendarVisibilityPolicy) => {
      salonClientMock.get.mockReturnValue(
        of({ appointmentSlotStepMinutes: 15, staffCalendarVisibilityPolicy: policy } as TenantDto),
      );
      component.salonSettings.reload();
      await fixture.whenStable();
      fixture.detectChanges();
    };

    it('OwnCalendarOnly: własny grafik tak, cudzy nie', async () => {
      await applyPolicy(StaffCalendarVisibilityPolicy.OwnCalendarOnly);
      expect(canFetch('emp-1')).toBe(true);
      expect(canFetch('emp-2')).toBe(false);
    });

    it('TeamFull: pracownik pobiera też grafik kolegi (inaczej kalendarz kłamie o dniu wolnym)', async () => {
      await applyPolicy(StaffCalendarVisibilityPolicy.TeamFull);
      expect(canFetch('emp-2')).toBe(true);
    });

    it('TeamReadOnly: pracownik też widzi grafik kolegi', async () => {
      await applyPolicy(StaffCalendarVisibilityPolicy.TeamReadOnly);
      expect(canFetch('emp-2')).toBe(true);
    });

    it('owner: dowolny pracownik', async () => {
      roleSignal.set('owner');
      await applyPolicy(StaffCalendarVisibilityPolicy.OwnCalendarOnly);
      expect(canFetch('emp-2')).toBe(true);
    });

    it('fail-closed przed hydratacją sesji — currentRole() zwraca wtedy fallback "owner"', () => {
      authSessionMock.isHydrated.mockReturnValue(false);
      roleSignal.set('owner'); // fallback z mapRoles([])
      expect(canFetch('emp-2')).toBe(false);
      expect(canFetch('emp-1')).toBe(false);
    });
  });

  it('appointmentSlotStepMinutes pracownik czyta z salonSettings (F2.3: GET dostępny dla każdej roli)', () => {
    // Mock w `beforeEach` zwraca `appointmentSlotStepMinutes: 10` — pracownik dostaje
    // wartość salonową, a nie fallback 15. Przed F2.3 endpoint był blokowany dla Employee.
    expect(component.appointmentSlotStepMinutes()).toBe(10);
  });

  it('appointmentSlotStepMinutes przy nieobsługiwanej wartości zwraca 15', async () => {
    salonClientMock.get.mockReturnValue(of({ appointmentSlotStepMinutes: 7 } as TenantDto));
    component.salonSettings.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component.appointmentSlotStepMinutes()).toBe(15);
  });

  it('nie nawiguje na innego pracownika gdy URL pasuje do listy', () => {
    // `CalendarStateService` synchronizuje URL z query params (navigate z `[]`) — to nie jest
    // redirect na innego pracownika. Sprawdzamy tylko, że nie wywołano redirectu na ścieżkę.
    const employeeRedirects = routerMock.navigate.mock.calls.filter(
      ([commands]) =>
        Array.isArray(commands) && commands[0] === '/admin' && commands[1] === 'schedule',
    );
    expect(employeeRedirects).toHaveLength(0);
  });

  it('ładuje wizyty dnia — pracownik scoped na desktopie idzie ścieżką kolumn (bez employeeId w query)', () => {
    expect(httpMock.get).toHaveBeenCalled();
    const calls = httpMock.get.mock.calls as Array<[string, { params: Record<string, string> }]>;
    // Zapytanie pojedynczego dnia: startDate === endDate.
    const dayCall = calls.find(
      ([url, o]) => url.includes('/api/Appointments') && o.params['startDate'] === o.params['endDate'],
    );
    expect(dayCall).toBeDefined();
    const [, opts] = dayCall!;
    expect(opts.params['startDate']).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    // Pracownik scoped (OwnCalendarOnly) na desktopie używa widoku kolumn (jak właściciel solo),
    // więc query NIE zawiera employeeId — zakres jest scope'owany polityką po stronie API
    // + filtrem kolumny (columnEmployees = własny pracownik). Na mobile (single-col) employeeId wraca.
    expect(opts.params['employeeId']).toBeUndefined();
  });

  it('F2.3: Employee + OwnCalendarOnly policy → isEmployeeScoped=true, canSeeTeam=false', async () => {
    salonClientMock.get.mockReturnValue(
      of({
        appointmentSlotStepMinutes: 15,
        staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.OwnCalendarOnly,
      } as TenantDto),
    );
    component.salonSettings.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['isEmployeeScoped']()).toBe(true);
    expect(component['canSeeTeam']()).toBe(false);
    expect(component['canMutateOthers']()).toBe(false);
  });

  it('F2.3: Employee + TeamReadOnly → canSeeTeam=true, canMutateOthers=false', async () => {
    salonClientMock.get.mockReturnValue(
      of({
        appointmentSlotStepMinutes: 15,
        staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.TeamReadOnly,
      } as TenantDto),
    );
    component.salonSettings.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['isEmployeeScoped']()).toBe(false);
    expect(component['canSeeTeam']()).toBe(true);
    expect(component['canMutateOthers']()).toBe(false);
  });

  it('F2.3: Employee + TeamFull → wszystko otwarte', async () => {
    salonClientMock.get.mockReturnValue(
      of({
        appointmentSlotStepMinutes: 15,
        staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.TeamFull,
      } as TenantDto),
    );
    component.salonSettings.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['canSeeTeam']()).toBe(true);
    expect(component['canMutateOthers']()).toBe(true);
  });

  it('F2.3: canMutateAppointment — Employee z TeamReadOnly nie mutuje cudzych', async () => {
    salonClientMock.get.mockReturnValue(
      of({
        appointmentSlotStepMinutes: 15,
        staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.TeamReadOnly,
      } as TenantDto),
    );
    component.salonSettings.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['canMutateAppointment']({ id: 'a1', employeeId: 'emp-1' })).toBe(true);
    expect(component['canMutateAppointment']({ id: 'a2', employeeId: 'other-emp' })).toBe(false);
  });

  it('F2.3: canMutateAppointment — Employee z TeamFull mutuje wszystkie', async () => {
    salonClientMock.get.mockReturnValue(
      of({
        appointmentSlotStepMinutes: 15,
        staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.TeamFull,
      } as TenantDto),
    );
    component.salonSettings.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['canMutateAppointment']({ id: 'a2', employeeId: 'other-emp' })).toBe(true);
  });

  it('F2.4: tap w kafelek miesiąca ustawia previewedDay i NIE przechodzi w widok day', () => {
    const before = component['viewMode']();
    const target = new Date(2026, 4, 20);
    component.onMonthCellClick(target);
    expect(component['previewedDay']()?.getTime()).toBe(target.getTime());
    // viewMode nie zmienia się od razu — sheet jest między tap-em a drill-down do timeline.
    expect(component['viewMode']()).toBe(before);
  });

  it('F2.4: onPreviewedDayOpenDay zamyka sheet i przechodzi na widok day', () => {
    component.onMonthCellClick(new Date(2026, 4, 20));
    component['onPreviewedDayOpenDay'](new Date(2026, 4, 20));
    expect(component['previewedDay']()).toBeNull();
    expect(component['viewMode']()).toBe('day');
  });

  it('F2.4: tap wizyty z sheet-a otwiera appointment-detail-sheet (selectedAppointment)', () => {
    component.onMonthCellClick(new Date(2026, 4, 20));
    const a = { id: 'app-1' } as AppointmentPreviewDto;
    component['onPreviewedDayPick'](a);
    expect(component['previewedDay']()).toBeNull();
    expect(component['selectedAppointment']()?.id).toBe('app-1');
  });

  it('F2.4: previewedDayAppointments filtruje monthAppointments wg dnia', async () => {
    // monthAppointments fetchuje tylko w view='month'; ustawiamy go i mockujemy http.
    httpMock.get.mockReturnValue(
      of([
        { id: 'x1', date: '2026-05-20', startTime: '10:00:00', endTime: '11:00:00' },
        { id: 'x2', date: '2026-05-21', startTime: '14:00:00', endTime: '15:00:00' },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['viewMode'].set('month');
    await fixture.whenStable();
    fixture.detectChanges();
    component.onMonthCellClick(new Date(2026, 4, 20));
    // Klik zmienia selectedMonthAnchor (→ maj) i przeładowuje monthAppointments asynchronicznie;
    // bez stabilizacji previewedDayAppointments czyta jeszcze pusty/poprzedni wynik.
    await fixture.whenStable();
    fixture.detectChanges();
    const list = component['previewedDayAppointments']();
    expect(list.map((a: AppointmentPreviewDto) => a.id)).toEqual(['x1']);
  });

  it('F2.5: dayStripCountFor zwraca total + pending dla wybranego dnia', async () => {
    httpMock.get.mockReturnValue(
      of([
        {
          id: 'x1',
          date: '2026-05-20',
          startTime: '09:00:00',
          endTime: '10:00:00',
          status: { name: 'Pending' },
        },
        {
          id: 'x2',
          date: '2026-05-20',
          startTime: '11:00:00',
          endTime: '12:00:00',
          status: { name: 'Booked' },
        },
        {
          id: 'x3',
          date: '2026-05-22',
          startTime: '14:00:00',
          endTime: '15:00:00',
          status: { name: 'Booked' },
        },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component.dayStripAppointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    const counts = component['dayStripCountFor'](new Date(2026, 4, 20));
    expect(counts.total).toBe(2);
    expect(counts.pending).toBe(1);
    const other = component['dayStripCountFor'](new Date(2026, 4, 22));
    expect(other.total).toBe(1);
    expect(other.pending).toBe(0);
    const empty = component['dayStripCountFor'](new Date(2026, 4, 25));
    expect(empty.total).toBe(0);
  });

  /** Grafik tygodniowy STATYCZNY (FixedStartTimes) Pn–Pt z podanymi godzinami startu. */
  function fixedSchedule(
    times: string[],
    activeFrom = new Date(2026, 0, 4),
    activeTo = new Date(2030, 0, 1),
  ): unknown {
    return {
      activeFrom,
      activeTo,
      numberOfCycles: 1,
      slotGenerationMode: SlotGenerationMode.FixedStartTimes,
      days: [1, 2, 3, 4, 5].map((cycleIndex) => ({
        cycleIndex,
        workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
        breaks: [],
        fixedStartTimes: times,
      })),
    };
  }

  // Przyszły poniedziałek względem zamrożonego „dziś" (2026-06-05), żeby uniknąć zależności
  // od pory dnia/daty uruchomienia. `freeStaticSlots` czyta `new Date()` na żywo i filtruje
  // sloty z przeszłości — bez zamrożenia czasu testy gniły, gdy realna data dogoniła 06-08.
  const futureMonday = new Date(2026, 5, 8); // 2026-06-08

  /** Zamraża zegar na pt 2026-06-05 (tylko Date — settery/rxjs zostają realne, więc whenStable działa). */
  function pinToday(): void {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 5, 5, 8, 0, 0));
  }

  afterEach(() => {
    vi.useRealTimers();
  });

  it('freeStaticSlots: liczy wolne stałe sloty pomijając zajęte i anulowane (single-col)', async () => {
    pinToday();
    component['viewportWidth'].set(800); // wymuszamy single-col (isDesktop=false)
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00', '11:00:00', '12:00:00'])]),
    );
    httpMock.get.mockReturnValue(
      of([
        // zajmuje slot 10:00
        { id: 'a1', date: '2026-06-08', startTime: '10:00:00', endTime: '10:30:00', status: { name: 'Booked' } },
        // anulowana — slot 11:00 zostaje wolny
        { id: 'a2', date: '2026-06-08', startTime: '11:00:00', endTime: '11:30:00', status: { name: 'Canceled' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['selectedDate'].set(futureMonday);
    component.weeklySchedule.reload();
    component.appointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    // 09/10/11/12 → 10 zajęty, reszta wolna = 3
    expect(component.freeStaticSlots()).toBe(3);
  });

  it('selectedDayStaticSlots: kafelki wolnych terminów (start/end) pomijając zajęte', async () => {
    pinToday();
    component['viewportWidth'].set(800); // single-col
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00', '11:00:00', '12:00:00'])]),
    );
    httpMock.get.mockReturnValue(
      of([
        { id: 'a1', date: '2026-06-08', startTime: '10:00:00', endTime: '10:30:00', status: { name: 'Booked' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['selectedDate'].set(futureMonday);
    component.weeklySchedule.reload();
    component.appointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    // 09/11/12 wolne (10 zajęty). slotLen = min odstęp = 60 min → end = start + 60.
    expect(component.selectedDayStaticSlots()).toEqual([
      { startMin: 540, endMin: 600 },
      { startMin: 660, endMin: 720 },
      { startMin: 720, endMin: 780 },
    ]);
  });

  it('agendaNowIndex: dla dnia innego niż dziś → null (znacznik „Teraz" tylko dziś)', () => {
    component['viewportWidth'].set(800);
    component['selectedDate'].set(new Date(2026, 5, 8)); // przyszły poniedziałek — nie dziś
    fixture.detectChanges();
    expect(component.agendaNowIndex()).toBeNull();
  });

  it('agendaNowContainer: trwająca wizyta → linia „Teraz" nakłada się na kafelek, bez osobnego wiersza', async () => {
    pinToday(); // dziś = 2026-06-05 (pt)
    component['viewportWidth'].set(800); // single-col → wizyty z httpMock.get
    employeesClientMock.getEmployeeSchedules.mockReturnValue(of([gridWeekly()]));
    httpMock.get.mockReturnValue(
      of([
        { id: 'v1', date: '2026-06-05', startTime: '10:00:00', endTime: '11:00:00', status: { name: 'Booked' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['selectedDate'].set(new Date(2026, 5, 5));
    component['nowTick'].set(new Date(2026, 5, 5, 10, 30, 0).getTime()); // teraz 10:30 — w środku wizyty
    component.weeklySchedule.reload();
    component.appointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();

    const container = component['agendaNowContainer']();
    expect(container?.kind).toBe('visit');
    expect(component['agendaNowFraction']()).toBeCloseTo(0.5, 5); // 10:30 w [10:00, 11:00]
    expect(component.agendaNowIndex()).toBeNull(); // osobny wiersz zbędny — linię rysuje nakładka
  });

  it('agendaNowContainer: godzina w luce między pozycjami → brak nakładki, osobny wiersz', async () => {
    pinToday();
    component['viewportWidth'].set(800);
    employeesClientMock.getEmployeeSchedules.mockReturnValue(of([gridWeekly()]));
    httpMock.get.mockReturnValue(
      of([
        { id: 'v1', date: '2026-06-05', startTime: '09:00:00', endTime: '09:30:00', status: { name: 'Booked' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['selectedDate'].set(new Date(2026, 5, 5));
    component['nowTick'].set(new Date(2026, 5, 5, 10, 30, 0).getTime()); // teraz 10:30 — po wizycie, w luce
    component.weeklySchedule.reload();
    component.appointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component['agendaNowContainer']()).toBeNull();
    expect(component['agendaNowFraction']()).toBeNull();
    expect(component.agendaNowIndex()).not.toBeNull(); // znacznik jako osobny wiersz
  });

  it('freeStaticSlots: dzień miniony → 0 (nic już do zarezerwowania)', async () => {
    pinToday();
    component['viewportWidth'].set(800);
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00'])]),
    );
    component['selectedDate'].set(new Date(2026, 4, 11)); // przeszły poniedziałek
    component.weeklySchedule.reload();
    component.appointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component.freeStaticSlots()).toBe(0);
  });

  it('freeStaticSlots: grafik dynamiczny (Grid) → null (kafelek ukryty)', async () => {
    component['viewportWidth'].set(800);
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([
        {
          activeFrom: new Date(2026, 0, 4),
          activeTo: new Date(2030, 0, 1),
          numberOfCycles: 1,
          slotGenerationMode: SlotGenerationMode.Grid,
          days: [{ cycleIndex: 1, workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }], breaks: [] }],
        },
      ]),
    );
    component['selectedDate'].set(new Date(2026, 4, 11));
    component.weeklySchedule.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component.freeStaticSlots()).toBeNull();
  });

  it('freeStaticSlots: desktop pojedyncza kolumna (pracownik scoped) liczy wolne sloty', async () => {
    // beforeEach: role=employee bez team-policy → isEmployeeScoped, desktop = jedna własna kolumna.
    // Grafik czytany z zasobów `desktop*` (per-pracownik).
    pinToday();
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00', '11:00:00'])]),
    );
    httpMock.get.mockReturnValue(
      of([
        { id: 'a1', employeeId: 'emp-1', date: '2026-06-08', startTime: '09:00:00', endTime: '09:30:00', status: { name: 'Booked' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['selectedDate'].set(futureMonday);
    component.desktopWeeklySchedules.reload();
    component.appointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    // 09/10/11 → 09 zajęty (emp-1), 10 i 11 wolne = 2
    expect(component.freeStaticSlots()).toBe(2);
  });

  it('freeStaticSlots: wiele kolumn (zespół) → null', async () => {
    // TeamFull (reaktywnie przez salonSettings) → pracownik widzi cały zespół; 2 kolumny →
    // licznik niejednoznaczny → null. Rolę zostawiamy 'employee' z beforeEach (mock roli nie
    // jest sygnałem, więc nie przeliczyłby computed — policy z resource'a jest reaktywne).
    salonClientMock.get.mockReturnValue(
      of({
        appointmentSlotStepMinutes: 10,
        staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.TeamFull,
      } as TenantDto),
    );
    employeesClientMock.getEmployees.mockReturnValue(
      of([
        { id: 'emp-1', firstName: 'Jan', lastName: 'Kowalski' },
        { id: 'emp-2', firstName: 'Anna', lastName: 'Nowak' },
      ]),
    );
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00'])]),
    );
    component.salonSettings.reload();
    component.employees.reload();
    component['selectedDate'].set(new Date(2026, 4, 11));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component.freeStaticSlots()).toBeNull();
  });

  it('freeStaticSlots: widok TYGODNIA sumuje wolne sloty po dniach (Pn–Nd)', async () => {
    pinToday();
    component['viewportWidth'].set(800); // single-col → źródło grafiku weeklySchedule
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00'])]), // 2 sloty/dzień, Pn–Pt
    );
    // weekAppointments fetchuje w trybie 'week' — wizyta zajmuje slot 09:00 we wtorek.
    httpMock.get.mockReturnValue(
      of([
        { id: 'w1', date: '2026-06-09', startTime: '09:00:00', endTime: '09:30:00', status: { name: 'Booked' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['selectedDate'].set(futureMonday); // tydzień 2026-06-08 .. 06-14 (cały przyszły)
    component['viewMode'].set('week');
    component.weeklySchedule.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    // 5 dni roboczych × 2 sloty = 10; minus 1 zajęty = 9 (weekend bez grafiku → pomijany)
    expect(component.freeStaticSlots()).toBe(9);
  });

  it('freeStaticSlots: widok MIESIĄCA sumuje wolne sloty tylko z dni objętych grafikiem', async () => {
    pinToday();
    component['viewportWidth'].set(800);
    // Grafik statyczny aktywny tylko w jednym tygodniu lipca (Pn–Pt 2026-07-06..07-10).
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00', '11:00:00'], new Date(2026, 6, 6), new Date(2026, 6, 10))]),
    );
    httpMock.get.mockReturnValue(
      of([
        { id: 'm1', date: '2026-07-07', startTime: '09:00:00', endTime: '09:30:00', status: { name: 'Booked' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    component['selectedDate'].set(new Date(2026, 6, 6)); // lipiec → anchor = 2026-07-01
    component['viewMode'].set('month');
    component.weeklySchedule.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    // tylko 5 dni roboczych w oknie aktywności × 3 sloty = 15; minus 1 zajęty = 14
    expect(component.freeStaticSlots()).toBe(14);
  });

  it('monthStaticSlots: na DESKTOPIE czyta grafik z zasobów desktop* (regresja: puste sloty znikały)', async () => {
    // Miesiąc jest zawsze single-employee, ale layout wciąż może być `showDesktopColumns`.
    // W tym trybie zasoby single-col (`weeklySchedule` itd.) są celowo NIE pobierane (dedup
    // requestów), więc czytanie ich w `monthScheduleConfig` dawało pusty grafik → komórki bez
    // slotów wolnych/zajętych. Pozostałe testy miesiąca wymuszają viewportWidth=800, więc tego
    // nie łapały.
    pinToday();
    employeesClientMock.getEmployeeSchedules.mockReturnValue(
      of([fixedSchedule(['09:00:00', '10:00:00', '11:00:00'])]),
    );
    httpMock.get.mockReturnValue(of([] as unknown as AppointmentPreviewDto[]));
    component['selectedDate'].set(futureMonday);
    component['viewMode'].set('month');
    component.desktopWeeklySchedules.reload();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component['showDesktopColumns']()).toBe(true);
    const slots = component['monthStaticSlots']();
    expect(slots).not.toBeNull();
    expect(slots!.cfg.sched).toBeTruthy();
  });

  // ── Szybka przerwa (wiring w komponencie) ─────────────────────────────────

  /** Grafik tygodniowy DYNAMICZNY (Grid) Pn–Pt z opcjonalnymi pasami/przerwami. */
  function gridWeekly(
    workRanges: { startTime: string; endTime: string }[] = [{ startTime: '09:00:00', endTime: '17:00:00' }],
    breaks: { startTime: string; endTime: string }[] = [],
  ): unknown {
    return {
      activeFrom: new Date(2026, 0, 4),
      activeTo: new Date(2030, 0, 1),
      numberOfCycles: 1,
      slotGenerationMode: SlotGenerationMode.Grid,
      days: [1, 2, 3, 4, 5].map((cycleIndex) => ({ cycleIndex, workRanges, breaks })),
    };
  }

  async function loadSchedule(opts: {
    schedule?: unknown;
    overrides?: unknown[];
    date?: Date;
  }): Promise<void> {
    pinToday();
    component['viewportWidth'].set(800); // single-col → isSingleEmployeeContext
    employeesClientMock.getEmployeeSchedules.mockReturnValue(of([opts.schedule ?? gridWeekly()]));
    employeesClientMock.getScheduleOverrides.mockReturnValue(of(opts.overrides ?? []));
    component['selectedDate'].set(opts.date ?? futureMonday);
    component.weeklySchedule.reload();
    component.scheduleOverrides.reload();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('canAddBreakForSelectedDay: grafik Grid z pasem → true', async () => {
    await loadSchedule({ schedule: gridWeekly() });
    expect(component['canAddBreakForSelectedDay']()).toBe(true);
    expect(component['breakDisabledReason']()).toBe('');
  });

  it('canAddBreakForSelectedDay: grafik statyczny → false + tooltip o grafiku dynamicznym', async () => {
    await loadSchedule({ schedule: fixedSchedule(['09:00:00', '10:00:00']) });
    expect(component['canAddBreakForSelectedDay']()).toBe(false);
    expect(component['breakDisabledReason']()).toContain('dynamicznego');
  });

  it('canAddBreakForSelectedDay: dzień miniony → false', async () => {
    await loadSchedule({ schedule: gridWeekly(), date: new Date(2026, 4, 11) });
    expect(component['canAddBreakForSelectedDay']()).toBe(false);
    expect(component['breakDisabledReason']()).toContain('przeszłości');
  });

  it('breakSegments: luka pokrywająca się z przerwą jest usuwalna (breakRange != null)', async () => {
    await loadSchedule({
      schedule: gridWeekly(),
      overrides: [
        {
          date: futureMonday,
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
          breaks: [{ startTime: '12:00:00', endTime: '12:30:00' }],
        },
      ],
    });
    const seg = component['breakSegments']().find((s) => s.startMin === 720 && s.endMin === 750);
    expect(seg).toBeDefined();
    expect(seg!.breakRange).not.toBeNull();
  });

  it('breakSegments: krótka przerwa (15 min) JEST renderowana jako kafelek', async () => {
    // Regresja: stary próg ≥20 min ukrywał krótkie przerwy (domyślna = krok slotów, np. 15 min).
    await loadSchedule({
      schedule: gridWeekly(),
      overrides: [
        {
          date: futureMonday,
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '09:00:00', endTime: '17:00:00' }],
          breaks: [{ startTime: '12:00:00', endTime: '12:15:00' }],
        },
      ],
    });
    const seg = component['breakSegments']().find((s) => s.startMin === 720 && s.endMin === 735);
    expect(seg).toBeDefined();
    expect(seg!.breakRange).not.toBeNull();
  });

  it('breakSegments: przerwa na KOŃCU zmiany nie jest obcinana (regresja: 14–16 pokazywane jako 14–15)', async () => {
    // Prod bug: grafik 10–16 z przerwą 14–16. Pasy pracy mają wyciętą przerwę (10–14), więc okno
    // osi bez uwzględnienia przerwy kończyło się na ~15:00 i kafelek przerwy był clampowany do 14–15
    // („1 godz"), mimo że backend blokuje poprawnie 2h.
    await loadSchedule({
      schedule: gridWeekly(),
      overrides: [
        {
          date: futureMonday,
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '10:00:00', endTime: '16:00:00' }],
          breaks: [{ startTime: '14:00:00', endTime: '16:00:00' }],
        },
      ],
    });
    const seg = component['breakSegments']().find((s) => s.startMin === 840);
    expect(seg).toBeDefined();
    expect(seg!.endMin).toBe(960); // 16:00 — pełne 2h, nie obcięte do 15:00
  });

  it('agendaItems: przerwa kończąca zmianę jest NAD „Koniec pracy" (ta sama minuta 14:00)', async () => {
    await loadSchedule({
      schedule: gridWeekly(),
      overrides: [
        {
          date: futureMonday,
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '10:00:00', endTime: '16:00:00' }],
          breaks: [{ startTime: '14:00:00', endTime: '16:00:00' }],
        },
      ],
    });
    const items = component['agendaItems']();
    const breakIdx = items.findIndex((i) => i.kind === 'break' && i.startMin === 840);
    const workEndIdx = items.findIndex((i) => i.kind === 'work-end' && i.startMin === 840);
    expect(breakIdx).toBeGreaterThanOrEqual(0);
    expect(workEndIdx).toBeGreaterThanOrEqual(0);
    expect(breakIdx).toBeLessThan(workEndIdx);
  });

  it('currentDayWorkBandSegments: pas pracy z surowych godzin — przerwa NIE skraca grafiku na osi', async () => {
    // Regresja: pas pracy szedł z godzin z wyciętą przerwą (10–14), więc przerwa 14–16 „ucinała"
    // tło grafiku. Pas ma iść z SUROWYCH godzin (10–16), a kafelek przerwy leży na nim.
    await loadSchedule({
      schedule: gridWeekly(),
      overrides: [
        {
          date: futureMonday,
          slotGenerationMode: SlotGenerationMode.Grid,
          workRanges: [{ startTime: '10:00:00', endTime: '16:00:00' }],
          breaks: [{ startTime: '14:00:00', endTime: '16:00:00' }],
        },
      ],
    });
    // Pas grafiku: pełne 10–16 (600–960), jeden ciągły segment.
    expect(component['currentDayWorkBandSegments']()).toEqual([{ startMin: 600, endMin: 960 }]);
    // Pasy do oceny „poza grafikiem" nadal mają wyciętą przerwę (10–14).
    expect(component['currentDaySingleRanges']()).toEqual([{ startMin: 600, endMin: 840 }]);
  });

  it('breakSegments: split-shift bez przerw → brak bloków przerw (strukturę pokazuje pas pracy)', async () => {
    await loadSchedule({
      schedule: gridWeekly([
        { startTime: '09:00:00', endTime: '13:00:00' },
        { startTime: '14:00:00', endTime: '18:00:00' },
      ]),
    });
    expect(component['breakSegments']()).toHaveLength(0);
  });

  it('openBreakEditFor: ustawia breakContext w trybie edycji (editBreak)', async () => {
    await loadSchedule({ schedule: gridWeekly() });
    const brk = { startTime: '12:00:00', endTime: '12:30:00' };
    component.openBreakEditFor(brk);
    const ctx = component['breakContext']();
    expect(ctx).not.toBeNull();
    expect(ctx!.employeeId).toBe('emp-1');
    expect(ctx!.editBreak).toEqual(brk);
  });

  it('canAddBreakAfterSelected: wolny czas po wizycie → true', async () => {
    await loadSchedule({ schedule: gridWeekly() });
    component['selectedAppointment'].set({
      id: 'a1',
      employeeId: 'emp-1',
      date: futureMonday,
      startTime: '10:00:00',
      endTime: '10:30:00',
      status: { id: 2 },
    } as unknown as AppointmentPreviewDto);
    expect(component['canAddBreakAfterSelected']()).toBe(true);
  });

  it('canAddBreakAfterSelected: wizyta back-to-back zaraz po → false', async () => {
    httpMock.get.mockReturnValue(
      of([
        { id: 'a1', employeeId: 'emp-1', date: '2026-06-08', startTime: '10:00:00', endTime: '10:30:00', status: { name: 'Booked' } },
        { id: 'a2', employeeId: 'emp-1', date: '2026-06-08', startTime: '10:30:00', endTime: '11:00:00', status: { name: 'Booked' } },
      ] as unknown as AppointmentPreviewDto[]),
    );
    await loadSchedule({ schedule: gridWeekly() });
    component.appointments.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    component['selectedAppointment'].set({
      id: 'a1',
      employeeId: 'emp-1',
      date: futureMonday,
      endTime: '10:30:00',
      status: { id: 2 },
    } as unknown as AppointmentPreviewDto);
    expect(component['canAddBreakAfterSelected']()).toBe(false);
  });

  it('canAddBreakAfterSelected: grafik statyczny → false', async () => {
    await loadSchedule({ schedule: fixedSchedule(['09:00:00']) });
    component['selectedAppointment'].set({
      id: 'a1',
      employeeId: 'emp-1',
      date: futureMonday,
      endTime: '10:30:00',
      status: { id: 2 },
    } as unknown as AppointmentPreviewDto);
    expect(component['canAddBreakAfterSelected']()).toBe(false);
  });

  it('onAddBreakAfterSelected: breakContext z prefillem startu = koniec wizyty', async () => {
    await loadSchedule({ schedule: gridWeekly() });
    const a = {
      id: 'a1',
      employeeId: 'emp-1',
      date: futureMonday,
      endTime: '10:30:00',
      status: { id: 2 },
    } as unknown as AppointmentPreviewDto;
    component['selectedAppointment'].set(a);
    component.onAddBreakAfterSelected(a);
    const ctx = component['breakContext']();
    expect(ctx).not.toBeNull();
    expect(ctx!.employeeId).toBe('emp-1');
    expect(ctx!.startTime).toBe('10:30');
    expect(ctx!.editBreak).toBeUndefined();
  });
});
