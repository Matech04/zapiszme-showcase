/**
 * Sesja zweryfikowanego kontaktu — wspólne źródło prawdy dla panelu zarządzania i flow rezerwacji.
 *
 * Token (dowód, że to urządzenie potwierdziło niedawno władanie kontaktem przez OTP) trzymamy w
 * `sessionStorage` (per-karta): przeżywa przełącznik „Rezerwuj ⇄ Zarządzaj" oraz odświeżenie strony,
 * a czyści się po zamknięciu karty. Token i tak wygasa po stronie serwera (patrz SelfServiceSessionPolicy).
 *
 * Uwaga IG: przeglądarka in-app Instagrama zwykle czyści storage między otwarciami linku — dlatego
 * celowo NIE używamy localStorage (i tak nie przetrwałby), a stawiamy na trwałość w obrębie sesji.
 */

export type VerifiedChannel = 'email' | 'phone';

export interface VerifiedSession {
	sessionToken: string;
	expiresAtUtc: string;
	/** Kanał, którym potwierdzono kontakt (opcjonalny dla zgodności ze starszym zapisem). */
	channel?: VerifiedChannel;
	/** Zweryfikowany kontakt (email lowercase / telefon). Tylko do UI — backend jest authoritative. */
	contact?: string;
}

export function sessionKey(slug: string): string {
	return `booking_saas:selfservice:${slug}`;
}

export function normalizeEmail(value: string): string {
	return value.trim().toLowerCase();
}

/** Lekka normalizacja telefonu do porównań/wyświetlania (usuwa spacje i separatory). */
export function normalizePhone(value: string): string {
	return value.replace(/[\s\-()]/g, '');
}

function isExpired(s: VerifiedSession): boolean {
	if (!s.expiresAtUtc) return false;
	const exp = new Date(s.expiresAtUtc);
	return !isNaN(exp.getTime()) && exp <= new Date();
}

/** Wczytuje sesję; zwraca null i sprząta wpis, gdy brak / niepoprawny / wygasły. */
export function loadSession(slug: string): VerifiedSession | null {
	if (typeof window === 'undefined') return null;
	try {
		const raw = window.sessionStorage.getItem(sessionKey(slug));
		if (!raw) return null;
		const parsed = JSON.parse(raw) as VerifiedSession;
		if (!parsed?.sessionToken) {
			clearSession(slug);
			return null;
		}
		if (isExpired(parsed)) {
			clearSession(slug);
			return null;
		}
		return parsed;
	} catch {
		clearSession(slug);
		return null;
	}
}

export function saveSession(slug: string, session: VerifiedSession): void {
	if (typeof window === 'undefined' || !session.sessionToken) return;
	try {
		window.sessionStorage.setItem(sessionKey(slug), JSON.stringify(session));
	} catch {
		/* private mode / quota */
	}
}

export function clearSession(slug: string): void {
	if (typeof window === 'undefined') return;
	try {
		window.sessionStorage.removeItem(sessionKey(slug));
	} catch {
		/* ignore */
	}
}

/** Czy sesja kwalifikuje się do pominięcia OTP dla danego kanału salonu (musi nieść kontakt). */
export function isSkipEligible(session: VerifiedSession | null, salonChannel: VerifiedChannel): boolean {
	return !!session?.sessionToken && !!session.contact && session.channel === salonChannel;
}

/** Zamaskowany kontakt do etykiety „Zarezerwuj jako …". */
export function maskContact(channel: VerifiedChannel, contact: string): string {
	if (channel === 'email') {
		const [local, domain] = contact.split('@');
		if (!domain) return contact;
		const head = local.length <= 2 ? local : `${local.slice(0, 2)}…`;
		return `${head}@${domain}`;
	}
	// telefon: pokaż 3 ostatnie cyfry
	const digits = contact.replace(/\D/g, '');
	if (digits.length <= 3) return contact;
	return `…${digits.slice(-3)}`;
}
