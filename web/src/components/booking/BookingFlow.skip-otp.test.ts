import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import BookingFlow from './BookingFlow.svelte';
import type { BookingDataSource } from '../../lib/booking/data-source';
import type {
	BookingEmployeeDto,
	BookingServiceDto,
	BookingMonthAvailabilityDto,
	MonthDayAvailabilityDto,
	PublicBookingSalonInfoDto,
} from '../../lib/booking-openapi-client';
import { toISODate } from '../../lib/booking/format';
import { saveSession } from '../../lib/booking/verified-session';

// Skip-OTP przy ponownej rezerwacji: gdy w sessionStorage jest ważna sesja zweryfikowanego kontaktu
// dla kanału salonu, potwierdzenie rezerwacji idzie przez confirm-with-session (bez OTP). „Zmień"
// wraca do zwykłej weryfikacji OTP.

// Turnstile zamockowane na poziomie pliku. Testy z `enableBotCheck: false` są tym nietknięte
// (`turnstileEnabled = enableBotCheck && isTurnstileEnabled()`), a ostatni test włącza bot-check,
// żeby sprawdzić, że skip-OTP dokleja świeży token — serwer go od tego endpointu wymaga.
vi.mock('../../lib/turnstile', () => ({
	isTurnstileEnabled: () => true,
	loadTurnstileScript: () => Promise.resolve(),
	renderInvisibleTurnstile: () => 'widget-1',
	removeTurnstile: () => {},
	getFreshTurnstileToken: () => Promise.resolve('fresh-turnstile-token'),
}));

const SLUG = 'test-salon';
const SERVICE: BookingServiceDto = {
	id: 'svc-1',
	name: 'Strzyżenie',
	durationInMinutes: 60,
	price: { amount: 100, currency: 'PLN' },
};
const EMPLOYEE: BookingEmployeeDto = { id: 'emp-1', firstName: 'Anna', lastName: 'Kowalska' };

const SALON_INFO = {
	name: 'Salon',
	slug: SLUG,
	customerVerificationChannel: 0, // 0 = telefon
	isBookingAvailable: true,
	requireCustomerName: false,
	collectInstagramHandle: false,
} as unknown as PublicBookingSalonInfoDto;

function buildDataSource(spies: {
	confirmWithSession: BookingDataSource['confirmWithSession'];
	verifyOtp: BookingDataSource['verifyOtp'];
	requestOtp: BookingDataSource['requestOtp'];
}): BookingDataSource {
	return {
		salonSlug: SLUG,
		async loadSalon() {
			return { services: [SERVICE], serviceCategories: [], salonInfo: SALON_INFO };
		},
		async loadEmployees() {
			return [EMPLOYEE];
		},
		async loadServices() {
			return [SERVICE];
		},
		async loadMonthAvailability(year, month): Promise<BookingMonthAvailabilityDto> {
			const daysInMonth = new Date(year, month, 0).getDate();
			const rows: MonthDayAvailabilityDto[] = [];
			for (let d = 1; d <= daysInMonth; d++) {
				rows.push({ date: toISODate(new Date(year, month - 1, d)), availableCount: 5 });
			}
			return { isClosed: false, opensOn: undefined, days: rows };
		},
		async loadSlots() {
			return [{ slot: '10:00', isPreferred: false }];
		},
		async createHold() {
			return {
				appointmentId: 'appt-1',
				lease: {
					reservationToken: 'res-tok',
					expiryTimeUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
				},
			};
		},
		async updateHold() {
			return {
				reservationToken: 'res-tok',
				expiryTimeUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
			};
		},
		attachInspiration: vi.fn(),
		requestOtp: (id, body) => spies.requestOtp(id, body),
		verifyOtp: (id, body) => spies.verifyOtp(id, body),
		confirmWithSession: (id, body) => spies.confirmWithSession(id, body),
	};
}

async function pickServiceAndSlot(): Promise<void> {
	const serviceBtn = await screen.findByTestId('booking-service-svc-1');
	await fireEvent.click(serviceBtn);
	const slotBtn = await screen.findByTestId('booking-slot-10:00', {}, { timeout: 3000 });
	await fireEvent.click(slotBtn);
}

