import { describe, it, expect, beforeEach, vi } from 'vitest';
import { LastScheduleEmployeeStore } from './last-schedule-employee.store';

describe('LastScheduleEmployeeStore', () => {
  let store: LastScheduleEmployeeStore;

  beforeEach(() => {
    localStorage.clear();
    store = new LastScheduleEmployeeStore();
  });

  it('zapisuje i odczytuje wybór dla danego użytkownika', () => {
    store.save('user-1', 'emp-7');
    expect(store.read('user-1')).toBe('emp-7');
  });

  it('nie miesza wyborów między użytkownikami (kiosk = współdzielony terminal)', () => {
    store.save('user-1', 'emp-7');

    // Druga osoba loguje się na tej samej przeglądarce — nie może zobaczyć cudzego wyboru.
    expect(store.read('user-2')).toBeNull();

    store.save('user-2', 'emp-3');
    expect(store.read('user-1')).toBe('emp-7');
    expect(store.read('user-2')).toBe('emp-3');
  });

  it('zwraca null bez userId i nic nie zapisuje', () => {
    store.save(null, 'emp-7');
    store.save(undefined, 'emp-7');

    expect(store.read(null)).toBeNull();
    expect(store.read(undefined)).toBeNull();
    expect(localStorage.length).toBe(0);
  });

  it('ignoruje pusty employeeId', () => {
    store.save('user-1', '');
    store.save('user-1', null);
    expect(store.read('user-1')).toBeNull();
  });

  it('nie wywraca się, gdy localStorage rzuca (tryb prywatny / quota)', () => {
    const boom = () => {
      throw new Error('QuotaExceeded');
    };
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(boom);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(boom);

    expect(() => store.save('user-1', 'emp-7')).not.toThrow();
    expect(store.read('user-1')).toBeNull();

    vi.restoreAllMocks();
  });
});
