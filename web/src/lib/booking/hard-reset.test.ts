import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  buildRetryUrl,
  clearBookingLocalState,
  clearBrowserCaches,
  RETRY_PARAM,
  stripRetryParam,
} from './hard-reset';
import { sessionKey } from './verified-session';

describe('clearBookingLocalState', () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it('kasuje zawieszony hold, który psuje kolejne wejście', () => {
    window.localStorage.setItem('booking_saas:hold:salon-a', '{"appointmentId":"x"}');
    window.sessionStorage.setItem('booking_saas:hold:salon-a', '{"appointmentId":"x"}');

    clearBookingLocalState();

    expect(window.localStorage.getItem('booking_saas:hold:salon-a')).toBeNull();
    expect(window.sessionStorage.getItem('booking_saas:hold:salon-a')).toBeNull();
  });

  it('ZOSTAWIA sesję zweryfikowanego kontaktu — jej skasowanie kosztuje płatnego SMS-a', () => {
    window.sessionStorage.setItem(sessionKey('salon-a'), '{"sessionToken":"tok"}');

    clearBookingLocalState();

    expect(window.sessionStorage.getItem(sessionKey('salon-a'))).not.toBeNull();
  });

  it('nie rusza cudzych kluczy w storage', () => {
    window.localStorage.setItem('inne-narzedzie:stan', 'zostaje');

    clearBookingLocalState();

    expect(window.localStorage.getItem('inne-narzedzie:stan')).toBe('zostaje');
  });
});

describe('buildRetryUrl / stripRetryParam', () => {
  it('dokleja cache-bust, zachowując pozostałe parametry (np. tryb „zarządzaj wizytą")', () => {
    const url = buildRetryUrl('https://zapisz.me/salon-a?manage=1', 1234);
    expect(url).toContain('manage=1');
    expect(url).toContain(`${RETRY_PARAM}=1234`);
  });

  it('sprząta parametr z paska adresu po udanym starcie', () => {
    window.history.replaceState({}, '', `/salon-a?manage=1&${RETRY_PARAM}=1234`);

    stripRetryParam();

    expect(window.location.search).toContain('manage=1');
    expect(window.location.search).not.toContain(RETRY_PARAM);
  });
});

describe('clearBrowserCaches', () => {
  it('kasuje Cache API i wyrejestrowuje Service Workery (stary build w cache)', async () => {
    const deleteCache = vi.fn().mockResolvedValue(true);
    const unregister = vi.fn().mockResolvedValue(true);
    vi.stubGlobal('caches', { keys: vi.fn().mockResolvedValue(['v1', 'v2']), delete: deleteCache });
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: { getRegistrations: vi.fn().mockResolvedValue([{ unregister }]) },
    });

    await clearBrowserCaches();

    expect(deleteCache).toHaveBeenCalledWith('v1');
    expect(deleteCache).toHaveBeenCalledWith('v2');
    expect(unregister).toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  it('nie wywala się, gdy przeglądarka nie daje dostępu do cache', async () => {
    vi.stubGlobal('caches', {
      keys: vi.fn().mockRejectedValue(new Error('SecurityError')),
      delete: vi.fn(),
    });

    await expect(clearBrowserCaches()).resolves.toBeUndefined();
    vi.unstubAllGlobals();
  });
});
