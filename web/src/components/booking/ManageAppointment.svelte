<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { createBookingApiClient } from '../../lib/booking-api-browser';
	import { reportBookingError } from '../../lib/booking/error-report';
	import {
		getFreshTurnstileToken,
		isTurnstileEnabled,
		loadTurnstileScript,
		removeTurnstile,
		renderInvisibleTurnstile
	} from '../../lib/turnstile';
	import { BookingApiException } from '../../lib/booking-openapi-client';
	import type {
		AppointmentSlotDto,
		SelfServiceAppointmentDto
	} from '../../lib/booking-openapi-client';
	import {
		MONTHS_PL,
		normalizeTimeOnly,
		todayISO,
		toISODate
	} from '../../lib/booking/format';
	import {
		buildMonthDays,
		computeAvailabilityScale,
		monthAvailabilityNotice
	} from '../../lib/booking/availability';
	import { applyBookingTheme } from '../../lib/booking/theme';
	import DateStrip from './parts/DateStrip.svelte';
	import TimeSlots from './parts/TimeSlots.svelte';
	import InlineError from './parts/InlineError.svelte';
	import {
		loadSession as loadVerifiedSession,
		saveSession as persistVerifiedSession,
		clearSession as dropVerifiedSession,
		normalizeEmail,
		normalizePhone
	} from '../../lib/booking/verified-session';

	let { salonSlug }: { salonSlug: string } = $props();

	/** Fallback horyzontu, zanim dojdą dane salonu — parytet z BookingFlow. */
	const DEFAULT_BOOKING_HORIZON_DAYS = 120;
	/** Horyzont rezerwacji salonu (dni). Źródło: publicznа konfiguracja, ta sama co w booking flow. */
	let bookingHorizonDays = $state(DEFAULT_BOOKING_HORIZON_DAYS);
	/** Ostatni miesiąc, w którym cokolwiek da się przełożyć (miesiąc zawierający dziś + horyzont). */
	const maxBrowseMonthIdx = $derived.by((): number => {
		const last = new Date();
		last.setDate(last.getDate() + bookingHorizonDays);
		return last.getFullYear() * 12 + last.getMonth();
	});

	// Sesja zweryfikowanego kontaktu — wspólny moduł (sessionStorage, per-karta). Zapisanie kanału +
	// kontaktu sprawia, że login w panelu umożliwia też pominięcie OTP przy rezerwacji na ten kontakt.
	function persistCurrentSession(): void {
		if (!sessionToken) return;
		persistVerifiedSession(salonSlug, {
			sessionToken,
			expiresAtUtc: sessionExpiresAtUtc ?? '',
			channel,
			contact: channel === 'email' ? normalizeEmail(contactEmail) : normalizePhone(contactPhone),
		});
	}
	function clearSession(): void {
		dropVerifiedSession(salonSlug);
	}
	function tryRestoreSession(): boolean {
		const s = loadVerifiedSession(salonSlug);
		if (!s) return false;
		sessionToken = s.sessionToken;
		sessionExpiresAtUtc = s.expiresAtUtc || null;
		return true;
	}

	type Step = 'contact' | 'otp' | 'list';
	type Channel = 'email' | 'phone';

	let step = $state<Step>('contact');
	// Kanał kontaktu idzie za ustawieniem salonu (jak w booking flow). Domyślnie SMS/telefon,
	// dopóki nie wczytamy salonu — nie pokazujemy ręcznego przełącznika.
	let channel = $state<Channel>('phone');
	let contactEmail = $state('');
	let contactPhone = $state('');
	let busy = $state(false);
	let errorMessage = $state<string | null>(null);
	let infoMessage = $state<string | null>(null);

	let otpCode = $state('');

	let sessionToken = $state<string | null>(null);
	let sessionExpiresAtUtc = $state<string | null>(null);

	let appointments = $state<SelfServiceAppointmentDto[]>([]);
	let appointmentsLoading = $state(false);
	/**
	 * Ponowienie dopięte do komunikatu błędu. Ustawiane tylko tam, gdzie powtórka ma sens
	 * (pobranie listy wizyt) — przy błędzie walidacji formularza przycisk byłby mylący.
	 */
	let appointmentsRetry = $state<(() => void) | null>(null);

	// reschedule modal
	let rescheduleTarget = $state<SelfServiceAppointmentDto | null>(null);
	let rescheduleDate = $state<string>('');
	let rescheduleSlots = $state<AppointmentSlotDto[]>([]);
	let rescheduleSlotsLoading = $state(false);
	let rescheduleSlotsError = $state<string | null>(null);
	let rescheduleSelected = $state<string | null>(null);
	let rescheduleBusy = $state(false);
	let rescheduleBrowseYear = $state(0);
	let rescheduleBrowseMonth = $state(0);
	let rescheduleMonthAvail = $state<Map<string, number>>(new Map());
	// Parytet z BookingFlow: rozróżniamy „pracownik nie ma grafiku" od „wszystko zajęte",
	// a dni „unknown" (dane w locie) blokujemy zamiast reklamować jako wolne.
	let rescheduleMonthHasWorkingDay = $state(false);
	/** Salon jawnie zamknął ten miesiąc — przy przekładaniu komunikat też musi to rozróżniać. */
	let rescheduleMonthClosed = $state(false);
	let rescheduleMonthOpensOn = $state<string | null>(null);
	let rescheduleMonthLoading = $state(false);
	let rescheduleMonthError = $state(false);

	// cancel confirmation dialog (spójny z resztą UI zamiast natywnego confirm())
	let cancelTarget = $state<SelfServiceAppointmentDto | null>(null);
	let cancelBusy = $state(false);

	// ── pochodne kalendarza przekładania (parytet z BookingFlow) ──────────────────
	const rescheduleAvailScale = $derived(computeAvailabilityScale(rescheduleMonthAvail));
	const rescheduleMonthDays = $derived(
		rescheduleBrowseYear
			? buildMonthDays(
					rescheduleBrowseYear,
					rescheduleBrowseMonth,
					rescheduleMonthAvail,
					rescheduleAvailScale
				)
			: []
	);
	const rescheduleHasAnyFreeDay = $derived(
		rescheduleMonthDays.some((d) => !d.isPast && d.status !== 'none')
	);
	const rescheduleMonthTitle = $derived(
		rescheduleBrowseYear ? `${MONTHS_PL[rescheduleBrowseMonth - 1] ?? ''} ${rescheduleBrowseYear}` : ''
	);
	const rescheduleCanGoPrev = $derived.by((): boolean => {
		const now = new Date();
		return !(
			rescheduleBrowseYear === now.getFullYear() &&
			rescheduleBrowseMonth === now.getMonth() + 1
		);
	});
	const rescheduleCanGoNext = $derived(
		rescheduleBrowseYear * 12 + (rescheduleBrowseMonth - 1) < maxBrowseMonthIdx
	);
	const rescheduleNotice = $derived(
		monthAvailabilityNotice({
			loading: rescheduleMonthLoading,
			error: rescheduleMonthError,
			hasAnyFreeDay: rescheduleHasAnyFreeDay,
			hasWorkingDay: rescheduleMonthHasWorkingDay,
			canGoNext: rescheduleCanGoNext,
			closed: rescheduleMonthClosed,
			opensOn: rescheduleMonthOpensOn
		})
	);
	/** Czas trwania wizyty z różnicy start–koniec — do podpisów slotów (combo nie jest w DTO). */
	const rescheduleDurationMin = $derived.by((): number | undefined => {
		const apt = rescheduleTarget;
		if (!apt) return undefined;
		const toMin = (t: string): number => {
			const [h, m] = t.split(':');
			return Number(h) * 60 + Number(m);
		};
		const d =
			toMin(apt.endTime as unknown as string) - toMin(apt.startTime as unknown as string);
		return d > 0 ? d : undefined;
	});

	// Invisible Turnstile — bot-check w tle przy request-otp self-service.
	let turnstileContainer: HTMLDivElement | null = $state(null);
	let turnstileWidgetId: string | null = null;
	const turnstileEnabled = isTurnstileEnabled();

	onMount(() => {
		if (!turnstileEnabled) return;
		loadTurnstileScript()
			.then(() => {
				if (!turnstileContainer) return;
				turnstileWidgetId = renderInvisibleTurnstile(turnstileContainer, () => {});
			})
			.catch(() => {
				// brak Turnstile → backend zwróci 400 w prod; w dev przepuszcza
			});
	});

	onDestroy(() => {
		removeTurnstile(turnstileWidgetId);
	});

	// Odtwórz sesję po odmontowaniu (przełącznik Rezerwuj/Zarządzaj) lub odświeżeniu — bez ponownego OTP.
	onMount(() => {
		if (tryRestoreSession()) {
			step = 'list';
			void loadAppointments();
		}
	});

	// Ustaw kanał i motyw wg ustawień salonu (parytet z booking flow). Błąd ładowania → zostaje
	// domyślny SMS i motyw domyślny.
	onMount(() => {
		void (async () => {
			try {
				const info = await createBookingApiClient().publicBookingSalon_Get(salonSlug);
				channel = info.customerVerificationChannel === 1 ? 'email' : 'phone';
				bookingHorizonDays = info.bookingHorizonDays ?? DEFAULT_BOOKING_HORIZON_DAYS;
				applyBookingTheme({
					accent: info.bookingCalendarColorHex,
					background: info.bookingCalendarBackgroundHex,
					surface: info.bookingCalendarSurfaceHex,
					price: info.bookingCalendarPriceHex
				});
			} catch (e: unknown) {
				// Defaulty (SMS + motyw domyślny) wystarczą, żeby panel działał, więc nie straszymy
				// klientki błędem — ale ślad w logu zostaje: to ta sama awaria, która psuje kalendarz.
				reportBookingError(e, { operation: 'manage.loadSalon', salonSlug });
			}
		})();
	});

	function formatDate(d: string): string {
		// d w formacie YYYY-MM-DD
		const [y, m, day] = d.split('-');
		if (!y || !m || !day) return d;
		return `${day}.${m}.${y}`;
	}

	function formatTime(t: string): string {
		// t może być HH:mm:ss lub HH:mm
		const [h, m] = t.split(':');
		if (!h || !m) return t;
		return `${h}:${m}`;
	}

	function statusLabel(status: string): string {
		switch (status) {
			case 'Booked':
				return 'Potwierdzona';
			case 'Pending':
				return 'Oczekująca';
			case 'Canceled':
				return 'Anulowana';
			case 'Completed':
				return 'Zrealizowana';
			default:
				return status;
		}
	}

	function statusClass(status: string): string {
		switch (status) {
			case 'Booked':
				return 'bg-emerald-100 dark:bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 border-emerald-200 dark:border-emerald-500/30';
			case 'Canceled':
				return 'bg-slate-100 dark:bg-white/10 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-white/10';
			case 'Completed':
				return 'bg-slate-900 text-white border-slate-900';
			default:
				return 'bg-amber-100 dark:bg-brand-500/15 text-slate-700 dark:text-slate-300 border-amber-200 dark:border-brand-500/30';
		}
	}

	async function requestOtp() {
		errorMessage = null;
		appointmentsRetry = null;
		infoMessage = null;
		const email = channel === 'email' ? contactEmail.trim() : '';
		const phone = channel === 'phone' ? contactPhone.trim() : '';
		if (channel === 'email' && !email.includes('@')) {
			errorMessage = 'Podaj poprawny adres e-mail.';
			return;
		}
		if (channel === 'phone' && phone.length < 9) {
			errorMessage = 'Podaj poprawny numer telefonu (z kodem kraju, np. +48…).';
			return;
		}

		busy = true;
		try {
			const client = createBookingApiClient();
			const turnstileToken = await getFreshTurnstileToken(turnstileWidgetId);
			await client.publicSelfService_RequestOtp(salonSlug, {
				email: email || undefined,
				phoneNumber: phone || undefined,
				turnstileToken
			});
			infoMessage =
				channel === 'email'
					? 'Wysłaliśmy kod na podany adres e-mail.'
					: 'Wysłaliśmy kod SMS-em.';
			step = 'otp';
		} catch (e: unknown) {
			errorMessage = reportBookingError(e, {
				operation: 'manage.requestOtp',
				salonSlug,
				extra: { channel }
			}).message;
		} finally {
			busy = false;
		}
	}

	async function verifyOtp() {
		errorMessage = null;
		const code = otpCode.trim();
		if (code.length < 4) {
			errorMessage = 'Wpisz kod, który otrzymałeś.';
			return;
		}
		busy = true;
		try {
			const client = createBookingApiClient();
			const result = await client.publicSelfService_VerifyOtp(salonSlug, {
				email: channel === 'email' ? contactEmail.trim() : undefined,
				phoneNumber: channel === 'phone' ? contactPhone.trim() : undefined,
				otp: code
			});
			sessionToken = result.sessionToken!;
			sessionExpiresAtUtc = (result.expiresAtUtc as unknown as string) ?? null;
			persistCurrentSession();
			step = 'list';
			infoMessage = null;
			await loadAppointments();
		} catch (e: unknown) {
			errorMessage = reportBookingError(e, {
				operation: 'manage.verifyOtp',
				salonSlug
			}).message;
		} finally {
			busy = false;
		}
	}

	async function loadAppointments() {
		if (!sessionToken) return;
		appointmentsLoading = true;
		errorMessage = null;
		appointmentsRetry = null;
		try {
			const client = createBookingApiClient({ selfServiceToken: sessionToken });
			appointments = await client.publicSelfService_List(salonSlug);
		} catch (e: unknown) {
			// Token wygasł / nieważny po stronie serwera → wyczyść sesję i wróć do logowania.
			if (e instanceof BookingApiException && (e.status === 401 || e.status === 403)) {
				clearSession();
				sessionToken = null;
				sessionExpiresAtUtc = null;
				appointments = [];
				step = 'contact';
				infoMessage = 'Sesja wygasła — zaloguj się ponownie.';
			} else {
				errorMessage = reportBookingError(e, {
					operation: 'manage.listAppointments',
					salonSlug
				}).message;
				// Lista jest sednem tego ekranu — dajemy jawne ponowienie zamiast samego komunikatu.
				appointmentsRetry = () => void loadAppointments();
			}
		} finally {
			appointmentsLoading = false;
		}
	}

	function askCancel(apt: SelfServiceAppointmentDto) {
		cancelTarget = apt;
	}

	function dismissCancel() {
		if (cancelBusy) return;
		cancelTarget = null;
	}

	async function confirmCancel() {
		const apt = cancelTarget;
		if (!sessionToken || !apt?.id) return;
		cancelBusy = true;
		try {
			const client = createBookingApiClient({ selfServiceToken: sessionToken });
			await client.publicSelfService_Cancel(salonSlug, apt.id);
			infoMessage = 'Wizyta została anulowana.';
			cancelTarget = null;
			await loadAppointments();
		} catch (e: unknown) {
			errorMessage = reportBookingError(e, {
				operation: 'manage.cancelAppointment',
				salonSlug,
				extra: { appointmentId: apt.id }
			}).message;
		} finally {
			cancelBusy = false;
		}
	}

	function parseISODateLocal(iso: string): Date {
		const [y, m, d] = iso.split('-').map(Number);
		return new Date(y || 1970, (m || 1) - 1, d || 1);
	}

	/**
	 * Pełny skład combo wizyty — dostępność/sloty muszą uwzględniać wszystkie usługi, nie tylko
	 * główną, inaczej slot „wolny" dla usługi głównej nie pomieści całego combo. Fallback na
	 * `serviceId` dla danych sprzed dodania pola `serviceIds`.
	 */
	function comboServiceIds(apt: SelfServiceAppointmentDto): string[] {
		if (apt.serviceIds && apt.serviceIds.length > 0) return apt.serviceIds;
		return apt.serviceId ? [apt.serviceId] : [];
	}

	function startReschedule(apt: SelfServiceAppointmentDto) {
		rescheduleTarget = apt;
		const initial = (apt.date as unknown as string) ?? todayISO();
		rescheduleDate = initial;
		rescheduleSelected = null;
		rescheduleSlots = [];
		rescheduleSlotsError = null;
		rescheduleMonthAvail = new Map();
		rescheduleMonthHasWorkingDay = false;
		rescheduleMonthClosed = false;
		rescheduleMonthOpensOn = null;
		rescheduleMonthError = false;
		rescheduleMonthLoading = true;
		const d = parseISODateLocal(initial);
		rescheduleBrowseYear = d.getFullYear();
		rescheduleBrowseMonth = d.getMonth() + 1;
		void loadRescheduleMonthAvail();
		void loadRescheduleSlots();
	}

	function cancelReschedule() {
		if (rescheduleBusy) return;
		rescheduleTarget = null;
		rescheduleSelected = null;
		rescheduleSlots = [];
		rescheduleSlotsError = null;
		rescheduleMonthAvail = new Map();
	}

	async function loadRescheduleMonthAvail() {
		const apt = rescheduleTarget;
		if (!apt || !apt.employeeId || !apt.serviceId || !rescheduleBrowseYear) return;
		rescheduleMonthLoading = true;
		rescheduleMonthError = false;
		try {
			const client = createBookingApiClient({ selfServiceToken: sessionToken });
			const res = await client.bookingAppointments_GetMonthAvailability(
				salonSlug,
				rescheduleBrowseYear,
				rescheduleBrowseMonth,
				apt.employeeId,
				comboServiceIds(apt)
			);
			const map = new Map<string, number>();
			let hasWorkingDay = false;
			for (const r of res?.days ?? []) {
				if (r.date) map.set(r.date, r.availableCount ?? 0);
				if (r.isWorkingDay) hasWorkingDay = true;
			}
			rescheduleMonthClosed = res?.isClosed === true;
			rescheduleMonthOpensOn = res?.opensOn ?? null;
			rescheduleMonthAvail = map;
			rescheduleMonthHasWorkingDay = hasWorkingDay;
			autoPickRescheduleDay(map);
		} catch (e: unknown) {
			reportBookingError(e, {
				operation: 'manage.rescheduleMonthAvailability',
				salonSlug,
				extra: { year: rescheduleBrowseYear, month: rescheduleBrowseMonth }
			});
			rescheduleMonthAvail = new Map();
			rescheduleMonthHasWorkingDay = false;
			rescheduleMonthClosed = false;
			rescheduleMonthOpensOn = null;
			rescheduleMonthError = true;
		} finally {
			rescheduleMonthLoading = false;
		}
	}

	/**
	 * Modal otwiera się na dacie BIEŻĄCEJ wizyty, która bywa w całości zajęta — a w przeciwieństwie
	 * do BookingFlow nie było tu nic, co by taki wybór poprawiło. Efekt: pusta siatka godzin zaraz
	 * po otwarciu. Po dojściu dostępności przeskakujemy na najbliższy wolny dzień miesiąca.
	 */
	function autoPickRescheduleDay(avail: Map<string, number>) {
		if (avail.size === 0 || !rescheduleBrowseYear) return;
		const days = buildMonthDays(
			rescheduleBrowseYear,
			rescheduleBrowseMonth,
			avail,
			computeAvailabilityScale(avail)
		);
		const current = days.find((d) => d.iso === rescheduleDate);
		if (current && !current.isPast && current.status !== 'none') return;
		const firstFree = days.find((d) => !d.isPast && d.status !== 'none');
		if (!firstFree || firstFree.iso === rescheduleDate) return;
		rescheduleDate = firstFree.iso;
		rescheduleSelected = null;
		void loadRescheduleSlots();
	}

	async function loadRescheduleSlots() {
		const apt = rescheduleTarget;
		if (!apt || !rescheduleDate || !apt.employeeId || !apt.serviceId) return;
		rescheduleSlotsError = null;
		rescheduleSlotsLoading = true;
		try {
			const client = createBookingApiClient({ selfServiceToken: sessionToken });
			rescheduleSlots = await client.bookingAppointments_GetAvailableSlots(
				salonSlug,
				rescheduleDate,
				apt.employeeId,
				comboServiceIds(apt)
			);
		} catch (e: unknown) {
			rescheduleSlotsError = reportBookingError(e, {
				operation: 'manage.rescheduleSlots',
				salonSlug,
				extra: { date: rescheduleDate }
			}).message;
			rescheduleSlots = [];
		} finally {
			rescheduleSlotsLoading = false;
		}
	}

	function shiftRescheduleMonth(delta: number) {
		let y = rescheduleBrowseYear;
		let m = rescheduleBrowseMonth + delta;
		while (m > 12) {
			m -= 12;
			y++;
		}
		while (m < 1) {
			m += 12;
			y--;
		}
		const now = new Date();
		const curY = now.getFullYear();
		const curM = now.getMonth() + 1;
		if (y < curY || (y === curY && m < curM)) {
			y = curY;
			m = curM;
		}
		const maxIdx = maxBrowseMonthIdx;
		if (y * 12 + (m - 1) > maxIdx) {
			y = Math.floor(maxIdx / 12);
			m = (maxIdx % 12) + 1;
		}
		rescheduleBrowseYear = y;
		rescheduleBrowseMonth = m;
		const isCurrentMonth = y === curY && m === curM;
		rescheduleDate = isCurrentMonth ? todayISO() : toISODate(new Date(y, m - 1, 1));
		rescheduleSelected = null;
		rescheduleSlots = [];
		// Dostępność poprzedniego miesiąca nie opisuje nowego — czyścimy, żeby stare kropki
		// nie wisiały nad nową siatką. Datę (1. dzień / dziś) koryguje autoPickRescheduleDay.
		rescheduleMonthAvail = new Map();
		rescheduleMonthHasWorkingDay = false;
		rescheduleMonthClosed = false;
		rescheduleMonthOpensOn = null;
		rescheduleMonthLoading = true;
		void loadRescheduleMonthAvail();
		void loadRescheduleSlots();
	}

	function selectRescheduleDay(iso: string) {
		if (rescheduleDate === iso) return;
		rescheduleDate = iso;
		rescheduleSelected = null;
		void loadRescheduleSlots();
	}

	function pickRescheduleSlot(slot: string) {
		rescheduleSelected = rescheduleSelected === slot ? null : slot;
	}

	async function confirmReschedule() {
		const apt = rescheduleTarget;
		if (!apt || !apt.id || !rescheduleSelected || !sessionToken) return;
		if (!apt.employeeId || !apt.serviceId) {
			errorMessage = 'Brak danych pracownika lub usługi.';
			return;
		}
		rescheduleBusy = true;
		try {
			const client = createBookingApiClient({ selfServiceToken: sessionToken });
			await client.publicSelfService_Reschedule(salonSlug, apt.id, {
				employeeId: apt.employeeId,
				serviceId: apt.serviceId,
				date: rescheduleDate,
				startTime: normalizeTimeOnly(rescheduleSelected)
			} as unknown as Parameters<typeof client.publicSelfService_Reschedule>[2]);
			infoMessage = 'Wizyta została przełożona.';
			rescheduleTarget = null;
			rescheduleSelected = null;
			await loadAppointments();
		} catch (e: unknown) {
			errorMessage = reportBookingError(e, {
				operation: 'manage.reschedule',
				salonSlug,
				extra: { date: rescheduleDate }
			}).message;
		} finally {
			rescheduleBusy = false;
		}
	}

	function logout() {
		clearSession();
		sessionToken = null;
		sessionExpiresAtUtc = null;
		appointments = [];
		step = 'contact';
		otpCode = '';
		errorMessage = null;
		appointmentsRetry = null;
		infoMessage = null;
	}
