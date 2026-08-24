import { BookingApiException } from './booking-openapi-client';

/**
 * Mapowanie błędów API na komunikaty dla KLIENTKI salonu.
 *
 * Zasada nadrzędna: użytkownik publicznego kalendarza nigdy nie widzi technicznego tekstu.
 * „Load failed" (Safari), „Failed to fetch" (Chrome), „NetworkError when attempting to fetch
 * resource." (Firefox) czy angielski `title` z ProblemDetails 500 to dla niej szum, po którym
 * nie wie, czy to jej internet, czy salon jest zamknięty. Surowa treść błędu trafia do konsoli
 * i (przez `reportBookingError`) do logu backendu — nie na ekran.
 */

const errorMessages: Record<string, string> = {
	'validation.failed': 'Sprawdź poprawność danych formularza.',
	'validation.invalid_argument': 'Podane dane są nieprawidłowe.',
	'resource.not_found': 'Nie znaleziono zasobu.',
	'rate_limit.exceeded': 'Wykonujesz zbyt wiele żądań. Spróbuj ponownie później.',
	'internal.error': 'Wystąpił nieoczekiwany błąd serwera.',
	'appointment.slot_unavailable': 'Wybrany termin jest niedostępny albo został już zajęty.',
	'appointment.otp.invalid_lease': 'Sesja rezerwacji wygasła. Wybierz termin ponownie.',
	'appointment.otp.missing_contact': 'Podaj numer telefonu albo adres e-mail.',
	'appointment.otp.unsupported_phone_region':
		'Obsługujemy wyłącznie polskie numery telefonu (+48). Podaj numer w formacie +48 500 600 700.',
	'appointment.otp.missing_name': 'Podaj imię i nazwisko, aby zarezerwować wizytę.',
	'appointment.otp.verification_required': 'Najpierw poproś o kod weryfikacyjny.',
	'appointment.otp.too_many_failures': 'Przekroczono liczbę prób. Poproś o nowy kod.',
	'appointment.otp.invalid_code': 'Kod OTP jest nieprawidłowy albo wygasł.',
	'appointment.zero_duration':
		'Wybierz usługę z czasem trwania — sam dodatek nie wystarczy.',
	'appointment.addon_requires_main': 'Dodatek wymaga wybrania usługi głównej.',
	'appointment.addon_not_allowed': 'Ten dodatek nie pasuje do wybranej usługi.',
	'booking.not_invited':
		'Ten salon przyjmuje rezerwacje tylko z zaproszeniem. Skontaktuj się z salonem, aby otrzymać dostęp.',
};

/**
 * Rodzaj awarii. Steruje trzema decyzjami naraz: jaki komunikat pokazać, czy ponawianie ma sens
 * i czy zdarzenie warto zaraportować do backendu (patrz `shouldReportKind`).
 */
export type BookingErrorKind =
	/** Żądanie anulowane przez nas (zmiana kryteriów, unmount). Nigdy nie pokazujemy ani nie logujemy. */
	| 'aborted'
	/** Przeglądarka wie, że nie ma sieci. */
	| 'offline'
	/** Fetch padł na warstwie transportu: brak sieci, DNS, CORS, ubity backend. */
	| 'network'
	/** Front dostał HTML zamiast JSON — złe `PUBLIC_API_BASE_URL` przy buildzie albo zły routing edge. */
	| 'misconfigured'
	/** 429 / throttling. */
	| 'rate-limit'
	/** 404 — zwykle nieistniejący salon; ma własny ekran. */
	| 'not-found'
	/** 5xx — nasza wina, po stronie serwera. */
	| 'server'
	/** 4xx biznesowe/walidacyjne — komunikat pochodzi z API i jest po polsku. */
	| 'request'
	/** Cokolwiek innego, w tym crash renderowania komponentu. */
	| 'unknown';

const GENERIC_MESSAGES: Record<Exclude<BookingErrorKind, 'aborted' | 'request'>, string> = {
	offline:
		'Wygląda na to, że urządzenie jest offline. Sprawdź połączenie z internetem i spróbuj ponownie.',
	network:
		'Nie udało się połączyć z systemem rezerwacji. Sprawdź połączenie z internetem i spróbuj ponownie.',
	misconfigured:
		'Nie udało się połączyć z systemem rezerwacji. Spróbuj ponownie za chwilę — jeśli problem się powtarza, zadzwoń do salonu.',
	'rate-limit':
		'Zbyt wiele prób w krótkim czasie. Odczekaj chwilę i spróbuj ponownie.',
	'not-found': 'Nie znaleziono tego, czego szukasz.',
	server:
		'Coś poszło nie tak po naszej stronie. Spróbuj ponownie za chwilę — Twoja rezerwacja nie została utracona.',
	unknown: 'Wystąpił nieoczekiwany błąd. Spróbuj ponownie.',
};

