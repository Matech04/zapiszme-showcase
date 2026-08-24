/**
 * Walidacja numeru telefonu spójna z backendem (App.Domain PhoneNumber + IsPolish).
 *
 * Backend parsuje numer przez libphonenumber (region domyślny PL) i ODRZUCA wszystko, co nie jest
 * poprawnym polskim numerem (+48, 9 cyfr krajowych). Front historycznie sprawdzał tylko długość ≥ 9,
 * więc przepuszczał numery zagraniczne / o złej liczbie cyfr — backend zwracał wtedy mylący błąd
 * (kod `missing_contact`, choć numer BYŁ podany). Ten helper domyka tę różnicę po stronie UI: dajemy
 * natychmiastowy, jednoznaczny komunikat zamiast wysyłać żądanie skazane na odrzucenie.
 *
 * Celowo nieco luźniejszy niż pełny libphonenumber (nie sprawdzamy poprawności prefiksu operatora),
 * ale wymusza kształt „+48 + 9 cyfr” — łapie realne pomyłki (kraj, liczba cyfr) bez ciągnięcia
 * ciężkiej biblioteki do frontu.
 */

/** Usuwa wszystko poza cyframi i wiodącym `+`. */
function stripFormatting(raw: string): string {
	return raw.replace(/[^\d+]/g, '');
}

/**
 * Normalizuje polski numer do E.164 (`+48XXXXXXXXX`) lub zwraca `null`, gdy numer nie jest poprawnym
 * polskim numerem. Akceptuje prefiks `+48`, `0048`, `48` (przy 11 cyfrach) albo gołe 9 cyfr krajowych.
 */
export function normalizePolishPhone(raw: string): string | null {
	const cleaned = stripFormatting(raw);
	let national = cleaned;

	if (national.startsWith('+48')) {
		national = national.slice(3);
	} else if (national.startsWith('0048')) {
		national = national.slice(4);
	} else if (national.startsWith('48') && national.length === 11) {
		national = national.slice(2);
	}

	// Inny kod kraju (np. +49, +1) — odrzucamy: to nie jest polski numer.
	if (national.startsWith('+')) return null;
	// Numery krajowe w PL nie zaczynają się od 0.
	if (national.startsWith('0')) return null;
	if (!/^\d{9}$/.test(national)) return null;

	return `+48${national}`;
}

/** Czy `raw` jest poprawnym polskim numerem telefonu (zgodnie z backendem). */
export function isPolishPhone(raw: string): boolean {
	return normalizePolishPhone(raw) !== null;
}

/**
 * Lekka walidacja adresu e-mail — co najmniej tak surowa jak backend (Guard: obecność `@`, długość
 * ≤ 254), plus wymóg sensownego kształtu `lokalna@domena.tld`, by nie przepuszczać oczywistych
 * literówek („ja@”, „ja@domena”) na drogi krok wysłania kodu.
 */
export function isValidEmail(raw: string): boolean {
	const v = raw.trim();
	if (v.length === 0 || v.length > 254) return false;
	return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v);
}
