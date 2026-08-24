import { describe, it, expect } from 'vitest';
import { comboGroupKey, toggleServiceSelection } from './combo-select.util';

describe('comboGroupKey', () => {
  it('normalizuje (trim + lowercase)', () => {
    expect(comboGroupKey('  Przedłużanie ')).toBe('przedłużanie');
  });
  it('pusta/null → pusty string', () => {
    expect(comboGroupKey('')).toBe('');
    expect(comboGroupKey(null)).toBe('');
    expect(comboGroupKey(undefined)).toBe('');
  });
});

describe('toggleServiceSelection', () => {
  // a,b → grupa "g1"; c → "g2"; x,y → bez grupy.
  const groups: Record<string, string> = { a: 'g1', b: 'g1', c: 'g2', x: '', y: '' };
  const groupOf = (id: string) => groups[id] ?? '';
  const MAX = 5;

  it('dodaje do pustego wyboru', () => {
    expect(toggleServiceSelection([], 'x', groupOf, MAX)).toEqual(['x']);
  });

  it('dodaje kolejne usługi bez grupy (kolejność zachowana)', () => {
    expect(toggleServiceSelection(['x'], 'y', groupOf, MAX)).toEqual(['x', 'y']);
  });

  it('usuwa już wybraną usługę', () => {
    expect(toggleServiceSelection(['x', 'y'], 'x', groupOf, MAX)).toEqual(['y']);
  });

  it('PODMIENIA usługę z tej samej grupy', () => {
    expect(toggleServiceSelection(['a', 'x'], 'b', groupOf, MAX)).toEqual(['x', 'b']);
  });

  it('usługi z różnych grup współistnieją', () => {
    expect(toggleServiceSelection(['a'], 'c', groupOf, MAX)).toEqual(['a', 'c']);
  });

  it('nie przekracza limitu (no-op po przekroczeniu)', () => {
    const sel = ['x', 'y', 'a', 'c'];
    expect(toggleServiceSelection(sel, 'z', groupOf, 4)).toEqual(sel);
  });

  it('podmiana w grupie działa przy limicie (nie zwiększa liczby)', () => {
    expect(toggleServiceSelection(['a', 'x'], 'b', groupOf, 2)).toEqual(['x', 'b']);
  });

  it('usuwanie działa zawsze, nawet przy limicie', () => {
    expect(toggleServiceSelection(['x', 'y'], 'x', groupOf, 2)).toEqual(['y']);
  });

  it('nie mutuje wejścia', () => {
    const sel = ['x'];
    const out = toggleServiceSelection(sel, 'y', groupOf, MAX);
    expect(out).not.toBe(sel);
    expect(sel).toEqual(['x']);
  });
});