/** Awarie warte zaraportowania. Błędy biznesowe (4xx) to normalna praca aplikacji — nie logujemy ich. */
export function shouldReportKind(kind: BookingErrorKind): boolean {
	return kind === 'network' || kind === 'misconfigured' || kind === 'server' || kind === 'unknown';
}

/** Czy ponowna próba tej samej operacji ma sens (steruje przyciskiem „Spróbuj ponownie"). */
export function isRetryableKind(kind: BookingErrorKind): boolean {
	return kind !== 'not-found' && kind !== 'aborted';
}

/**
 * Bezpieczne rozpoznanie wyjątku NSwag. Wygenerowany `BookingApiException.isBookingApiException`
 * czyta pole bez sprawdzenia null → rzuca TypeError dla `null`/`undefined`, a tu przychodzi
 * dowolny `unknown` z `catch`. `instanceof` łapie normalny przypadek, sprawdzenie pola ratuje
 * sytuację, gdyby w bundle znalazły się dwie kopie klasy.
 */
function asApiException(e: unknown): BookingApiException | null {
	if (e === null || e === undefined) return null;
	if (e instanceof BookingApiException) return e;
	if (
		typeof e === 'object' &&
		(e as { isBookingApiException?: boolean }).isBookingApiException === true
	) {
		return e as BookingApiException;
	}
	return null;
}

function errorName(e: unknown): string {
	return e instanceof Error ? e.name : '';
}

function errorMessageText(e: unknown): string {
	return e instanceof Error ? e.message : typeof e === 'string' ? e : '';
}

/** Przerwanie przez `AbortController` (zmiana kryteriów / unmount) — nie jest awarią. */
export function isAbortError(e: unknown): boolean {
	const name = errorName(e);
	return name === 'AbortError' || name === 'CanceledError';
}

function looksLikeHtmlInsteadOfJson(message: string): boolean {
	return (
		message.includes('<!DOCTYPE') ||
		message.includes("Unexpected token '<'") ||
		message.includes('Unexpected token "<"')
	);
}

/**
 * Komunikaty transportowe przeglądarek. Każda pisze inaczej, a użytkownik widział dokładnie ten
 * tekst („Load failed") jako czerwony błąd w kalendarzu — stąd ta lista.
 */
function looksLikeNetworkFailure(message: string): boolean {
	const m = message.toLowerCase();
	return (
		m.includes('failed to fetch') ||
		m.includes('load failed') ||
		m.includes('networkerror') ||
		m.includes('network request failed') ||
		m.includes('connection appears to be offline') ||
		m.includes('network connection was lost') ||
		m.includes('err_internet_disconnected') ||
		m.includes('err_connection') ||
		m.includes('err_name_not_resolved')
	);
}

function isOffline(): boolean {
	return typeof navigator !== 'undefined' && navigator.onLine === false;
}

export function classifyBookingError(e: unknown): BookingErrorKind {
	if (isAbortError(e)) return 'aborted';

	const apiError = asApiException(e);
	if (apiError) {
		const status = apiError.status;
		// status 0 potrafi wyjść z warstwy fetch przy CORS/przerwanym połączeniu — to transport, nie API.
		if (status === 0) return isOffline() ? 'offline' : 'network';
		if (status === 404) return 'not-found';
		if (status === 429) return 'rate-limit';
		if (status >= 500) return 'server';
		if (status >= 400) return 'request';
		return 'unknown';
	}

	const message = errorMessageText(e);
	if (e instanceof SyntaxError && looksLikeHtmlInsteadOfJson(message)) return 'misconfigured';
	if (looksLikeHtmlInsteadOfJson(message)) return 'misconfigured';
	if (errorName(e) === 'TimeoutError') return 'network';
	if (looksLikeNetworkFailure(message)) return isOffline() ? 'offline' : 'network';
	// TypeError z fetch bez rozpoznawalnej treści to i tak najczęściej transport.
	if (e instanceof TypeError) return isOffline() ? 'offline' : 'network';
	return 'unknown';
}

