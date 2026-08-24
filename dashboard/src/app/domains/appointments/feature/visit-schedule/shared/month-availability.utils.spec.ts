import { describe, it, expect } from 'vitest';
import {
  buildAvailabilityMap,
  classifyAvailability,
  dateMetaKey,
} from './month-availability.utils';

describe('classifyAvailability', () => {
  it('0 → none', () => {
    expect(classifyAvailability(0)).toBe('none');
    expect(classifyAvailability(-5)).toBe('none');
  });

  it('1..2 → low', () => {
    expect(classifyAvailability(1)).toBe('low');
    expect(classifyAvailability(2)).toBe('low');
  });

  it('3..5 → medium', () => {
    expect(classifyAvailability(3)).toBe('medium');
    expect(classifyAvailability(5)).toBe('medium');
  });

  it('>=6 → high', () => {
    expect(classifyAvailability(6)).toBe('high');
    expect(classifyAvailability(20)).toBe('high');
  });
});

describe('buildAvailabilityMap', () => {
  it('akceptuje string ISO i Date — kluczuje przez yyyy-MM-dd', () => {
    const map = buildAvailabilityMap([
      { date: '2026-05-17' as unknown as Date, availableCount: 0 },
      { date: '2026-05-18T00:00:00Z' as unknown as Date, availableCount: 2 },
      { date: new Date(2026, 4, 19), availableCount: 8 },
    ]);
    expect(map.get('2026-05-17')).toBe('none');
    expect(map.get('2026-05-18')).toBe('low');
    expect(map.get('2026-05-19')).toBe('high');
  });

  it('null/undefined wejście → pusta mapa', () => {
    expect(buildAvailabilityMap(null).size).toBe(0);
    expect(buildAvailabilityMap(undefined).size).toBe(0);
    expect(buildAvailabilityMap([]).size).toBe(0);
  });

  it('zero-padding miesiąca i dnia', () => {
    const map = buildAvailabilityMap([
      { date: new Date(2026, 0, 5), availableCount: 4 },
    ]);
    expect(map.get('2026-01-05')).toBe('medium');
  });
});

describe('dateMetaKey', () => {
  it('PrimeNG month (0-indexed) → 1-indexed string', () => {
    expect(dateMetaKey({ year: 2026, month: 0, day: 1 })).toBe('2026-01-01');
    expect(dateMetaKey({ year: 2026, month: 11, day: 31 })).toBe('2026-12-31');
  });
});
