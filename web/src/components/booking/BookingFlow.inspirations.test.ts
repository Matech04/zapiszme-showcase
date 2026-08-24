import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
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

// Deferred-upload inspiracji: zdjęcia trzymane lokalnie w przeglądarce przez cały lejek; na storage
// trafiają DOPIERO po potwierdzeniu rezerwacji, autoryzowane tokenem grantu z confirm/verify-otp.
// Tu jedziemy ścieżką confirm-with-session (bez OTP) — najprostsza do wysterowania.

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

beforeAll(() => {
	let n = 0;
	URL.createObjectURL = () => `blob:mock/${n++}`;
	URL.revokeObjectURL = () => {};
});

function buildDataSource(spies: {
	attachInspiration: BookingDataSource['attachInspiration'];
	confirmWithSession: BookingDataSource['confirmWithSession'];
	createHold: BookingDataSource['createHold'];
	collectInspirationImages?: boolean;
}): BookingDataSource {
	return {
		salonSlug: SLUG,
		async loadSalon() {
			const salonInfo = {
				...SALON_INFO,
				collectInspirationImages: spies.collectInspirationImages ?? true,
			} as unknown as PublicBookingSalonInfoDto;
			return { services: [SERVICE], serviceCategories: [], salonInfo };
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
		createHold: (body, signal) => spies.createHold(body, signal),
		async updateHold() {
			return {
				reservationToken: 'res-tok',
				expiryTimeUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
			};
		},
		attachInspiration: (id, token, file, signal) => spies.attachInspiration(id, token, file, signal),
		requestOtp: vi.fn(),
		verifyOtp: vi.fn(),
		confirmWithSession: (id, body) => spies.confirmWithSession(id, body),
	};
}

async function pickServiceAndSlot(): Promise<void> {
	const serviceBtn = await screen.findByTestId('booking-service-svc-1');
	await fireEvent.click(serviceBtn);
	const slotBtn = await screen.findByTestId('booking-slot-10:00', {}, { timeout: 3000 });
	await fireEvent.click(slotBtn);
}

function fakeImageFile(name = 'hair.png'): File {
	return new File([new Uint8Array([1, 2, 3])], name, { type: 'image/png' });
}

describe('BookingFlow — deferred upload inspiracji', () => {
	beforeEach(() => {
		window.sessionStorage.clear();
		vi.clearAllMocks();
	});

	it('nie wysyła zdjęć przy holdzie, a wgrywa je tokenem grantu PO potwierdzeniu', async () => {
		// Sesja telefoniczna zgodna z kanałem salonu → potwierdzenie idzie przez confirm-with-session.
		saveSession(SLUG, {
			sessionToken: 'sess-1',
			expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
			channel: 'phone',
			contact: '+48501234567',
		});

		const attachInspiration = vi.fn().mockResolvedValue({ url: 'u', thumbnailUrl: 't', key: 'k' });
		const confirmWithSession = vi
			.fn()
			.mockResolvedValue({ requiresManualConfirmation: false, inspirationUploadToken: 'grant-tok' });
		const createHold = vi.fn().mockResolvedValue({
			appointmentId: 'appt-1',
			lease: {
				reservationToken: 'res-tok',
				expiryTimeUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
			},
		});

		const { container } = render(BookingFlow, {
			props: {
				dataSource: buildDataSource({ attachInspiration, confirmWithSession, createHold }),
				salonSlug: SLUG,
				enableBotCheck: false,
			},
		});

		await pickServiceAndSlot();

		// Dodaj zdjęcie inspiracji (trzymane lokalnie — nic nie powinno pójść na storage).
		const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;
		await fireEvent.change(fileInput, { target: { files: [fakeImageFile()] } });
		await waitFor(() => expect(container.querySelectorAll('img').length).toBe(1));

		const primary = await screen.findByTestId('booking-footer-primary', {}, { timeout: 3000 });
		await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });

		// Hold NIE dostał żadnych zdjęć (deferred). Upload nie nastąpił przed potwierdzeniem.
		expect(createHold.mock.calls.length).toBeGreaterThan(0);
		expect(createHold.mock.calls[0][0]).not.toHaveProperty('inspirationImages');
		expect(attachInspiration).not.toHaveBeenCalled();

		await fireEvent.click(primary);

		await waitFor(() => expect(confirmWithSession).toHaveBeenCalled());
		// PO potwierdzeniu — upload zdjęcia tokenem grantu do potwierdzonej wizyty.
		await waitFor(() => expect(attachInspiration).toHaveBeenCalledTimes(1));
		const [appointmentId, token, file] = attachInspiration.mock.calls[0];
		expect(appointmentId).toBe('appt-1');
		expect(token).toBe('grant-tok');
		expect((file as File).name).toBe('hair.png');
	});

	it('ukrywa sekcję inspiracji, gdy salon wyłączył funkcję', async () => {
		const attachInspiration = vi.fn();
		const confirmWithSession = vi.fn().mockResolvedValue({ requiresManualConfirmation: false });
		const createHold = vi.fn().mockResolvedValue({
			appointmentId: 'appt-1',
			lease: {
				reservationToken: 'res-tok',
				expiryTimeUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
			},
		});

		const { container } = render(BookingFlow, {
			props: {
				dataSource: buildDataSource({
					attachInspiration,
					confirmWithSession,
					createHold,
					collectInspirationImages: false,
				}),
				salonSlug: SLUG,
				enableBotCheck: false,
			},
		});

		await pickServiceAndSlot();
		// Jesteśmy na kroku podsumowania (footer aktywny), ale picker nie istnieje.
		await screen.findByTestId('booking-footer-primary', {}, { timeout: 3000 });
		expect(container.querySelector('input[type="file"]')).toBeNull();
		expect(screen.queryByText(/Inspiracje/i)).toBeNull();
	});
});
