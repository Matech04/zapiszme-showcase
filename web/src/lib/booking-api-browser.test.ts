import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createBookingApiClient,
  getBookingApiBase,
  getCustomBookingApiBase,
  getCustomBookingHomeUrl,
  isCustomBookingHost,
} from './booking-api-browser';

describe('getBookingApiBase', () => {
  it('usuwa końcowy ukośnik z PUBLIC_API_BASE_URL (ustawione w vitest.config)', () => {
    expect(getBookingApiBase()).toBe('https://api.example/v1');
  });
});

describe('getCustomBookingApiBase', () => {
  it('wyprowadza api.<domena> z rezerwacja.<domena>', () => {
    expect(getCustomBookingApiBase('rezerwacja.salon-przyklad.pl')).toBe(
      'https://api.salon-przyklad.pl',
    );
  });

  it('działa dla wieloczłonowych TLD', () => {
    expect(getCustomBookingApiBase('rezerwacja.example.co.uk')).toBe('https://api.example.co.uk');
  });

  it.each([
    'zapisz.me',
    'www.zapisz.me',
    'rezerwacja.zapisz.me', // własna domena — nie white-label
    'localhost',
    'rezerwacja.pl', // base "pl" bez kropki → niepełna domena
    'salon-przyklad.pl', // brak prefiksu rezerwacja.
  ])('zwraca null dla %s', (host) => {
    expect(getCustomBookingApiBase(host)).toBeNull();
  });
});

describe('getCustomBookingHomeUrl', () => {
  it('wyprowadza https://<domena> z rezerwacja.<domena>', () => {
    expect(getCustomBookingHomeUrl('rezerwacja.salon-przyklad.pl')).toBe(
      'https://salon-przyklad.pl',
    );
  });

  it('działa dla wieloczłonowych TLD', () => {
    expect(getCustomBookingHomeUrl('rezerwacja.example.co.uk')).toBe('https://example.co.uk');
  });

  it.each([
    'zapisz.me',
    'www.zapisz.me',
    'rezerwacja.zapisz.me',
    'localhost',
    'rezerwacja.pl',
    'salon-przyklad.pl',
  ])('zwraca null dla %s', (host) => {
    expect(getCustomBookingHomeUrl(host)).toBeNull();
  });
});

describe('isCustomBookingHost', () => {
  it('true dla customowej domeny klienta', () => {
    expect(isCustomBookingHost('rezerwacja.salon-przyklad.pl')).toBe(true);
  });

  it('false dla zapisz.me i localhost', () => {
    expect(isCustomBookingHost('zapisz.me')).toBe(false);
    expect(isCustomBookingHost('localhost')).toBe(false);
  });
});

describe('createBookingApiClient', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('tworzy klienta z bazą z getBookingApiBase', () => {
    const client = createBookingApiClient();
    expect(client).toBeDefined();
  });

  it('przekazuje AbortSignal do globalnego fetch', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const ac = new AbortController();
    const client = createBookingApiClient(ac.signal);
    await client.bookingAppointments_GetAvailableSlots('demo-salon');

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const init = fetchSpy.mock.calls[0][1] as RequestInit | undefined;
    expect(init?.signal).toBe(ac.signal);
  });
});