</script>

{#if turnstileEnabled}
	<div bind:this={turnstileContainer} class="absolute h-0 w-0 overflow-hidden"></div>
{/if}

<div class="relative mx-auto w-full max-w-2xl">
	<div class="rounded-4xl border border-white/80 dark:border-white/10 bg-[var(--booking-surface)] p-6 shadow-2xl shadow-slate-900/10 sm:p-8">
		<header class="mb-6 flex items-center justify-between gap-4">
			<div>
				<p class="text-xs font-bold uppercase tracking-[0.2em] text-slate-700 dark:text-slate-300">
					Zarządzaj wizytą
				</p>
				<h2 class="mt-1 text-2xl font-black text-slate-950 dark:text-slate-100 sm:text-3xl">
					{step === 'contact' ? 'Zaloguj się kodem' : step === 'otp' ? 'Wpisz kod' : 'Twoje wizyty'}
				</h2>
			</div>
			{#if step === 'list'}
				<button
					type="button"
					onclick={logout}
					class="rounded-full border border-slate-200 dark:border-white/10 px-4 py-2 text-xs font-bold uppercase tracking-wider text-slate-700 dark:text-slate-300 hover:border-slate-400"
				>
					Wyloguj
				</button>
			{/if}
		</header>

		{#if errorMessage}
			<div class="mb-5">
				<InlineError
					message={errorMessage}
					onretry={appointmentsRetry ?? undefined}
					testid="manage-error"
				/>
			</div>
		{/if}

		{#if infoMessage}
			<div
				class="mb-5 rounded-3xl border border-emerald-200 dark:border-emerald-500/30 bg-emerald-50 dark:bg-emerald-500/15 px-4 py-3 text-sm font-medium text-emerald-900 dark:text-emerald-200"
			>
				{infoMessage}
			</div>
		{/if}

		{#if step === 'contact'}
			<div class="space-y-5">
				<p class="text-sm leading-6 text-slate-700 dark:text-slate-300">
					Wpisz {channel === 'email' ? 'adres e-mail' : 'numer telefonu'} użyty przy rezerwacji —
					wyślemy na niego kod weryfikacyjny.
				</p>

				{#if channel === 'email'}
					<label class="block">
						<span class="mb-1 block text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400">
							Adres e-mail
						</span>
						<input
							type="email"
							data-testid="manage-contact-email"
								bind:value={contactEmail}
							placeholder="ty@example.com"
							autocomplete="email"
							class="w-full rounded-2xl border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] px-4 py-3 text-sm font-medium text-slate-950 dark:text-slate-100 outline-none transition focus:border-slate-900"
						/>
					</label>
				{:else}
					<label class="block">
						<span class="mb-1 block text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400">
							Numer telefonu
						</span>
						<input
							type="tel"
							data-testid="manage-contact-phone"
								bind:value={contactPhone}
							placeholder="600 000 000"
							autocomplete="tel"
							class="w-full rounded-2xl border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] px-4 py-3 text-sm font-medium text-slate-950 dark:text-slate-100 outline-none transition focus:border-slate-900"
						/>
					</label>
				{/if}

				<button
					type="button"
					disabled={busy}
					data-testid="manage-send-otp"
					onclick={requestOtp}
					class="w-full rounded-full bg-[var(--accent)] px-6 py-3 text-sm font-black uppercase tracking-wider text-[var(--accent-contrast)] transition disabled:opacity-50"
				>
					{busy ? 'Wysyłam…' : 'Wyślij kod'}
				</button>
			</div>
		{:else if step === 'otp'}
			<div class="space-y-5">
				<p class="text-sm leading-6 text-slate-700 dark:text-slate-300">
					Wpisz 6-cyfrowy kod, który wysłaliśmy na
					<strong>{channel === 'email' ? contactEmail : contactPhone}</strong>.
				</p>

				<input
					type="text"
					inputmode="numeric"
					maxlength="6"
					autocomplete="one-time-code"
					data-testid="manage-otp-code"
						bind:value={otpCode}
					placeholder="123456"
					class="w-full rounded-2xl border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] px-4 py-4 text-center text-3xl font-black tracking-[0.5em] text-slate-950 dark:text-slate-100 outline-none transition focus:border-slate-900"
				/>

				<div class="flex gap-2">
					<button
						type="button"
						class="flex-1 rounded-full border border-slate-200 dark:border-white/10 px-4 py-3 text-sm font-bold text-slate-700 dark:text-slate-300 hover:border-slate-400"
						onclick={() => (step = 'contact')}
					>
						Wróć
					</button>
					<button
						type="button"
						disabled={busy}
						data-testid="manage-verify-otp"
						onclick={verifyOtp}
						class="flex-1 rounded-full bg-[var(--accent)] px-6 py-3 text-sm font-black uppercase tracking-wider text-[var(--accent-contrast)] transition disabled:opacity-50"
					>
						{busy ? 'Sprawdzam…' : 'Potwierdź kod'}
					</button>
				</div>
			</div>
		{:else if step === 'list'}
			<div class="space-y-4">
				{#if appointmentsLoading}
					<div class="space-y-3" aria-busy="true">
						{#each Array.from({ length: 3 }) as _, i (i)}
							<div class="h-24 animate-pulse rounded-3xl bg-slate-100 dark:bg-white/10"></div>
						{/each}
					</div>
				{:else if appointments.length === 0}
					<div class="rounded-3xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-6 py-10 text-center">
						<p class="text-sm font-medium text-slate-600 dark:text-slate-400">
							Brak nadchodzących lub niedawnych wizyt.
						</p>
					</div>
				{:else}
					{#each appointments as apt (apt.id)}
						<article class="rounded-3xl border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] p-4 sm:p-5">
							<div class="flex flex-wrap items-start justify-between gap-3">
								<div>
									<h3 class="text-base font-black text-slate-950 dark:text-slate-100">{apt.serviceName}</h3>
									<p class="mt-0.5 text-sm text-slate-600 dark:text-slate-400">
										u {apt.employeeFirstName} {apt.employeeLastName}
									</p>
									<p class="mt-2 text-sm font-medium text-slate-900 dark:text-slate-100">
										{formatDate(apt.date as unknown as string)}
										<span class="mx-1 text-slate-400 dark:text-slate-500">·</span>
										{formatTime(apt.startTime as unknown as string)} – {formatTime(apt.endTime as unknown as string)}
									</p>
								</div>
								<span
									class={`rounded-full border px-3 py-1 text-[10px] font-black uppercase tracking-wider ${statusClass(apt.status as unknown as string)}`}
								>
									{statusLabel(apt.status as unknown as string)}
								</span>
							</div>

							{#if apt.canCancel || apt.canReschedule}
								<div class="mt-4 flex flex-wrap gap-2">
									{#if apt.canReschedule}
										<button
											type="button"
											data-testid="manage-appointment-reschedule"
											onclick={() => startReschedule(apt)}
											class="rounded-full border border-slate-200 dark:border-white/10 px-4 py-2 text-xs font-bold uppercase tracking-wider text-slate-700 dark:text-slate-300 hover:border-slate-400"
										>
											Przełóż
										</button>
									{/if}
									{#if apt.canCancel}
										<button
											type="button"
											data-testid="manage-appointment-cancel"
											onclick={() => askCancel(apt)}
											class="rounded-full border border-red-200 bg-red-50 px-4 py-2 text-xs font-bold uppercase tracking-wider text-red-700 hover:bg-red-100"
										>
											Anuluj
										</button>
									{/if}
								</div>
							{/if}
						</article>
					{/each}
				{/if}
			</div>
		{/if}
	</div>
</div>

{#if rescheduleTarget}
	<div
		class="fixed inset-0 z-50 flex items-end justify-center bg-slate-950/60 px-4 py-6 sm:items-center sm:py-12"
		role="dialog"
		aria-modal="true"
	>
		<div class="w-full max-w-xl rounded-4xl bg-[var(--booking-surface)] p-6 shadow-2xl">
			<header class="mb-4 flex items-start justify-between gap-4">
				<div>
					<p class="text-xs font-bold uppercase tracking-wider text-slate-700 dark:text-slate-300">Przełóż wizytę</p>
					<h3 class="mt-1 text-xl font-black text-slate-950 dark:text-slate-100">
						{rescheduleTarget.serviceName}
					</h3>
					<p class="mt-1 text-xs text-slate-500 dark:text-slate-400">
						Aktualnie: {formatDate(rescheduleTarget.date as unknown as string)}
						{formatTime(rescheduleTarget.startTime as unknown as string)}
					</p>
				</div>
				<button
					type="button"
					onclick={cancelReschedule}
					class="rounded-full p-2 text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10 dark:hover:text-white"
					aria-label="Zamknij"
				>
					<span aria-hidden="true">×</span>
				</button>
			</header>

			<div class="mt-2" data-testid="manage-reschedule-date">
				<DateStrip
					monthTitle={rescheduleMonthTitle}
					days={rescheduleMonthDays}
					selectedDate={rescheduleDate}
					showLegend={rescheduleMonthAvail.size > 0}
					monthLoading={rescheduleMonthLoading}
					canGoPrev={rescheduleCanGoPrev}
					canGoNext={rescheduleCanGoNext}
					onprev={() => shiftRescheduleMonth(-1)}
					onnext={() => shiftRescheduleMonth(1)}
					onselect={selectRescheduleDay}
				/>
			</div>

			<div class="mt-2 max-h-72 overflow-y-auto">
				{#if rescheduleMonthError}
					<!-- Awaria dostępności ≠ brak terminów: dajemy ponowienie samego miesiąca. -->
					<InlineError
						message={rescheduleNotice ?? 'Nie udało się sprawdzić dostępności w tym miesiącu.'}
						onretry={() => void loadRescheduleMonthAvail()}
						testid="manage-month-error"
					/>
				{:else if rescheduleNotice}
					<p
						class="rounded-2xl border border-dashed border-slate-200 dark:border-white/10 bg-slate-50 dark:bg-white/5 px-4 py-4 text-sm text-slate-600 dark:text-slate-400"
						role="status"
					>
						{rescheduleNotice}
					</p>
				{:else}
					<TimeSlots
						prerequisitesMet={true}
						loading={rescheduleSlotsLoading}
						error={rescheduleSlotsError}
						slots={rescheduleSlots}
						selectedSlot={rescheduleSelected}
						durationMinutes={rescheduleDurationMin}
						emptyMessage="Brak wolnych godzin w tym dniu — wybierz inny."
						onpick={pickRescheduleSlot}
						onretry={() => void loadRescheduleSlots()}
					/>
				{/if}
			</div>

			<div class="mt-5 flex gap-2">
				<button
					type="button"
					class="flex-1 rounded-full border border-slate-200 dark:border-white/10 px-4 py-3 text-sm font-bold text-slate-700 dark:text-slate-300"
					onclick={cancelReschedule}
				>
					Anuluj
				</button>
				<button
					type="button"
					disabled={!rescheduleSelected || rescheduleBusy}
					data-testid="manage-reschedule-confirm"
					onclick={confirmReschedule}
					class="flex-1 rounded-full bg-[var(--accent)] px-6 py-3 text-sm font-black uppercase tracking-wider text-[var(--accent-contrast)] disabled:opacity-50"
				>
					{rescheduleBusy ? 'Przekładam…' : 'Potwierdź'}
				</button>
			</div>
		</div>
	</div>
{/if}

{#if cancelTarget}
	<div
		class="fixed inset-0 z-50 flex items-end justify-center bg-slate-950/60 px-4 py-6 sm:items-center sm:py-12"
		role="dialog"
		aria-modal="true"
	>
		<div class="w-full max-w-sm rounded-4xl bg-[var(--booking-surface)] p-6 shadow-2xl">
			<p class="text-xs font-bold uppercase tracking-wider text-red-700 dark:text-red-300">Anuluj wizytę</p>
			<h3 class="mt-1 text-xl font-black text-slate-950 dark:text-slate-100">
				{cancelTarget.serviceName}
			</h3>
			<p class="mt-2 text-sm text-slate-600 dark:text-slate-400">
				{formatDate(cancelTarget.date as unknown as string)}
				<span class="mx-1 text-slate-400 dark:text-slate-500">·</span>
				{formatTime(cancelTarget.startTime as unknown as string)}
			</p>
			<p class="mt-3 text-sm text-slate-600 dark:text-slate-400">
				Czy na pewno chcesz anulować tę wizytę? Tej operacji nie można cofnąć.
			</p>

			<div class="mt-5 flex gap-2">
				<button
					type="button"
					disabled={cancelBusy}
					class="flex-1 rounded-full border border-slate-200 dark:border-white/10 px-4 py-3 text-sm font-bold text-slate-700 dark:text-slate-300 disabled:opacity-50"
					onclick={dismissCancel}
				>
					Wróć
				</button>
				<button
					type="button"
					disabled={cancelBusy}
					data-testid="manage-cancel-confirm"
					onclick={confirmCancel}
					class="flex-1 rounded-full bg-red-600 px-6 py-3 text-sm font-black uppercase tracking-wider text-white disabled:opacity-50"
				>
					{cancelBusy ? 'Anuluję…' : 'Anuluj wizytę'}
				</button>
			</div>
		</div>
	</div>
{/if}
