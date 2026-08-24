import { beforeEach, describe, expect, it } from 'vitest';
import {
	clearSession,
	isSkipEligible,
	loadSession,
	maskContact,
	normalizeEmail,
	normalizePhone,
	saveSession,
	sessionKey,
} from './verified-session';

const SLUG = 'test-salon';
const future = () => new Date(Date.now() + 60 * 60 * 1000).toISOString();
const past = () => new Date(Date.now() - 1000).toISOString();

describe('verified-session', () => {
	beforeEach(() => window.sessionStorage.clear());

	it('zapisuje i odczytuje sesję (round-trip)', () => {
		saveSession(SLUG, { sessionToken: 'tok', expiresAtUtc: future(), channel: 'phone', contact: '+48501234567' });
		const s = loadSession(SLUG);
		expect(s?.sessionToken).toBe('tok');
		expect(s?.channel).toBe('phone');
		expect(s?.contact).toBe('+48501234567');
	});

	it('odrzuca i sprząta wygasłą sesję przy odczycie', () => {
		saveSession(SLUG, { sessionToken: 'tok', expiresAtUtc: past() });
		expect(loadSession(SLUG)).toBeNull();
		expect(window.sessionStorage.getItem(sessionKey(SLUG))).toBeNull();
	});

	it('clearSession usuwa wpis', () => {
		saveSession(SLUG, { sessionToken: 'tok', expiresAtUtc: '' });
		clearSession(SLUG);
		expect(loadSession(SLUG)).toBeNull();
	});

	it('isSkipEligible wymaga tokenu + kontaktu + zgodnego kanału', () => {
		const base = { sessionToken: 'tok', expiresAtUtc: future() };
		expect(isSkipEligible(null, 'phone')).toBe(false);
		expect(isSkipEligible({ ...base }, 'phone')).toBe(false);
		expect(isSkipEligible({ ...base, channel: 'phone', contact: '+48501234567' }, 'phone')).toBe(true);
		// inny kanał niż salonu → nie kwalifikuje
		expect(isSkipEligible({ ...base, channel: 'email', contact: 'a@b.co' }, 'phone')).toBe(false);
	});

	it('maskContact maskuje email i telefon', () => {
		expect(maskContact('email', 'guest@example.com')).toBe('gu…@example.com');
		expect(maskContact('phone', '+48501234567')).toBe('…567');
	});

	it('normalizery: email lowercase/trim, telefon bez separatorów', () => {
		expect(normalizeEmail('  Guest@EX.com ')).toBe('guest@ex.com');
		expect(normalizePhone(' +48 501-234 (567) ')).toBe('+48501234567');
	});
});
