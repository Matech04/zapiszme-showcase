import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingApiException } from '../../lib/booking-openapi-client';
import type { SelfServiceAppointmentDto } from '../../lib/booking-openapi-client';

// Regresja: przekładanie wizyty z panelu klienta nie pokazywało żadnych wolnych terminów.
//
// Bug: `loadRescheduleSlots()` przekazywał POJEDYNCZY `apt.serviceId` (string) jako parametr
// `serviceIds`, który wygenerowany klient NSwag oczekuje jako TABLICĘ (`string[]`). Serializacja
// w kliencie robi `serviceIds.forEach(...)` — string nie ma `.forEach`, więc leciał TypeError
// JESZCZE przed wysłaniem requestu, łapany przez catch → pusta lista → „Brak wolnych terminów".
// To samo dotyczyło `GetMonthAvailability` (kolorowanie obłożenia w kalendarzu).
//
// Ten test napędza pełny flow (logowanie kodem → lista → „Przełóż") i pilnuje, że oba wywołania
// dostają TABLICĘ `[serviceId]`. Wariant z gołym stringiem nie przejdzie (`Array.isArray` = false).

const mocks = vi.hoisted(() => {
  const requestOtp = vi.fn();
  const verifyOtp = vi.fn();
  const list = vi.fn();
  const getAvailableSlots = vi.fn();
  const getMonthAvailability = vi.fn();
  const reschedule = vi.fn();
  const cancel = vi.fn();
  const getSalon = vi.fn();
  // Każde createBookingApiClient() zwraca TEN SAM obiekt — spies są współdzielone między wywołaniami.
  const client = {
    publicSelfService_RequestOtp: requestOtp,
    publicSelfService_VerifyOtp: verifyOtp,
    publicSelfService_List: list,
    publicSelfService_Reschedule: reschedule,
    publicSelfService_Cancel: cancel,
    bookingAppointments_GetAvailableSlots: getAvailableSlots,
    bookingAppointments_GetMonthAvailability: getMonthAvailability,
    publicBookingSalon_Get: getSalon,
  };
  return { client, requestOtp, verifyOtp, list, getAvailableSlots, getMonthAvailability, getSalon };
});

vi.mock('../../lib/booking-api-browser', () => ({
  createBookingApiClient: () => mocks.client,
}));

// Turnstile off w teście — pomijamy bot-check (brak site-key i tak by go wyłączył).
vi.mock('../../lib/turnstile', () => ({
  isTurnstileEnabled: () => false,
  getFreshTurnstileToken: () => Promise.resolve(undefined),
  loadTurnstileScript: () => Promise.resolve(),
  renderInvisibleTurnstile: () => null,
  removeTurnstile: () => {},
}));

import ManageAppointment from './ManageAppointment.svelte';

// Wizyta combo: usługa główna + dodatek. Dostępność/sloty MUSZĄ uwzględniać całe combo,
// nie tylko serviceId, inaczej slot wolny dla usługi głównej nie pomieści całej wizyty.
// Data wizyty musi być w PRZYSZŁOŚCI licząc od dnia uruchomienia testu. Zaszyta data (2026-07-15)
// zgniła: gdy minęła, dzień był „przeszły" → zero wolnych dni → zamiast siatki godzin komponent
// pokazywał komunikat o braku terminów i test padał bez żadnej zmiany w kodzie.
const APPT_DATE = futureDay(15);

const APPT = {
  id: 'appt-1',
  employeeId: 'emp-1',
  serviceId: 'svc-1',
  serviceIds: ['svc-1', 'svc-2'],
  serviceName: 'Strzyżenie + koloryzacja',
  employeeFirstName: 'Anna',
  employeeLastName: 'Kowalska',
  date: APPT_DATE,
  startTime: '10:00:00',
  endTime: '11:30:00',
  status: 'Booked',
  canCancel: true,
  canReschedule: true,
} as unknown as SelfServiceAppointmentDto;

