import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  API_BASE_URL,
  AppointmentDto,
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
import { AppointmentFocusService } from '@core/services/appointment-focus.service';
import { ConfirmationService, MessageService } from 'primeng/api';

/**
 * Deep-link szczegółów wizyty: `/admin/schedule?appointment=<id>` → otwiera drawer w kalendarzu
 * na DNIU wizyty (zamiast usuniętej strony `/admin/appointment/:id`). Dwie regresje, które to
 * gwarantuje:
 *  1. Dzień (`date`) MUSI iść w tej samej nawigacji co czyszczenie paramu — inaczej
 *     `CalendarStateService.applyFromUrl` cofa sygnał daty z URL i kalendarz zostaje na starym dniu.
 *  2. Efekt czeka na stabilną trasę `/admin/schedule/:employeeId` (goła `/admin/schedule` jest
 *     przekierowywana i odtwarza komponent) — bez tego fetch leci na znikającej instancji.
 */
describe('VisitScheduleComponent — deep-link wizyty (?appointment)', () => {
  // Pierwszy test płaci zimną kompilację JIT ciężkiego standalone-komponentu (compileComponents()
  // w setup() — kolejne korzystają z cache Angulara). Pod obciążeniem równoległym (wiele plików
  // testowych naraz, saturacja CPU) potrafi przekroczyć domyślny 5s timeout Vitest → flake. Sama
  // logika jest szybka i zielona w izolacji; dajemy realny budżet czasu na kompilację.
  vi.setConfig({ testTimeout: 15000 });

  let fixture: ComponentFixture<VisitScheduleComponent>;
  let routerMock: { navigate: ReturnType<typeof vi.fn>; events: Observable<unknown>; url: string };
  let getAppointmentById: ReturnType<typeof vi.fn>;

  const EMPLOYEES = [
    { id: 'emp-1', firstName: 'Jan', lastName: 'Kowalski', email: 'jan@test.pl' },
    { id: 'emp-2', firstName: 'Ada', lastName: 'Nowak', email: 'ada@test.pl' },
  ];

  function makeAppt(over: Partial<AppointmentDto> = {}): AppointmentDto {
    return {
      id: 'apt-1',
      employeeId: 'emp-1',
      // 15.12.2026 — celowo INNY dzień niż „dziś", żeby zmiana dnia była obserwowalna.
      date: new Date(2026, 11, 15) as unknown as Date,
      startTime: '10:00:00',
      endTime: '10:45:00',
      serviceName: 'Strzyżenie',
      status: { id: 2, name: 'Booked' },
      ...over,
    } as AppointmentDto;
  }

  async function setup(opts: {
    appointment?: string | null;
    employeeIdParam?: string | null;
    appt?: AppointmentDto | null;
    reveal?: string;
  }): Promise<void> {
    // CalendarStateService persystuje `date` w localStorage — czyścimy, by poprzedni test nie
    // narzucił dnia i nie zamaskował zmiany przez deep-link.
    globalThis.localStorage?.clear();

    getAppointmentById = vi
      .fn()
      .mockReturnValue(of(opts.appt === undefined ? makeAppt() : opts.appt));
    // `events` jest wymagane przez pigułkę „Przewodnik" w nagłówku kalendarza.
    routerMock = { navigate: vi.fn().mockResolvedValue(true), events: of(), url: '/admin/schedule' };

    const queryParamMap = convertToParamMap({
      ...(opts.appointment ? { appointment: opts.appointment } : {}),
      ...(opts.reveal !== undefined ? { reveal: opts.reveal } : {}),
    });
    const paramMap = convertToParamMap(
      opts.employeeIdParam === null ? {} : { employeeId: opts.employeeIdParam ?? 'emp-1' },
    );

    await TestBed.configureTestingModule({
      imports: [VisitScheduleComponent],
      providers: [
        { provide: HttpClient, useValue: { get: vi.fn().mockReturnValue(of([])) } },
        { provide: API_BASE_URL, useValue: 'http://test-api' },
        {
          provide: DepositsClient,
          useValue: { generateLink: vi.fn().mockReturnValue(of({})), send: vi.fn().mockReturnValue(of({})) },
        },
        {
          provide: EmployeesClient,
          useValue: {
            getEmployees: vi.fn().mockReturnValue(of(EMPLOYEES)),
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
            getAppointmentById,
            // Karta „pierwsza rezerwacja" nad kalendarzem pyta o to przy montowaniu.
            hasAnyAppointment: vi.fn().mockReturnValue(of(false)),
          },
        },
        {
          // …a slug bierze stąd. Pusty stan trzyma kartę poza DOM-em testowanego kalendarza.
          provide: OnboardingStateService,
          useValue: { state: signal(null) },
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
        { provide: ServicesClient, useValue: { getServices: vi.fn().mockReturnValue(of([])) } },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
        },
        { provide: CustomersClient, useValue: { getCustomers: vi.fn().mockReturnValue(of([])) } },
        // AppointmentDetailSheet (w template) injektuje DepositsClient — bez mocka NG0201.
        { provide: DepositsClient, useValue: { generateLink: () => of(null), send: () => of(null) } },
        {
          provide: AuthSessionService,
          useValue: {
            currentRole: vi.fn().mockReturnValue('owner'),
            currentEmployeeId: vi.fn().mockReturnValue(null),
            currentUserId: vi.fn().mockReturnValue('user-1'),
            isHydrated: vi.fn().mockReturnValue(true),
            // mock defensywny — używany m.in. przez baner trybu demo
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
            queryParamMap: of(queryParamMap),
            snapshot: { queryParams: {} },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VisitScheduleComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Wśród wszystkich nawigacji znajduje tę deep-linkową (jedyna z `appointment: null`). */
  function deepLinkNav(): { commands: unknown[]; queryParams: Record<string, unknown> } | undefined {
    const call = routerMock.navigate.mock.calls.find(
      ([, opts]) => (opts as { queryParams?: { appointment?: unknown } })?.queryParams?.appointment === null,
    );
    if (!call) return undefined;
    return { commands: call[0] as unknown[], queryParams: (call[1] as { queryParams: Record<string, unknown> }).queryParams };
  }

  beforeEach(() => {
    routerMock = { navigate: vi.fn(), events: of(), url: '/admin/schedule' };
  });

  it('ustawia wizytę, widok dnia i FETCHuje pełne dane po id', async () => {
    await setup({ appointment: 'apt-1' });

    expect(getAppointmentById).toHaveBeenCalledWith('apt-1');
    expect(fixture.componentInstance['selectedAppointment']()?.id).toBe('apt-1');
    expect(fixture.componentInstance['viewMode']()).toBe('day');
  });

  it('REGRESJA: dzień wizyty idzie w tej samej nawigacji co czyszczenie paramu (kalendarz zmienia dzień)', async () => {
    await setup({ appointment: 'apt-1', appt: makeAppt({ date: new Date(2026, 11, 15) as unknown as Date }) });

    const nav = deepLinkNav();
    expect(nav, 'oczekiwano nawigacji deep-linkowej z appointment:null').toBeTruthy();
    // Sedno fixu: date + view w TYM SAMYM navigate — bez tego applyFromUrl cofał dzień.
    expect(nav!.queryParams['date']).toBe('2026-12-15');
    expect(nav!.queryParams['view']).toBe('day');
    expect(nav!.queryParams['appointment']).toBeNull();
    // Sygnał daty też wskazuje dzień wizyty.
    const d = fixture.componentInstance['selectedDate']() as Date;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 11, 15]);
  });

  it('przełącza na pracownika wizyty gdy inny niż w URL', async () => {
    await setup({
      appointment: 'apt-1',
      employeeIdParam: 'emp-1',
      appt: makeAppt({ employeeId: 'emp-2' }),
    });

    const nav = deepLinkNav();
    expect(nav!.commands).toEqual(['/admin', 'schedule', 'emp-2']);
    expect(nav!.queryParams['date']).toBe('2026-12-15');
  });

  it('REGRESJA: czeka na stabilną trasę — na gołej /admin/schedule (bez employeeId) NIE fetchuje', async () => {
    await setup({ appointment: 'apt-1', employeeIdParam: null });

    expect(getAppointmentById).not.toHaveBeenCalled();
    expect(fixture.componentInstance['selectedAppointment']()).toBeNull();
  });

  it('reveal=0: otwiera panel, ale NIE rusza dnia ani widoku kalendarza', async () => {
    // Powiadomienie „wizyta do potwierdzenia" — personel potwierdza i zostaje tam, gdzie był.
    await setup({
      appointment: 'apt-1',
      reveal: '0',
      appt: makeAppt({ date: new Date(2026, 11, 15) as unknown as Date }),
    });

    // Panel otwarty — to jest cała intencja kliknięcia.
    expect(fixture.componentInstance['selectedAppointment']()?.id).toBe('apt-1');
    // ...ale kalendarz stoi na dziś, NIE na 15.12.2026.
    const d = fixture.componentInstance['selectedDate']() as Date;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).not.toEqual([2026, 11, 15]);

    // Nawigacja czyści oba paramy i NIE przemyca dnia wizyty.
    const nav = deepLinkNav();
    expect(nav!.queryParams['appointment']).toBeNull();
    expect(nav!.queryParams['reveal']).toBeNull();
    expect(nav!.queryParams['date']).toBeUndefined();
    expect(nav!.queryParams['view']).toBeUndefined();
  });

  it('reveal=0 nie przełącza pracownika, nawet gdy wizyta należy do innego', async () => {
    await setup({
      appointment: 'apt-1',
      reveal: '0',
      employeeIdParam: 'emp-1',
      appt: makeAppt({ employeeId: 'emp-2' }),
    });

    // Przełączenie pracownika przebudowuje kalendarz — to też jest „ruszanie widoku".
    expect(deepLinkNav()!.commands).toEqual([]);
  });

  it('reveal=0: „Pokaż w kalendarzu" jest dostępne (kontekst o jedno kliknięcie)', async () => {
    // Zawór bezpieczeństwa: skoro nie przenosimy automatycznie, musi istnieć droga na żądanie.
    await setup({
      appointment: 'apt-1',
      reveal: '0',
      appt: makeAppt({ date: new Date(2026, 11, 15) as unknown as Date }),
    });

    expect(fixture.componentInstance['canRevealSelectedInCalendar']()).toBe(true);
  });

  it('po przeniesieniu przyciska już nie ma — kalendarz pokazuje tę wizytę', async () => {
    await setup({
      appointment: 'apt-1',
      reveal: '0',
      appt: makeAppt({ date: new Date(2026, 11, 15) as unknown as Date }),
    });

    fixture.componentInstance['revealSelectedInCalendar']();
    fixture.detectChanges();

    const d = fixture.componentInstance['selectedDate']() as Date;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 11, 15]);
    expect(fixture.componentInstance['viewMode']()).toBe('day');
    // Panel zostaje otwarty — zmieniło się tylko tło, o które użytkownik poprosił.
    expect(fixture.componentInstance['selectedAppointment']()?.id).toBe('apt-1');
    expect(fixture.componentInstance['canRevealSelectedInCalendar']()).toBe(false);
  });

  it('REGRESJA: dzwonek przy zamontowanym kalendarzu otwiera panel BEZ nawigacji', async () => {
    // Wcześniej powiadomienie linkowało na gołą /admin/schedule → redirect na trasę z employeeId
    // ODTWARZAŁ komponent: 4 zapisy URL, dwa montowania, ~46 requestów. Kanał przez
    // AppointmentFocusService omija router — dla reveal=false nie ma czego zapisać w URL.
    await setup({ appointment: null });
    routerMock.navigate.mockClear();

    TestBed.inject(AppointmentFocusService).request('apt-1', false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(getAppointmentById).toHaveBeenCalledWith('apt-1');
    expect(fixture.componentInstance['selectedAppointment']()?.id).toBe('apt-1');
    // Sedno: ANI JEDNEJ nawigacji.
    expect(routerMock.navigate).not.toHaveBeenCalled();
    // Kalendarz stoi — reveal=false nie rusza dnia wizyty (15.12.2026).
    const d = fixture.componentInstance['selectedDate']() as Date;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).not.toEqual([2026, 11, 15]);
  });

  it('dzwonek z reveal=true przenosi kalendarz (poza grafikiem / anulowana)', async () => {
    await setup({ appointment: null });
    routerMock.navigate.mockClear();

    TestBed.inject(AppointmentFocusService).request('apt-1', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const d = fixture.componentInstance['selectedDate']() as Date;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 11, 15]);
    expect(fixture.componentInstance['viewMode']()).toBe('day');
    // Dzień musi trafić do URL, inaczej applyFromUrl cofnie sygnał daty.
    const nav = routerMock.navigate.mock.calls.at(-1);
    expect((nav?.[1] as { queryParams: Record<string, unknown> }).queryParams['date']).toBe('2026-12-15');
  });

  it('bez paramu appointment nie otwiera niczego', async () => {
    await setup({ appointment: null });

    expect(getAppointmentById).not.toHaveBeenCalled();
    expect(fixture.componentInstance['selectedAppointment']()).toBeNull();
  });
});