/** Kod błędu z ProblemDetails (`errorCode`/`messageKey`), jeśli backend go podał. */
export function bookingApiErrorCode(e: unknown): string | null {
	const apiError = asApiException(e);
	if (!apiError) return null;
	try {
		const j = JSON.parse(apiError.response) as { errorCode?: string; messageKey?: string };
		return j.errorCode ?? j.messageKey ?? null;
	} catch {
		return null;
	}
}

/** Komunikat z ciała odpowiedzi 4xx — pochodzi z naszego API, więc jest po polsku i dla człowieka. */
function apiBodyMessage(e: BookingApiException): string | null {
	try {
		const j = JSON.parse(e.response) as {
			title?: string;
			detail?: string;
			errorCode?: string;
			messageKey?: string;
		};
		const code = j.errorCode ?? j.messageKey;
		if (code && errorMessages[code]) return errorMessages[code];
		if (j.detail) return j.detail;
		if (j.title) return j.title;
	} catch {
		/* body nie jest JSON-em — nie pokazujemy surowego tekstu */
	}
	return null;
}

function headerRetryAfterSeconds(headers: { [key: string]: unknown } | undefined): number | null {
	if (!headers) return null;
	const raw =
		(headers['retry-after'] as string | undefined) ??
		(headers['Retry-After'] as string | undefined);
	if (raw === undefined || raw === null) return null;
	const n = parseInt(String(raw), 10);
	return Number.isFinite(n) && n > 0 ? n : null;
}

/** Sekundy z nagłówka Retry-After (429 / throttling), jeśli są. */
export function bookingApiRetryAfterSeconds(e: unknown): number | null {
	const apiError = asApiException(e);
	return apiError ? headerRetryAfterSeconds(apiError.headers) : null;
}

/**
 * Komunikat dla użytkownika. Dla 4xx bierze treść z API (nasza, polska), dla wszystkiego innego
 * — stały, przyjazny tekst zależny od rodzaju awarii. NIGDY nie zwraca `e.message` z transportu.
 */
export function bookingApiErrorMessage(e: unknown): string {
	const kind = classifyBookingError(e);
	const apiError = asApiException(e);
	if (kind === 'request' || kind === 'not-found') {
		const fromBody = apiError ? apiBodyMessage(apiError) : null;
		if (fromBody) return fromBody;
		return kind === 'not-found'
			? GENERIC_MESSAGES['not-found']
			: 'Nie udało się wykonać tej operacji. Sprawdź dane i spróbuj ponownie.';
	}
	if (kind === 'rate-limit') {
		// Backend potrafi dorzucić własny komunikat throttlingu — jest po polsku, więc ma pierwszeństwo.
		const fromBody = apiError ? apiBodyMessage(apiError) : null;
		if (fromBody) return fromBody;
		return GENERIC_MESSAGES['rate-limit'];
	}
	if (kind === 'aborted') return GENERIC_MESSAGES.unknown;
	return GENERIC_MESSAGES[kind];
}

export interface BookingErrorDescription {
	kind: BookingErrorKind;
	/** Krótki nagłówek na ekran błędu. */
	title: string;
	/** Zdanie wyjaśniające, co się stało i co z tym zrobić. */
	message: string;
	retryable: boolean;
	/** Status HTTP, jeśli błąd pochodzi z API. */
	status: number | null;
	/** Techniczny opis DO LOGU (konsola/backend) — nigdy nie pokazywany klientce. */
	technical: string;
}

const TITLES: Record<BookingErrorKind, string> = {
	aborted: 'Coś poszło nie tak',
	offline: 'Brak połączenia z internetem',
	network: 'Nie możemy połączyć się z salonem',
	misconfigured: 'Nie możemy połączyć się z salonem',
	'rate-limit': 'Za dużo prób naraz',
	'not-found': 'Nie znaleziono',
	server: 'Chwilowy problem po naszej stronie',
	request: 'Coś poszło nie tak',
	unknown: 'Wystąpił nieoczekiwany błąd',
};

/** Pełny opis błędu: co pokazać, czy ponawiać, co zalogować. */
export function describeBookingError(e: unknown): BookingErrorDescription {
	const kind = classifyBookingError(e);
	const status = asApiException(e)?.status ?? null;
	const code = bookingApiErrorCode(e);
	const raw = errorMessageText(e) || String(e ?? 'unknown');
	return {
		kind,
		title: TITLES[kind],
		message: bookingApiErrorMessage(e),
		retryable: isRetryableKind(kind),
		status,
		technical: [
			errorName(e) || typeof e,
			raw,
			status !== null ? `HTTP ${status}` : null,
			code ? `code=${code}` : null,
		]
			.filter(Boolean)
			.join(' | '),
	};
}