function toISO(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** N-ty dzień PRZYSZŁEGO miesiąca — z dala od „dziś" i od granic miesiąca. */
function futureDay(dayNum: number): string {
  const now = new Date();
  return toISO(new Date(now.getFullYear(), now.getMonth() + 1, dayNum));
}

/** Wiersze dostępności na cały miesiąc zawierający `iso`; `countFor` decyduje o wolnych miejscach. */
function monthRowsAround(iso: string, countFor: (iso: string) => number) {
  const ref = new Date(`${iso}T00:00:00`);
  const y = ref.getFullYear();
  const m = ref.getMonth();
  const daysInMonth = new Date(y, m + 1, 0).getDate();
  const rows = [];
  for (let d = 1; d <= daysInMonth; d++) {
    const date = toISO(new Date(y, m, d));
    rows.push({ date, availableCount: countFor(date), isWorkingDay: true });
  }
  return rows;
}

/** Przeprowadza komponent przez logowanie kodem aż do widoku listy wizyt. Zwraca uchwyt render(). */
async function loginToList() {
  const result = render(ManageAppointment, { props: { salonSlug: 'test-salon' } });

  // Kanał ustala się po wczytaniu salonu (async) → czekamy na pole e-mail.
  await fireEvent.input(await screen.findByTestId('manage-contact-email'), {
    target: { value: 'klient@example.com' },
  });
  await fireEvent.click(screen.getByTestId('manage-send-otp'));

  const code = await screen.findByTestId('manage-otp-code');
  await fireEvent.input(code, { target: { value: '123456' } });
  await fireEvent.click(screen.getByTestId('manage-verify-otp'));

  // Po verify-otp komponent ładuje listę wizyt → pojawia się przycisk „Przełóż".
  await screen.findByTestId('manage-appointment-reschedule');
  return result;
}

describe('ManageAppointment — przekładanie wizyty', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Sesja self-service trzymana jest w sessionStorage — czyść między testami (izolacja).
    window.sessionStorage.clear();
    mocks.requestOtp.mockResolvedValue(undefined);
    mocks.verifyOtp.mockResolvedValue({
      sessionToken: 'sess-tok-1',
      // Data odległa w przyszłości: sesja restore'owana z sessionStorage nie może wygasnąć wraz z
      // upływem realnego czasu (loadSession odrzuca expiresAtUtc <= now). Wcześniej „2026-07-15"
      // było przyszłością, ale test rotował i padał, gdy ta data stała się przeszłością.
      expiresAtUtc: '2999-01-01T00:00:00Z',
    });
    mocks.list.mockResolvedValue([APPT]);
    mocks.getAvailableSlots.mockResolvedValue([{ slot: '10:00', isPreferred: false }]);
    mocks.getMonthAvailability.mockResolvedValue({
      isClosed: false,
      opensOn: undefined,
      days: monthRowsAround(APPT_DATE, (iso) => (iso === APPT_DATE ? 3 : 0)),
    });
    // Salon weryfikuje e-mailem (1) → formularz logowania pokazuje pole e-mail (jak w tym teście).
    mocks.getSalon.mockResolvedValue({ customerVerificationChannel: 1 });
  });

  it('woła GetAvailableSlots z PEŁNYM combo (tablica, nie goły string)', async () => {
    await loginToList();

    await fireEvent.click(screen.getByTestId('manage-appointment-reschedule'));

    await waitFor(() => expect(mocks.getAvailableSlots).toHaveBeenCalled());

    // Sygnatura: (slug, date, employeeId, serviceIds)
    const [slug, date, employeeId, serviceIds] = mocks.getAvailableSlots.mock.calls[0];
    expect(slug).toBe('test-salon');
    expect(date).toBe(APPT_DATE);
    expect(employeeId).toBe('emp-1');
    // Sedno regresji: tablica (nie string → klient rzuca TypeError) ORAZ pełne combo
    // (nie tylko serviceId → sloty liczone dla całej wizyty, nie samej usługi głównej).
    expect(Array.isArray(serviceIds)).toBe(true);
    expect(serviceIds).toEqual(['svc-1', 'svc-2']);
  });

  it('woła GetMonthAvailability z PEŁNYM combo (obłożenie kalendarza)', async () => {
    await loginToList();

    await fireEvent.click(screen.getByTestId('manage-appointment-reschedule'));

    await waitFor(() => expect(mocks.getMonthAvailability).toHaveBeenCalled());

    // Sygnatura: (slug, year, month, employeeId, serviceIds)
    const serviceIds = mocks.getMonthAvailability.mock.calls[0][4];
    expect(Array.isArray(serviceIds)).toBe(true);
    expect(serviceIds).toEqual(['svc-1', 'svc-2']);
  });

  it('renderuje wolne terminy w modalu (nie „Brak wolnych terminów")', async () => {
    await loginToList();

    await fireEvent.click(screen.getByTestId('manage-appointment-reschedule'));

    // Slot z mocka musi się pojawić jako klikalny przycisk.
    await screen.findByTestId('booking-slot-10:00');
  });

  // Regresja: modal otwiera się na dacie BIEŻĄCEJ wizyty, która bywa w całości zajęta.
  // W przeciwieństwie do BookingFlow nie było tu żadnego auto-wyboru, więc taki wybór nigdy
  // się nie korygował — klientka widziała pustą siatkę godzin i komunikat sugerujący zmianę
  // pracownika (czego w tym modalu zrobić się nie da).
  it('przeskakuje na najbliższy wolny dzień, gdy dzień bieżącej wizyty jest zajęty', async () => {
    // Daty liczone względem „dziś" — sztywne daty rotują w przeszłość i test cicho traci sens
    // (dzień w przeszłości ma status „none" niezależnie od dostępności).
    const bookedDay = futureDay(10);
    const freeDay = futureDay(11);

    mocks.list.mockResolvedValue([{ ...APPT, date: bookedDay }]);
    // Cały miesiąc roboczy, ale wolne miejsca TYLKO w `freeDay` — w tym dzień wizyty ma zero.
    mocks.getMonthAvailability.mockResolvedValue({
      isClosed: false,
      opensOn: undefined,
      days: monthRowsAround(bookedDay, (iso) => (iso === freeDay ? 5 : 0)),
    });
    mocks.getAvailableSlots.mockResolvedValue([{ slot: '12:00', isPreferred: false }]);

    await loginToList();
    await fireEvent.click(screen.getByTestId('manage-appointment-reschedule'));

    // Auto-korekta dociąga sloty dla wolnego dnia — ostatnie wywołanie musi dotyczyć `freeDay`,
    // nie zajętego dnia wizyty, od którego modal wystartował.
    await waitFor(() => {
      const calls = mocks.getAvailableSlots.mock.calls;
      expect(calls[calls.length - 1][1]).toBe(freeDay);
    });

    // ...i to on jest zaznaczony w pasku dni.
    await waitFor(() =>
      expect(screen.getByTestId(`booking-day-${freeDay}`).getAttribute('aria-pressed')).toBe(
        'true',
      ),
    );
  });

  it('nie rusza wybranej daty, gdy dzień bieżącej wizyty ma wolne godziny', async () => {
    const bookedDay = futureDay(10);

    mocks.list.mockResolvedValue([{ ...APPT, date: bookedDay }]);
    mocks.getMonthAvailability.mockResolvedValue(monthRowsAround(bookedDay, () => 4));

    await loginToList();
    await fireEvent.click(screen.getByTestId('manage-appointment-reschedule'));

    await waitFor(() => expect(mocks.getMonthAvailability).toHaveBeenCalled());
    // Dzień wizyty jest wolny → auto-korekta ma milczeć (żadne wywołanie nie dotyczy innej daty).
    await waitFor(() =>
      expect(screen.getByTestId(`booking-day-${bookedDay}`).getAttribute('aria-pressed')).toBe(
        'true',
      ),
    );
    for (const call of mocks.getAvailableSlots.mock.calls) expect(call[1]).toBe(bookedDay);
  });

  it('utrzymuje sesję po odmontowaniu (przełącznik Rezerwuj/Zarządzaj) — bez ponownego OTP', async () => {
    // 1. Pełne logowanie kodem → token trafia do sessionStorage.
    const first = await loginToList();

    // 2. Odmontowanie — tak jak przy przełączeniu na „Rezerwuj" (BookingEntry niszczy komponent).
    first.unmount();

    // 3. Ponowne zamontowanie (powrót do „Zarządzaj") — lista od razu, BEZ formularza logowania.
    render(ManageAppointment, { props: { salonSlug: 'test-salon' } });
    await screen.findByTestId('manage-appointment-reschedule');
    expect(screen.queryByTestId('manage-contact-email')).toBeNull();

    // OTP nie był wołany ponownie — sesja została odtworzona z sessionStorage.
    expect(mocks.verifyOtp).toHaveBeenCalledTimes(1);
  });

  it('czyści sesję i wraca do logowania, gdy token wygasł po stronie serwera (401)', async () => {
    const first = await loginToList();
    first.unmount();

    // Po remountcie restore wywoła loadAppointments() — tym razem token jest już nieważny (401).
    mocks.list.mockRejectedValueOnce(
      new BookingApiException('unauthorized', 401, '', {}, null),
    );
    render(ManageAppointment, { props: { salonSlug: 'test-salon' } });

    // 401 → sesja wyczyszczona, znów ekran logowania, a token zniknął z sessionStorage.
    await screen.findByTestId('manage-contact-email');
    expect(window.sessionStorage.getItem('booking_saas:selfservice:test-salon')).toBeNull();
  });
});
