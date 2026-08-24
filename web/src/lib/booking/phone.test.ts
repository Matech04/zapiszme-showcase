import { describe, expect, it } from 'vitest';
import { isPolishPhone, isValidEmail, normalizePolishPhone } from './phone';

describe('normalizePolishPhone', () => {
	it('akceptuje gołe 9 cyfr i normalizuje do E.164', () => {
		expect(normalizePolishPhone('500600700')).toBe('+48500600700');
	});

	it('akceptuje prefiks +48 oraz formatowanie (spacje/myślniki)', () => {
		expect(normalizePolishPhone('+48 500 600 700')).toBe('+48500600700');
		expect(normalizePolishPhone('+48-500-600-700')).toBe('+48500600700');
		expect(normalizePolishPhone('+48501234567')).toBe('+48501234567');
	});

	it('akceptuje prefiks 0048 oraz 48 (11 cyfr)', () => {
		expect(normalizePolishPhone('0048500600700')).toBe('+48500600700');
		expect(normalizePolishPhone('48500600700')).toBe('+48500600700');
	});

	it('odrzuca numery zagraniczne (inny kod kraju)', () => {
		expect(normalizePolishPhone('+49500600700')).toBeNull();
		expect(normalizePolishPhone('+1 202 555 0182')).toBeNull();
		expect(normalizePolishPhone('0049500600700')).toBeNull();
	});

	it('odrzuca złą liczbę cyfr', () => {
		expect(normalizePolishPhone('50060070')).toBeNull(); // 8 cyfr
		expect(normalizePolishPhone('5006007001')).toBeNull(); // 10 cyfr
		expect(normalizePolishPhone('')).toBeNull();
	});

	it('odrzuca numer krajowy zaczynający się od 0', () => {
		expect(normalizePolishPhone('012345678')).toBeNull();
	});
});

describe('isPolishPhone', () => {
	it('zwraca true/false zgodnie z normalizacją', () => {
		expect(isPolishPhone('+48501234567')).toBe(true);
		expect(isPolishPhone('123')).toBe(false);
		expect(isPolishPhone('+49500600700')).toBe(false);
	});
});

describe('isValidEmail', () => {
	it('akceptuje sensowny adres', () => {
		expect(isValidEmail('ja@przyklad.pl')).toBe(true);
		expect(isValidEmail('  anna.kowalska@salon.example.com  ')).toBe(true);
	});

	it('odrzuca oczywiste literówki i puste', () => {
		expect(isValidEmail('ja@')).toBe(false);
		expect(isValidEmail('ja@domena')).toBe(false); // brak TLD
		expect(isValidEmail('jadomena.pl')).toBe(false); // brak @
		expect(isValidEmail('')).toBe(false);
	});

	it('odrzuca zbyt długi adres (> 254)', () => {
		const long = `${'a'.repeat(250)}@b.pl`;
		expect(isValidEmail(long)).toBe(false);
	});
});
