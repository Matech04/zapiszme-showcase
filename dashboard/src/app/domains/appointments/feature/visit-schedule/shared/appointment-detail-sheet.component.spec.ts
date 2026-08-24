import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { ConfirmationService, MessageService } from 'primeng/api';
import {
  AppointmentDto,
  AppointmentPreviewDto,
  AppointmentStatus,
  AppointmentsClient,
  CustomerDto,
  CustomersClient,
  DepositsClient,
} from '@core/api/api-client';
import { AppointmentDetailSheetComponent } from './appointment-detail-sheet.component';

function appt(
  partial: Partial<AppointmentPreviewDto> & { statusName?: string; statusId?: number },
): AppointmentPreviewDto {
  const { statusName, statusId, ...rest } = partial;
  const status: AppointmentStatus | undefined =
    statusName || statusId
      ? ({ id: statusId, name: statusName } as AppointmentStatus)
      : undefined;
  return { ...rest, status } as AppointmentPreviewDto;
}

describe('AppointmentDetailSheetComponent', () => {
  let fixture: ComponentFixture<AppointmentDetailSheetComponent>;
  let component: AppointmentDetailSheetComponent;

  let confirmationServiceMock: { confirm: ReturnType<typeof vi.fn> };
  let sendDepositMock: ReturnType<typeof vi.fn>;
  let messageServiceMock: { add: ReturnType<typeof vi.fn> };
  let getAppointmentByIdMock: ReturnType<typeof vi.fn>;
  let updateAppointmentNoteMock: ReturnType<typeof vi.fn>;
  let setAppointmentDurationMock: ReturnType<typeof vi.fn>;
  let getCustomerMock: ReturnType<typeof vi.fn>;
  let updateCustomerNoteMock: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    // Sheet wywołuje `confirmationService.confirm()` na „Anuluj wizytę" — mock przechwytuje
    // wywołanie zamiast pokazywać realny p-confirmDialog.
    confirmationServiceMock = { confirm: vi.fn() };
    // Wysyłka linku do zadatku też przechodzi przez confirm() — mock pozwala sprawdzić, że bez
    // akceptacji nic nie leci do API.
    sendDepositMock = vi.fn(() => of({ channel: 'Phone' }));
    messageServiceMock = { add: vi.fn() };
    // Drawer doładowuje cenę/notatkę/Instagram po id — domyślnie pusto; testy nadpisują return.
    getAppointmentByIdMock = vi.fn().mockReturnValue(of(null));
    updateAppointmentNoteMock = vi.fn(() => of(''));
    setAppointmentDurationMock = vi.fn(() => of(''));
    // Notatka klienta doładowywana po customerId; domyślnie pusto, testy nadpisują.
    getCustomerMock = vi.fn().mockReturnValue(of(null));
    updateCustomerNoteMock = vi.fn(() => of(''));
    await TestBed.configureTestingModule({
      imports: [AppointmentDetailSheetComponent],
      providers: [
        { provide: ConfirmationService, useValue: confirmationServiceMock },
        // setFinalPrice dla ścieżki ceny końcowej; getAppointmentById domyślnie pusto (testy nadpisują).
        {
          provide: AppointmentsClient,
          useValue: {
            getAppointmentById: getAppointmentByIdMock,
            setFinalPrice: vi.fn(() => of('')),
            updateAppointmentNote: updateAppointmentNoteMock,
            setAppointmentDuration: setAppointmentDurationMock,
          },
        },
        // Sheet wstrzykuje CustomersClient dla notatki o kliencie (getCustomer + updateCustomerNote).
        {
          provide: CustomersClient,
          useValue: { getCustomer: getCustomerMock, updateCustomerNote: updateCustomerNoteMock },
        },
        // Sheet wstrzykuje DepositsClient (akcje „Generuj zadatek" / „Wyślij klientowi").
        { provide: DepositsClient, useValue: { generateLink: () => of(null), send: sendDepositMock } },
        { provide: MessageService, useValue: messageServiceMock },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AppointmentDetailSheetComponent);
    component = fixture.componentInstance;
  });

  function setInputs(partial: {
    appointment?: AppointmentPreviewDto | null;
    isDesktop?: boolean;
    isUpdating?: boolean;
    preloadedDetail?: AppointmentDto | null;
  }): void {
    // Preload PRZED `appointment` — resource czyta oba w tym samym CD, a to preload decyduje,
    // czy fetch po id w ogóle wystartuje.
    if ('preloadedDetail' in partial) {
      fixture.componentRef.setInput('preloadedDetail', partial.preloadedDetail ?? null);
    }
    if ('appointment' in partial) {
      fixture.componentRef.setInput('appointment', partial.appointment ?? null);
    }
    if ('isDesktop' in partial) {
      fixture.componentRef.setInput('isDesktop', !!partial.isDesktop);
    }
    if ('isUpdating' in partial) {
      fixture.componentRef.setInput('isUpdating', !!partial.isUpdating);
    }
    fixture.detectChanges();
  }

  it('startowo niewidoczny gdy appointment null', () => {
    setInputs({ appointment: null });
    expect((component as unknown as { isVisible: () => boolean }).isVisible()).toBe(false);
  });

  it('po ustawieniu appointment isVisible() przechodzi na true w tym samym CD', () => {
    setInputs({ appointment: appt({ id: 'a1', statusName: 'Pending' }) });
    expect((component as unknown as { isVisible: () => boolean }).isVisible()).toBe(true);
  });

  it('emituje confirm z id dla pending wizyty', () => {
    let confirmedId: string | null = null;
    component.confirm.subscribe((id) => (confirmedId = id));
    const a = appt({ id: 'a1', statusName: 'Pending' });
    setInputs({ appointment: a });
    (component as unknown as { onConfirm: (a: AppointmentPreviewDto) => void }).onConfirm(a);
    expect(confirmedId).toBe('a1');
  });

  it('onCancel otwiera popup potwierdzenia (a nie emituje od razu)', () => {
    let canceledId: string | null = null;
    component.cancel.subscribe((id) => (canceledId = id));
    const a = appt({ id: 'a2', statusName: 'Booked' });
    setInputs({ appointment: a });
    (component as unknown as { onCancel: (a: AppointmentPreviewDto) => void }).onCancel(a);
    // Dopóki user nie potwierdzi w confirmie, nie emitujemy cancel.
    expect(canceledId).toBeNull();
    expect(confirmationServiceMock.confirm).toHaveBeenCalledTimes(1);

    // Wywołanie `accept` callbacku symuluje klik „Tak, anuluj".
    const args = confirmationServiceMock.confirm.mock.calls[0]?.[0] as { accept: () => void };
    args.accept();
    expect(canceledId).toBe('a2');
  });

  it('hasAnyAction: false dla read-only (canceled), true gdy są szybkie akcje', () => {
    // Canceled bez akcji mutujących → footer się nie renderuje (pusty pasek byłby brzydki).
    setInputs({ appointment: appt({ id: 'a3', statusName: 'Canceled' }) });
    expect(
      (component as unknown as { hasAnyAction: () => boolean }).hasAnyAction(),
    ).toBe(false);
    setInputs({ appointment: appt({ id: 'a3', statusName: 'Booked' }) });
    expect(
      (component as unknown as { hasAnyAction: () => boolean }).hasAnyAction(),
    ).toBe(true);
  });

  it('doładowuje cenę / notatkę / Instagram po id (lazy fetch pełnej wizyty)', async () => {
    // Pola spoza AppointmentPreviewDto — drawer dociąga je przez getAppointmentById.
    getAppointmentByIdMock.mockReturnValue(
      of({
        id: 'rich-1',
        totalPrice: { amount: 100, currency: 'PLN' },
        appointmentNotes: 'Alergia na henne',
        customerInstagramNick: 'klientka_x',
      } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'rich-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(getAppointmentByIdMock).toHaveBeenCalledWith('rich-1');
    const c = component as unknown as {
      price: () => { amount?: number; currency?: string } | null;
      notes: () => string | null;
      instagramNick: () => string | null;
    };
    expect(c.price()).toEqual({ amount: 100, currency: 'PLN' });
    expect(c.notes()).toBe('Alergia na henne');
    expect(c.instagramNick()).toBe('klientka_x');
  });

  it('REGRESJA: preloadedDetail zastępuje lazy fetch — parent już pobrał tę wizytę (deep-link)', async () => {
    // Deep-link `?appointment=<id>` fetchuje pełny AppointmentDto w parencie, żeby wiedzieć,
    // którą wizytę otworzyć. Bez preloadu sheet strzelał po ten sam zasób drugi raz —
    // dodatkowy round-trip i drugi przeskok layoutu tuż po otwarciu panelu.
    setInputs({
      appointment: appt({ id: 'deep-1', statusName: 'Pending' }),
      preloadedDetail: {
        id: 'deep-1',
        totalPrice: { amount: 240, currency: 'PLN' },
        appointmentNotes: 'Z powiadomienia',
      } as unknown as AppointmentDto,
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(getAppointmentByIdMock).not.toHaveBeenCalled();
    const c = component as unknown as {
      price: () => { amount?: number } | null;
      notes: () => string | null;
    };
    expect(c.price()).toEqual({ amount: 240, currency: 'PLN' });
    expect(c.notes()).toBe('Z powiadomienia');
  });

  it('preloadedDetail dla INNEJ wizyty jest ignorowany — sheet dociąga własne dane', async () => {
    // Strażnik przed pokazaniem danych wizyty A po przełączeniu na wizytę B.
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'other-1', totalPrice: { amount: 50, currency: 'PLN' } } as unknown as AppointmentDto),
    );
    setInputs({
      appointment: appt({ id: 'other-1', statusName: 'Booked' }),
      preloadedDetail: { id: 'stale-9', totalPrice: { amount: 999, currency: 'PLN' } } as unknown as AppointmentDto,
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(getAppointmentByIdMock).toHaveBeenCalledWith('other-1');
    expect(
      (component as unknown as { price: () => { amount?: number } | null }).price(),
    ).toEqual({ amount: 50, currency: 'PLN' });
  });

  it('karta „Cena końcowa" ukryta dla wizyty przyszłej (Booked), widoczna po rozpoczęciu (Completed)', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'price-1', totalPrice: { amount: 120, currency: 'PLN' } } as unknown as AppointmentDto),
    );

    // Wizyta przyszła — rozliczenie bez sensu, karta i orientacyjna cena ukryte.
    setInputs({ appointment: appt({ id: 'price-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="appointment-final-price"]'),
    ).toBeNull();

    // Ta sama wizyta po zakończeniu — karta rozliczenia się pojawia.
    setInputs({ appointment: appt({ id: 'price-1', statusName: 'Completed' }) });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="appointment-final-price"]'),
    ).not.toBeNull();
  });

  it('hideEmployeeLine chowa wiersz pracownika (salon jednoosobowy)', () => {
    const a = appt({ id: 'emp-x', statusName: 'Booked', employeeFirstName: 'Ewa', employeeLastName: 'Z.' });

    setInputs({ appointment: a });
    expect(fixture.nativeElement.querySelector('[data-testid="employee-line"]')).not.toBeNull();

    fixture.componentRef.setInput('hideEmployeeLine', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="employee-line"]')).toBeNull();
  });

  it('akcje drugorzędne schowane pod „Więcej opcji" — rozwijają się po kliknięciu', () => {
    // Booked + canMutate (default) → canChangeService/canSwap = true → są akcje drugorzędne.
    setInputs({ appointment: appt({ id: 'sec-1', statusName: 'Booked' }) });

    const toggle = fixture.nativeElement.querySelector(
      '[data-testid="more-actions-toggle"]',
    ) as HTMLButtonElement | null;
    expect(toggle).not.toBeNull();
    // Zwinięte na starcie — sekcja akcji drugorzędnych nie w DOM.
    expect(fixture.nativeElement.querySelector('[data-testid="secondary-actions"]')).toBeNull();

    toggle!.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="secondary-actions"]')).not.toBeNull();
    // „Zamień z inną wizytą" (akcja drugorzędna) dopiero teraz widoczna.
    expect(fixture.nativeElement.querySelector('[data-testid="swap-from-drawer"]')).not.toBeNull();
  });

  it('renderuje miniatury inspiracji i otwiera lightbox po kliknięciu', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({
        id: 'insp-1',
        inspirationImages: [
          { url: 'https://cdn/i/a.webp', thumbnailUrl: 'https://cdn/i/a_thumb.webp' },
          { url: 'https://cdn/i/b.webp', thumbnailUrl: 'https://cdn/i/b_thumb.webp' },
        ],
      } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'insp-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    const thumbs = fixture.nativeElement.querySelectorAll(
      '[data-testid="inspiration-thumb"]',
    ) as NodeListOf<HTMLElement>;
    expect(thumbs.length).toBe(2);

    const c = component as unknown as { previewPhoto: () => string | null };
    expect(c.previewPhoto()).toBeNull();
    thumbs[0].click();
    fixture.detectChanges();
    expect(c.previewPhoto()).toBe('https://cdn/i/a.webp');
  });

  it('brak inspiracji — sekcja miniatur nie renderuje się', async () => {
    getAppointmentByIdMock.mockReturnValue(of({ id: 'no-insp' } as unknown as AppointmentDto));
    setInputs({ appointment: appt({ id: 'no-insp', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector('[data-testid="inspiration-images"]'),
    ).toBeNull();
  });

  it('bez pól dodatkowych — cena/notatka/Instagram pozostają null', async () => {
    getAppointmentByIdMock.mockReturnValue(of({ id: 'bare-1' } as unknown as AppointmentDto));
    setInputs({ appointment: appt({ id: 'bare-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    const c = component as unknown as {
      price: () => unknown;
      notes: () => unknown;
      instagramNick: () => unknown;
    };
    expect(c.price()).toBeNull();
    expect(c.notes()).toBeNull();
    expect(c.instagramNick()).toBeNull();
  });

  it('position desktop=right, mobile=bottom', () => {
    setInputs({ appointment: appt({ id: 'a' }), isDesktop: true });
    expect(
      (component as unknown as { position: () => 'right' | 'bottom' }).position(),
    ).toBe('right');
    setInputs({ isDesktop: false });
    expect(
      (component as unknown as { position: () => 'right' | 'bottom' }).position(),
    ).toBe('bottom');
  });

  it('canQuickConfirm dotyczy tylko pending', () => {
    setInputs({ appointment: appt({ statusName: 'Pending' }) });
    expect(
      (component as unknown as { canQuickConfirm: () => boolean }).canQuickConfirm(),
    ).toBe(true);
    setInputs({ appointment: appt({ statusName: 'Booked' }) });
    expect(
      (component as unknown as { canQuickConfirm: () => boolean }).canQuickConfirm(),
    ).toBe(false);
  });

  it('canQuickCancel jest false dla completed i canceled', () => {
    setInputs({ appointment: appt({ statusName: 'Completed' }) });
    expect(
      (component as unknown as { canQuickCancel: () => boolean }).canQuickCancel(),
    ).toBe(false);
    setInputs({ appointment: appt({ statusName: 'Canceled' }) });
    expect(
      (component as unknown as { canQuickCancel: () => boolean }).canQuickCancel(),
    ).toBe(false);
    setInputs({ appointment: appt({ statusName: 'Booked' }) });
    expect(
      (component as unknown as { canQuickCancel: () => boolean }).canQuickCancel(),
    ).toBe(true);
  });

  it('F3.1: onReschedule emituje rescheduleRequested z wizytą', () => {
    let target: AppointmentPreviewDto | null = null;
    component.rescheduleRequested.subscribe((a) => (target = a));
    const a = appt({ id: 'a-x', statusName: 'Booked' });
    setInputs({ appointment: a });
    (
      component as unknown as { onReschedule: (a: AppointmentPreviewDto) => void }
    ).onReschedule(a);
    expect(target!.id).toBe('a-x');
  });

  it('F3.1: canReschedule jest false dla completed/canceled lub gdy nie canMutate', () => {
    setInputs({ appointment: appt({ statusName: 'Completed' }) });
    expect(
      (component as unknown as { canReschedule: () => boolean }).canReschedule(),
    ).toBe(false);
    setInputs({ appointment: appt({ statusName: 'Canceled' }) });
    expect(
      (component as unknown as { canReschedule: () => boolean }).canReschedule(),
    ).toBe(false);
    setInputs({ appointment: appt({ statusName: 'Booked' }) });
    expect(
      (component as unknown as { canReschedule: () => boolean }).canReschedule(),
    ).toBe(true);
    fixture.componentRef.setInput('canMutate', false);
    fixture.detectChanges();
    expect(
      (component as unknown as { canReschedule: () => boolean }).canReschedule(),
    ).toBe(false);
  });

  it('zamknięcie drawera emituje closeRequested gdy appointment był ustawiony', () => {
    let closed = false;
    component.closeRequested.subscribe(() => (closed = true));
    setInputs({ appointment: appt({ id: 'a' }) });
    (component as unknown as { onVisibleChange: (v: boolean) => void }).onVisibleChange(false);
    expect(closed).toBe(true);
  });

  // ── Czas trwania wizyty (override personelu) ─────────────────────────────────────────────────

  it('saveDuration woła setAppointmentDuration z override i emituje durationChanged', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({
        id: 'dur-1',
        services: [{ serviceId: 's1', durationMinutes: 60 }],
        customDurationMinutes: null,
      } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'dur-1', statusName: 'Booked', startTime: '10:00:00', endTime: '11:00:00' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    let changed = false;
    component.durationChanged.subscribe(() => (changed = true));

    const c = component as unknown as {
      standardDurationMinutes: () => number;
      durationValue: { set: (v: number | null) => void };
      saveDuration: (reset?: boolean) => void;
    };
    expect(c.standardDurationMinutes()).toBe(60);

    c.durationValue.set(40);
    c.saveDuration();

    expect(setAppointmentDurationMock).toHaveBeenCalledWith('dur-1', { durationMinutes: 40 });
    expect(changed).toBe(true);
  });

  it('saveDuration(reset) i wartość równa standardowi wysyłają durationMinutes undefined (powrót do standardu)', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({
        id: 'dur-2',
        services: [{ serviceId: 's1', durationMinutes: 60 }],
        customDurationMinutes: 40,
      } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'dur-2', statusName: 'Booked', startTime: '10:00:00', endTime: '10:40:00' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    const c = component as unknown as {
      isCustomDuration: () => boolean;
      durationValue: { set: (v: number | null) => void };
      saveDuration: (reset?: boolean) => void;
    };
    expect(c.isCustomDuration()).toBe(true);

    c.saveDuration(true);
    expect(setAppointmentDurationMock).toHaveBeenLastCalledWith('dur-2', { durationMinutes: undefined });

    // Wpisanie wartości == standard też normalizuje do undefined.
    c.durationValue.set(60);
    c.saveDuration();
    expect(setAppointmentDurationMock).toHaveBeenLastCalledWith('dur-2', { durationMinutes: undefined });
  });

  it('przycisk „Zmień czas trwania" jest pod „Więcej opcji" (ukryty do rozwinięcia)', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'dur-3', services: [{ serviceId: 's1', durationMinutes: 30 }] } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'dur-3', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    // Zwinięte: przycisku zmiany czasu nie ma w DOM.
    expect(fixture.nativeElement.querySelector('[data-testid="detail-duration-edit"]')).toBeNull();

    const toggle = fixture.nativeElement.querySelector('[data-testid="more-actions-toggle"]') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Po rozwinięciu „Więcej opcji" pojawia się przycisk; klik odsłania edytor (pełnoszerokościowy panel).
    const editBtn = fixture.nativeElement.querySelector('[data-testid="detail-duration-edit"]') as HTMLButtonElement;
    expect(editBtn).not.toBeNull();
    editBtn.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="detail-duration-editor"]')).not.toBeNull();
  });

  it('canEditDuration: false dla completed/canceled, true dla booked (gdy fullDetail wczytany)', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'ed-1', services: [{ serviceId: 's1', durationMinutes: 30 }] } as unknown as AppointmentDto),
    );
    const c = component as unknown as { canEditDuration: () => boolean };

    setInputs({ appointment: appt({ id: 'ed-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(c.canEditDuration()).toBe(true);

    setInputs({ appointment: appt({ id: 'ed-1', statusName: 'Completed' }) });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(c.canEditDuration()).toBe(false);
  });

  // ── Notatka wizyty (edycja inline) ───────────────────────────────────────────────────────────

  it('saveAppointmentNotes woła updateAppointmentNote z przyciętą wartością i przeładowuje', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'note-1', appointmentNotes: 'stara' } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'note-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    const c = component as unknown as {
      appointmentNotesValue: { set: (v: string) => void };
      saveAppointmentNotes: () => void;
    };
    c.appointmentNotesValue.set('  nowa notatka  ');
    c.saveAppointmentNotes();

    expect(updateAppointmentNoteMock).toHaveBeenCalledWith('note-1', 'nowa notatka');
  });

  it('canEditAppointmentNotes: false dla canceled i gdy nie canMutate, true dla booked', async () => {
    getAppointmentByIdMock.mockReturnValue(of({ id: 'edit-1' } as unknown as AppointmentDto));
    const c = component as unknown as { canEditAppointmentNotes: () => boolean };

    setInputs({ appointment: appt({ id: 'edit-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(c.canEditAppointmentNotes()).toBe(true);

    setInputs({ appointment: appt({ id: 'edit-1', statusName: 'Canceled' }) });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(c.canEditAppointmentNotes()).toBe(false);

    setInputs({ appointment: appt({ id: 'edit-1', statusName: 'Booked' }) });
    fixture.componentRef.setInput('canMutate', false);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(c.canEditAppointmentNotes()).toBe(false);
  });

  // ── Notatka o kliencie ───────────────────────────────────────────────────────────────────────

  it('dla wizyty z klientem doładowuje notatkę klienta i saveCustomerNotes ją zapisuje', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'c-appt', customerId: 'cust-1', isGuest: false } as unknown as AppointmentDto),
    );
    getCustomerMock.mockReturnValue(
      of({ id: 'cust-1', generalNotes: 'VIP, alergia' } as unknown as CustomerDto),
    );
    setInputs({ appointment: appt({ id: 'c-appt', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(getCustomerMock).toHaveBeenCalledWith('cust-1');
    const c = component as unknown as {
      customerNotes: () => string | null;
      customerNotesValue: { set: (v: string) => void };
      saveCustomerNotes: () => void;
    };
    expect(c.customerNotes()).toBe('VIP, alergia');

    c.customerNotesValue.set('  nowa notatka klienta  ');
    c.saveCustomerNotes();
    expect(updateCustomerNoteMock).toHaveBeenCalledWith('cust-1', 'nowa notatka klienta');
  });

  it('wspólna sekcja notatek jest domyślnie zwinięta i przełącza się', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'col-1', customerId: 'cust-1', isGuest: false } as unknown as AppointmentDto),
    );
    getCustomerMock.mockReturnValue(of({ id: 'cust-1', generalNotes: '' } as unknown as CustomerDto));
    setInputs({ appointment: appt({ id: 'col-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    const c = component as unknown as {
      notesExpanded: { (): boolean; set: (v: boolean) => void };
    };
    expect(c.notesExpanded()).toBe(false);

    c.notesExpanded.set(true);
    expect(c.notesExpanded()).toBe(true);
  });

  it('przełączenie na inną wizytę zwija notatki z powrotem', async () => {
    getAppointmentByIdMock.mockReturnValue(of({ id: 'sw-1' } as unknown as AppointmentDto));
    setInputs({ appointment: appt({ id: 'sw-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    const c = component as unknown as {
      notesExpanded: { (): boolean; set: (v: boolean) => void };
    };
    c.notesExpanded.set(true);
    expect(c.notesExpanded()).toBe(true);

    setInputs({ appointment: appt({ id: 'sw-2', statusName: 'Booked' }) });
    fixture.detectChanges();
    expect(c.notesExpanded()).toBe(false);
  });

  it('dla wizyty-gościa nie pobiera klienta (sekcja notatki klienta ukryta)', async () => {
    getAppointmentByIdMock.mockReturnValue(
      of({ id: 'g-appt', customerId: null, isGuest: true } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'g-appt', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(getCustomerMock).not.toHaveBeenCalled();
    // rxResource z pustymi params nie uruchamia streamu → value undefined; sekcja w template ukryta.
    const c = component as unknown as { customerDetail: { value: () => unknown } };
    expect(c.customerDetail.value()).toBeFalsy();
  });

  // ── Wysyłka linku do zadatku ────────────────────────────────────────────────────────────

  type SheetInternals = {
    sendDepositLink: () => void;
    depositLinkSentAt: () => Date | null;
    depositLinkSentLabel: () => string | null;
    depositAmountLabel: () => string | null;
    depositAttemptLabel: () => string | null;
    expiredAttemptsLabel: () => string | null;
    depositValidityLabel: () => string | null;
    depositLinkExpired: () => boolean;
    depositStatusChip: () => { label: string };
  };

  async function openWithDeposit(extra: Partial<AppointmentDto> = {}): Promise<SheetInternals> {
    getAppointmentByIdMock.mockReturnValue(
      of({
        id: 'dep-1',
        paymentStatus: 'AwaitingPayment',
        paymentLinkUrl: 'https://zps.me/p/ABC',
        paymentLinkExpired: false,
        ...extra,
      } as unknown as AppointmentDto),
    );
    setInputs({ appointment: appt({ id: 'dep-1', statusName: 'Booked' }) });
    await fixture.whenStable();
    fixture.detectChanges();
    return component as unknown as SheetInternals;
  }

  it('wysyłka zadatku pyta o potwierdzenie i bez akceptacji nie woła API', async () => {
    const c = await openWithDeposit();

    c.sendDepositLink();

    expect(confirmationServiceMock.confirm).toHaveBeenCalledTimes(1);
    expect(sendDepositMock).not.toHaveBeenCalled();
  });

  it('po akceptacji potwierdzenia wysyła link i pokazuje toast sukcesu', async () => {
    const c = await openWithDeposit();

    c.sendDepositLink();
    confirmationServiceMock.confirm.mock.calls[0][0].accept();

    expect(sendDepositMock).toHaveBeenCalledWith('dep-1', { channel: undefined });
    expect(messageServiceMock.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success', summary: 'Wysłano' }),
    );
  });

  it('nie pokazuje toasta sukcesu, gdy backend odrzuci wysyłkę (deposit.send_failed)', async () => {
    sendDepositMock.mockReturnValue(throwError(() => new Error('send_failed')));
    const c = await openWithDeposit();

    c.sendDepositLink();
    confirmationServiceMock.confirm.mock.calls[0][0].accept();

    expect(messageServiceMock.add).not.toHaveBeenCalled();
  });

  it('link niewysłany — brak znacznika „Wysłano"', async () => {
    const c = await openWithDeposit({ depositLinkSentAtUtc: undefined });

    expect(c.depositLinkSentAt()).toBeFalsy();
    expect(c.depositLinkSentLabel()).toBeNull();
  });

  it('link wysłany SMS-em — znacznik podaje kanał i datę', async () => {
    const c = await openWithDeposit({
      depositLinkSentAtUtc: new Date('2026-07-09T12:30:00Z'),
      depositLinkSentChannel: 'Sms',
    });

    expect(c.depositLinkSentAt()).toBeTruthy();
    expect(c.depositLinkSentLabel()).toContain('Wysłano SMS-em');
  });

  it('potwierdzenie ponownej wysyłki ma inny nagłówek niż pierwszej', async () => {
    const c = await openWithDeposit({
      depositLinkSentAtUtc: new Date('2026-07-09T12:30:00Z'),
      depositLinkSentChannel: 'Sms',
    });

    c.sendDepositLink();

    expect(confirmationServiceMock.confirm.mock.calls[0][0].header).toBe('Wysłać link ponownie?');
  });

  // ── Karta stanu zadatku (kwota, próby, ważność) ─────────────────────────────────────────

  it('kwota zadatku formatowana jako waluta PL', async () => {
    const c = await openWithDeposit({ depositAmount: { amount: 30, currency: 'PLN' } as never });

    //   (twarda spacja) — Intl wstawia ją przed symbolem waluty.
    expect(c.depositAmountLabel()?.replace(/ /g, ' ')).toBe('30,00 zł');
  });

  it('pierwszy link nie dostaje numeru próby — „1. link" to szum', async () => {
    const c = await openWithDeposit({ depositLinkAttempts: 1 });

    expect(c.depositAttemptLabel()).toBeNull();
  });

  it('kolejne linki numerowane', async () => {
    const c = await openWithDeposit({ depositLinkAttempts: 3 });

    expect(c.depositAttemptLabel()).toBe('3. link');
  });

  it('brak wygasłych prób — brak ostrzeżenia', async () => {
    const c = await openWithDeposit({ expiredDepositLinkCount: 0 });

    expect(c.expiredAttemptsLabel()).toBeNull();
  });

  it('jedna wygasła próba — forma pojedyncza', async () => {
    const c = await openWithDeposit({ expiredDepositLinkCount: 1 });

    expect(c.expiredAttemptsLabel()).toBe('Poprzedni link wygasł bez opłaty.');
  });

  it.each([
    [2, '2 poprzednie linki wygasły bez opłaty.'],
    [3, '3 poprzednie linki wygasły bez opłaty.'],
    [5, '5 poprzednich linków wygasło bez opłaty.'],
    [12, '12 poprzednich linków wygasło bez opłaty.'],
    [22, '22 poprzednie linki wygasły bez opłaty.'],
    [25, '25 poprzednich linków wygasło bez opłaty.'],
  ])('polska liczba mnoga wygasłych prób: %i', async (count, expected) => {
    const c = await openWithDeposit({ expiredDepositLinkCount: count });

    expect(c.expiredAttemptsLabel()).toBe(expected);
  });

  // Zegar komponentu startuje przy jego tworzeniu, czyli ułamek sekundy przed `Date.now()` poniżej.
  // Etykiety obcinają sekundy w dół, więc celowanie w okrągłą granicę (dokładnie 2 godz.) dawałoby
  // flaky „1 godz. 59 min". Stąd 30-sekundowa poduszka w każdym teście czasu.
  const CUSHION_MS = 30_000;

  it('ważność linku odliczana do wygaśnięcia', async () => {
    const expiresAt = new Date(Date.now() + 3 * 3_600_000 + 20 * 60_000 + CUSHION_MS);
    const c = await openWithDeposit({ paymentLinkExpiresAtUtc: expiresAt });

    expect(c.depositLinkExpired()).toBe(false);
    expect(c.depositValidityLabel()).toBe('Ważny jeszcze 3 godz. 20 min');
    expect(c.depositStatusChip().label).toBe('Czeka na opłatę');
  });

  it('link po terminie jest wygasły nawet gdy backend jeszcze tego nie policzył', async () => {
    // paymentLinkExpired=false (stan z chwili fetchu), ale termin już minął — lokalny zegar wygrywa.
    const c = await openWithDeposit({
      paymentLinkExpired: false,
      paymentLinkExpiresAtUtc: new Date(Date.now() - 2 * 3_600_000 - CUSHION_MS),
    });

    expect(c.depositLinkExpired()).toBe(true);
    expect(c.depositValidityLabel()).toBe('Wygasł 2 godz. temu');
    expect(c.depositStatusChip().label).toBe('Wygasł');
  });
});