describe('BookingFlow — pominięcie OTP dla zweryfikowanego kontaktu', () => {
	beforeEach(() => {
		window.sessionStorage.clear();
		vi.clearAllMocks();
	});

	it('z pasującą sesją potwierdza rezerwację przez confirm-with-session (bez dialogu OTP)', async () => {
		// Sesja telefoniczna zgodna z kanałem salonu (0 = telefon).
		saveSession(SLUG, {
			sessionToken: 'sess-1',
			expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
			channel: 'phone',
			contact: '+48501234567',
		});

		const confirmWithSession = vi.fn().mockResolvedValue({ requiresManualConfirmation: false });
		const verifyOtp = vi.fn();
		const requestOtp = vi.fn();
		render(BookingFlow, {
			props: { dataSource: buildDataSource({ confirmWithSession, verifyOtp, requestOtp }), salonSlug: SLUG, enableBotCheck: false },
		});

		await pickServiceAndSlot();

		// Po założeniu holdu (debounce) podsumowanie pokazuje „Zarezerwuj jako …" i włącza przycisk.
		const primary = await screen.findByTestId('booking-footer-primary', {}, { timeout: 3000 });
		await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });
		expect(screen.getByTestId('booking-footer-change-contact')).toBeTruthy();

		await fireEvent.click(primary);

		await waitFor(() => expect(confirmWithSession).toHaveBeenCalled());
		const [appointmentId, body] = confirmWithSession.mock.calls[0];
		expect(appointmentId).toBe('appt-1');
		expect(body.token).toBe('res-tok');
		expect(body.sessionToken).toBe('sess-1');

		// Skip → NIE otwiera dialogu OTP i NIE woła verify/request OTP.
		expect(screen.queryByTestId('booking-otp-contact')).toBeNull();
		expect(verifyOtp).not.toHaveBeenCalled();
		expect(requestOtp).not.toHaveBeenCalled();
	});

	it('przy włączonym bot-checku dokleja świeży token Turnstile do confirm-with-session', async () => {
		// Regresja z preflightu: confirm-with-session był jedynym anonimowym endpointem wyzwalającym
		// płatny SMS bez bot-checku. Po dołożeniu go serwerowo front MUSI wysyłać token — inaczej
		// skip-OTP przestaje działać na produkcji (400 „Nie udało się zweryfikować zabezpieczenia").
		saveSession(SLUG, {
			sessionToken: 'sess-1',
			expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
			channel: 'phone',
			contact: '+48501234567',
		});

		const confirmWithSession = vi.fn().mockResolvedValue({ requiresManualConfirmation: false });
		render(BookingFlow, {
			props: {
				dataSource: buildDataSource({ confirmWithSession, verifyOtp: vi.fn(), requestOtp: vi.fn() }),
				salonSlug: SLUG,
				enableBotCheck: true,
			},
		});

		await pickServiceAndSlot();
		const primary = await screen.findByTestId('booking-footer-primary', {}, { timeout: 3000 });
		await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });
		await fireEvent.click(primary);

		await waitFor(() => expect(confirmWithSession).toHaveBeenCalled());
		const [, body] = confirmWithSession.mock.calls[0];
		expect(body.turnstileToken).toBe('fresh-turnstile-token');
	});

	it('„Zmień" wraca do zwykłej weryfikacji OTP (otwiera dialog, nie woła confirm-with-session)', async () => {
		saveSession(SLUG, {
			sessionToken: 'sess-1',
			expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
			channel: 'phone',
			contact: '+48501234567',
		});

		const confirmWithSession = vi.fn().mockResolvedValue({ requiresManualConfirmation: false });
		const verifyOtp = vi.fn();
		const requestOtp = vi.fn();
		render(BookingFlow, {
			props: { dataSource: buildDataSource({ confirmWithSession, verifyOtp, requestOtp }), salonSlug: SLUG, enableBotCheck: false },
		});

		await pickServiceAndSlot();

		const change = await screen.findByTestId('booking-footer-change-contact', {}, { timeout: 3000 });
		await fireEvent.click(change);

		// Dialog OTP się otwiera; confirm-with-session nie jest wołane.
		await screen.findByTestId('booking-otp-contact');
		expect(confirmWithSession).not.toHaveBeenCalled();
	});

	it('bez sesji w sessionStorage pokazuje zwykłą ścieżkę OTP (brak „Zarezerwuj jako")', async () => {
		const confirmWithSession = vi.fn();
		const verifyOtp = vi.fn();
		const requestOtp = vi.fn();
		render(BookingFlow, {
			props: { dataSource: buildDataSource({ confirmWithSession, verifyOtp, requestOtp }), salonSlug: SLUG, enableBotCheck: false },
		});

		await pickServiceAndSlot();

		const primary = await screen.findByTestId('booking-footer-primary', {}, { timeout: 3000 });
		await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });
		expect(screen.queryByTestId('booking-footer-change-contact')).toBeNull();

		await fireEvent.click(primary);
		await screen.findByTestId('booking-otp-contact');
		expect(confirmWithSession).not.toHaveBeenCalled();
	});
});
