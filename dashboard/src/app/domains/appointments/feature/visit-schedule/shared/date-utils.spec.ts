import { describe, it, expect } from 'vitest';
import { appointmentsRequestKey } from './date-utils';

/**
 * Regresja: widok tygodnia nie reagował na zmianę pracownika, bo klucz `rxResource` zawierał samą
 * datę. `stream` czytał `effectiveEmployeeId`, ale zasób powtarza żądanie wyłącznie przy zmianie
 * `params` — id pracownika MUSI być częścią klucza.
 */
describe('appointmentsRequestKey', () => {
  it('różni się dla różnych pracowników w tym samym zakresie dat', () => {
    const a = appointmentsRequestKey('2026-07-06', false, 'emp-1');
    const b = appointmentsRequestKey('2026-07-06', false, 'emp-2');
    expect(a).not.toBe(b);
  });

  it('różni się dla różnych zakresów dat u tego samego pracownika', () => {
    expect(appointmentsRequestKey('2026-07-06', false, 'emp-1')).not.toBe(
      appointmentsRequestKey('2026-07-13', false, 'emp-1'),
    );
  });

  it('różni się dla trybu kolumn (desktop) vs pojedynczego pracownika', () => {
    expect(appointmentsRequestKey('2026-07-06', true, 'emp-1')).not.toBe(
      appointmentsRequestKey('2026-07-06', false, 'emp-1'),
    );
  });

  it('brak pracownika ma stabilny klucz (null i undefined tak samo)', () => {
    expect(appointmentsRequestKey('2026-07-06', false, null)).toBe(
      appointmentsRequestKey('2026-07-06', false, undefined),
    );
  });

  it('ten sam wejściowy stan daje ten sam klucz (brak zbędnych refetchy)', () => {
    expect(appointmentsRequestKey('2026-07-06', false, 'emp-1')).toBe(
      appointmentsRequestKey('2026-07-06', false, 'emp-1'),
    );
  });
});
