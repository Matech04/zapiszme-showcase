import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingApiException } from '../booking-openapi-client';
import {
  __resetBookingErrorReporting,
  bookingCorrelationId,
  reportBookingError,
  shortCorrelationId,
} from './error-report';

/**
 * Raport awarii to jedyny kanał, którym błąd u klientki („Load failed" na telefonie) dociera do
 * naszego logu — ale ten sam kanał, źle zrobiony, potrafi zalać Seq albo wpaść w pętlę raportowania
 * własnej awarii. Testy pilnują obu stron: że raport LECI, kiedy trzeba, i że NIE leci, kiedy nie.
 */
describe('reportBookingError', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    __resetBookingErrorReporting();
    fetchMock = vi.fn().mockResolvedValue({ ok: true });
    vi.stubGlobal('fetch', fetchMock);
    // Konsola jest częścią kontraktu (log lokalny), ale w wyniku testów to tylko szum.
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  function bodyOf(call: number): Record<string, unknown> {
    const init = fetchMock.mock.calls[call][1] as RequestInit;
    return JSON.parse(String(init.body));
  }

  it('wysyła raport awarii sieci na endpoint diagnostyczny i loguje do konsoli', () => {
    reportBookingError(new TypeError('Load failed'), {
      operation: 'loadSalon',
      salonSlug: 'salon-testowy',
    });

    expect(console.error).toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain('/api/booking-diagnostics/client-error');
    expect((init as RequestInit).method).toBe('POST');
    // `keepalive`: raport musi dolecieć, nawet gdy klientka zamyka kartę zaraz po błędzie.
    expect((init as RequestInit).keepalive).toBe(true);

    const body = bodyOf(0);
    expect(body.kind).toBe('network');
    expect(body.operation).toBe('loadSalon');
    expect(body.salonSlug).toBe('salon-testowy');
    expect(String(body.message)).toContain('Load failed');
  });

  it('zwraca przyjazny komunikat, nigdy surowej treści wyjątku', () => {
    const described = reportBookingError(new TypeError('Load failed'), { operation: 'loadSlots' });
    expect(described.message).not.toContain('Load failed');
    expect(described.message).toContain('połączenie');
    // Surowa treść zostaje — ale wyłącznie w polu diagnostycznym, które nie trafia na ekran.
    expect(described.technical).toContain('Load failed');
  });

  it('nie raportuje błędów biznesowych 4xx ani przerwanych żądań', () => {
    reportBookingError(new BookingApiException('slot', 409, '{}', {}, null), {
      operation: 'createHold',
    });
    const abort = new Error('aborted');
    abort.name = 'AbortError';
    reportBookingError(abort, { operation: 'loadSlots' });

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('ten sam błąd tej samej operacji leci raz (dedupe okna czasowego)', () => {
    for (let i = 0; i < 5; i++) {
      reportBookingError(new TypeError('Load failed'), { operation: 'loadSlots' });
    }
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('twardy limit na kartę — awaria w pętli nie zamienia się w flood', () => {
    for (let i = 0; i < 40; i++) {
      // Różna treść → dedupe nie zadziała; ratuje wyłącznie limit sesji.
      reportBookingError(new TypeError(`Load failed ${i}`), { operation: 'loadSlots' });
    }
    expect(fetchMock.mock.calls.length).toBeLessThanOrEqual(20);
  });

  it('awaria samego raportowania nie generuje kolejnych raportów', async () => {
    fetchMock.mockRejectedValue(new TypeError('Load failed'));

    reportBookingError(new TypeError('Load failed'), { operation: 'loadSalon' });
    await Promise.resolve();
    await Promise.resolve();
    reportBookingError(new TypeError('inny błąd'), { operation: 'loadSlots' });

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  /**
   * Sedno naprawy z sierpnia 2026: raport nie może jechać kanałem cross-origin, który właśnie padł.
   * Gdy host serwujący SPA wypadł poza Cors:AllowedOrigins (www.zapisz.me), beacon na api.<domena>
   * był blokowany tym samym mechanizmem co awaria, o której miał donieść — i znikała z logów.
   */
  it('w produkcji raportuje na ścieżkę względną (same-origin), nie na adres API', () => {
    vi.stubEnv('PROD', true);
    try {
      reportBookingError(new TypeError('Load failed'), { operation: 'loadSalon' });
      const url = String(fetchMock.mock.calls[0][0]);
      expect(url).toBe('/api/booking-diagnostics/client-error');
      expect(url).not.toContain('//');
    } finally {
      vi.unstubAllEnvs();
    }
  });

  /**
   * Szum in-app browserów (Instagram/Facebook/WebView) to były 39 z 45 raportów w sierpniu 2026.
   * Nie pochodzi z naszego kodu i niczego nie psuje, ale zjadał budżet MAX_REPORTS_PER_SESSION,
   * przez co realna awaria kalendarza nie miała się już jak zalogować.
   */
  describe('szum wstrzykiwany przez in-app browsery', () => {
    const noise = [
      "Can't find variable: _AutofillCallbackHandler",
      "undefined is not an object (evaluating 'window.webkit.messageHandlers')",
      'Error invoking postMessage: Java object is gone',
    ];

    it.each(noise)('nie raportuje do backendu: %s', (message) => {
      reportBookingError(new TypeError(message), { operation: 'window.onerror' });
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it('zostaje w konsoli — lokalne debugowanie ma go widzieć', () => {
      reportBookingError(new TypeError(noise[0]), { operation: 'window.onerror' });
      expect(console.error).toHaveBeenCalled();
    });

    it('nie zużywa budżetu raportów, więc realna awaria wciąż doleci', () => {
      for (let i = 0; i < 40; i++) {
        reportBookingError(new TypeError(`${noise[0]} ${i}`), { operation: 'window.onerror' });
      }
      reportBookingError(new TypeError('Load failed'), { operation: 'loadSalon' });

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(String(bodyOf(0).message)).toContain('Load failed');
    });
  });

  it('identyfikator zgłoszenia jest stały w obrębie karty i skracalny do podania przez telefon', () => {
    const full = bookingCorrelationId();
    expect(bookingCorrelationId()).toBe(full);
    expect(shortCorrelationId()).toHaveLength(8);
    expect(full.replace(/-/g, '')).toContain(shortCorrelationId());
  });
});
