import { BookingApiClient } from './booking-openapi-client';

/**
 * Bazowy URL backendu (bez końcowego `/`). NSwag składa ścieżki w stylu `/api/booking/...`.
 *
 * Gdy `PUBLIC_API_BASE_URL` nie jest ustawione w momencie **buildu** Astro, w bundle wpada
 * pusty string → fetch leci na **tę samą** domenę co strona (`/api/...` względem zapisz.me).
 * Kontener `booking-app` serwuje tylko HTML — w odpowiedzi nie ma JSON i klient wywala
 * `Unexpected token '<', "<!DOCTYPE "...`.
 *
 * Fallback na prod domenę zapisz.me: ostatnia linia obrony przy starym obrazie / ręcznym buildzie.
 */
const CUSTOM_BOOKING_PREFIX = 'rezerwacja.';

/**
 * Gdy SPA bookingu jest serwowane pod customową domeną klienta (`rezerwacja.<domena>`),
 * API żyje pod `api.<domena>` — ten sam rejestrowalny domain → cookies same-site
 * (anon-session, OTP) działają bez blokad ITP. Zwraca `null` dla zapisz.me / localhost / innych.
 */
export function getCustomBookingApiBase(hostname: string): string | null {
	if (!hostname.startsWith(CUSTOM_BOOKING_PREFIX)) return null;
	const base = hostname.slice(CUSTOM_BOOKING_PREFIX.length);
	// `base` musi być pełną domeną (min. domena.tld) i nie naszą własną.
	if (!base.includes('.') || base === 'zapisz.me') return null;
	return `https://api.${base}`;
}

/** Czy bieżący host to customowa domena white-label (`rezerwacja.<domena-klienta>`). */
export function isCustomBookingHost(hostname: string): boolean {
	return getCustomBookingApiBase(hostname) !== null;
}

/**
 * Adres strony głównej klienta dla white-label bookingu: `rezerwacja.<domena>` → `https://<domena>`.
 * Zwraca `null` poza customową domeną (zapisz.me / localhost / ścieżkowy slug) — wtedy nie pokazujemy
 * linku „powrót do strony głównej".
 */
export function getCustomBookingHomeUrl(hostname: string): string | null {
	if (!hostname.startsWith(CUSTOM_BOOKING_PREFIX)) return null;
	const base = hostname.slice(CUSTOM_BOOKING_PREFIX.length);
	if (!base.includes('.') || base === 'zapisz.me') return null;
	return `https://${base}`;
}

export function getBookingApiBase(): string {
	// PRIORYTET: runtime na customowej domenie → api.<domena>. Musi wyprzedzać build-time
	// PUBLIC_API_BASE_URL (=api.zapisz.me w prod), inaczej white-label wołałby cross-site
	// api.zapisz.me (third-party cookies → ITP psuje hold/OTP).
	if (typeof globalThis.window !== 'undefined') {
		const custom = getCustomBookingApiBase(globalThis.window.location.hostname);
		if (custom) return custom;
	}

	const trimmed = (import.meta.env.PUBLIC_API_BASE_URL ?? '').replace(/\/$/, '');
	if (trimmed) return trimmed;

	if (import.meta.env.PROD && typeof globalThis.window !== 'undefined') {
		const host = globalThis.window.location.hostname;
		if (host === 'zapisz.me' || host === 'www.zapisz.me') {
			return 'https://api.zapisz.me';
		}
	}
	return '';
}

export interface BookingApiClientOptions {
	signal?: AbortSignal;
	/** Token sesji self-service (zwracany przez verify-otp). Wstrzykiwany jako `X-SelfService-Token`. */
	selfServiceToken?: string | null;
}

/** Klient NSwag z opcjonalnym `AbortSignal` i tokenem sesji self-service. */
export function createBookingApiClient(options?: BookingApiClientOptions | AbortSignal): BookingApiClient {
	const base = getBookingApiBase();
	const opts: BookingApiClientOptions =
		options instanceof AbortSignal ? { signal: options } : (options ?? {});

	const fetcher: typeof globalThis.fetch = (url, init) => {
		const merged: RequestInit = { ...(init ?? {}) };
		// `include` żeby HttpOnly cookie booking_saas_anon_session leciało cross-origin
		// (booking-web → api). Bez tego każdy refresh tworzył nowe anon-session i
		// CancelPreviousHoldsForAnonSession nie miało czego znaleźć → squatting.
		merged.credentials = 'include';
		if (opts.signal) {
			merged.signal = opts.signal;
		}
		if (opts.selfServiceToken) {
			const headers = new Headers(merged.headers ?? {});
			headers.set('X-SelfService-Token', opts.selfServiceToken);
			merged.headers = headers;
		}
		return globalThis.fetch(url, merged);
	};

	return new BookingApiClient(base, { fetch: fetcher });
}
