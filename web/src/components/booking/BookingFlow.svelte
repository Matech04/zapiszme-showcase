<script lang="ts">
  import { onDestroy, onMount, untrack } from "svelte";
  import {
    bookingApiRetryAfterSeconds,
    classifyBookingError,
    isAbortError,
  } from "../../lib/booking-api-errors";
  import {
    reportBookingError,
    shortCorrelationId,
  } from "../../lib/booking/error-report";
  import type {
    AppointmentSlotDto,
    BookingServiceDto,
    PublicBookingHoldDto,
    PublicBookingSalonInfoDto,
    ServiceCategoryDto,
  } from "../../lib/booking-openapi-client";
  import {
    getFreshTurnstileToken,
    isTurnstileEnabled,
    loadTurnstileScript,
    removeTurnstile,
    renderInvisibleTurnstile,
  } from "../../lib/turnstile";
  import type {
    BookingDataSource,
    BookingEmployee,
    PendingInspirationImage,
  } from "../../lib/booking/data-source";
  import {
    toggleServiceSelection,
    makeGroupResolver,
    removeOrphanedAddons,
  } from "../../lib/booking/combo";
  import {
    MONTHS_PL,
    formatDatePl,
    normalizeTimeOnly,
    titleFromSlug,
    toISODate,
    startOfDay,
    todayISO,
  } from "../../lib/booking/format";
  import {
    buildMonthDays,
    computeAvailabilityScale,
  } from "../../lib/booking/availability";
  import { applyBookingTheme } from "../../lib/booking/theme";
  import { isPolishPhone, isValidEmail } from "../../lib/booking/phone";
  import {
    loadSession as loadVerifiedSession,
    saveSession as persistVerifiedSession,
    clearSession as dropVerifiedSession,
    isSkipEligible,
    maskContact,
    type VerifiedSession,
  } from "../../lib/booking/verified-session";
  import StepIndicator from "./parts/StepIndicator.svelte";
  import type { Step } from "./parts/types";
  import ServicePicker from "./parts/ServicePicker.svelte";
  import EmployeePicker from "./parts/EmployeePicker.svelte";
  import AvailabilitySection from "./parts/availability/AvailabilitySection.svelte";
  import {
    DATE_PICKER_DESKTOP_QUERY,
    resolveDatePickerVariant,
    type DatePickerSetting,
  } from "./parts/availability/date-picker";
  import BookingSummary from "./parts/BookingSummary.svelte";
  import InspirationsPicker from "./parts/InspirationsPicker.svelte";
  import OtpDialog from "./parts/OtpDialog.svelte";
  import SuccessScreen from "./parts/SuccessScreen.svelte";
  import StatusScreen from "./parts/StatusScreen.svelte";
  import ErrorScreen from "./parts/ErrorScreen.svelte";
  import InlineError from "./parts/InlineError.svelte";

  const HOLD_DEBOUNCE_MS = 400;
  const OTP_REQUEST_COOLDOWN_SEC = 60;
  /**
   * Fallback horyzontu rezerwacji, gdy dane salonu jeszcze nie doszły. Wartość docelowa przychodzi
   * z API (`salonInfo.bookingHorizonDays`) — wcześniej ta liczba była stałą zaszytą w dwóch
   * komponentach i w backendzie, utrzymywaną wyłącznie komentarzem.
   */
  const DEFAULT_BOOKING_HORIZON_DAYS = 120;
  const SITE_URL = import.meta.env.PUBLIC_SITE_URL ?? "https://zapisz.me";

  let {
    dataSource,
    salonSlug,
    enableBotCheck = false,
    persistHold = false,
    setDocumentTitle = false,
    otpLeaseSeconds = 180,
    layout = "single",
    datePickerVariant = "strip",
    onsalonNotFound,
  }: {
    dataSource: BookingDataSource;
    salonSlug: string;
    /** Renderuj niewidoczne Turnstile i dołączaj token do /hold + /request-otp. */
    enableBotCheck?: boolean;
    /** Zapisuj hold w localStorage (przetrwanie F5). Demo: false. */
    persistHold?: boolean;
    /** Ustawiaj document.title nazwą salonu. Demo: false. */
    setDocumentTitle?: boolean;
    /** Lokalny szacunek OtpLease do licznika po wysłaniu kodu (parytet z backendem). */
    otpLeaseSeconds?: number;
    /** Układ flow: `single` (wszystko na jednym ekranie) lub `wizard` (krok-po-kroku). */
    layout?: "single" | "wizard";
    /** Ustawienie wyboru daty (przełączalny komponent). `responsive` = pasek/mobile + siatka/desktop. Docelowo z konfiguracji salonu. */
    datePickerVariant?: DatePickerSetting;
    onsalonNotFound?: () => void;
  } = $props();

  // Szerokość ekranu dla wariantu responsywnego daty (pasek na mobile, siatka na desktopie).
  // matchMedia może nie istnieć (jsdom/SSR) → traktujemy jako mobile (pasek).
  let isDesktopWidth = $state(false);
  onMount(() => {
    if (typeof window === "undefined" || !window.matchMedia) return;
    const mq = window.matchMedia(DATE_PICKER_DESKTOP_QUERY);
    isDesktopWidth = mq.matches;
    const onChange = (e: MediaQueryListEvent) => (isDesktopWidth = e.matches);
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  });
  const effectiveDateVariant = $derived(
    resolveDatePickerVariant(datePickerVariant, isDesktopWidth),
  );

  // ── stan ─────────────────────────────────────────────────────────────────────
  const nowInit = new Date();
  let browseYear = $state(nowInit.getFullYear());
  let browseMonth = $state(nowInit.getMonth() + 1);

  /**
   * Awaria, po której kalendarz nie ma czego pokazać (nie doszły dane salonu). Zamiast czerwonego
   * paska z techniczną treścią pokazujemy pełny ekran z jednym wyjściem: „Spróbuj ponownie".
   */
  let fatalError = $state<{ title: string; message: string } | null>(null);
  let salonNotFound = $state(false);
  let bookingLimitReached = $state(false);
  let bookingPaused = $state(false);
  let bookingPauseMessage = $state<string | null>(null);
  let platformMaintenance = $state(false);
  let platformMaintenanceMessage = $state<string | null>(null);

  let employees = $state<BookingEmployee[]>([]);
  let employeesLoading = $state(false);
  // Nieudane pobranie listy osób/usług było wcześniej połykane po cichu (pusta lista) — klientka
  // widziała „brak pracowników" i wychodziła, przekonana, że salon nie przyjmuje zapisów.
  let employeesError = $state<string | null>(null);
  let services = $state<BookingServiceDto[]>([]);
  let servicesLoading = $state(false);
  let servicesError = $state<string | null>(null);
  let serviceCategories = $state<ServiceCategoryDto[]>([]);
  let initialLoading = $state(true);

  // Liczniki ponowień — każdy „Spróbuj ponownie" bumpuje swój licznik, co przeładowuje TYLKO
  // swoją sekcję (efekt czyta licznik), bez przeładowania strony i utraty wyborów klientki.
  let employeesReloadKey = $state(0);
  let servicesReloadKey = $state(0);
  let holdRetryKey = $state(0);

  let selectedDate = $state(todayISO());
  let selectedEmployeeId = $state("");
  /** Wybrane usługi combo (pierwsza = główna). Single-service = 1 element. */
  let selectedServiceIds = $state<string[]>([]);
  /** Maksymalna liczba usług w combo (spójne z backendem Appointment.MaxServices). */
  const MAX_COMBO_SERVICES = 5;

  let slots = $state<AppointmentSlotDto[]>([]);
  let slotsLoading = $state(false);
  let slotsError = $state<string | null>(null);
  let selectedSlot = $state<string | null>(null);

  let monthAvailability = $state<Map<string, number>>(new Map());
  // Czy pracownik ma w tym miesiącu JAKIKOLWIEK dzień roboczy (grafik), niezależnie od rezerwacji.
  // Gate dla auto-skoku „w przód": pracownik bez grafiku (0 dni roboczych) → nie skaczemy, tylko
  // pokazujemy „brak terminów". Pracownik z grafikiem (pełny miesiąc) → skok w przód dozwolony.
  let monthHasWorkingDay = $state(false);
  // Dopóki dostępność miesiąca leci, dni są „unknown" — pickery mają je blokować, a komunikat
  // o braku terminów milczeć. Bez tego wyścig (`dateSelectionLocked` puszcza wcześniej niż
  // przychodzą dane) pozwalał wejść na dzień w całości zajęty.
  let monthAvailLoading = $state(false);
  let monthAvailError = $state(false);
  // Salon jawnie zamknął ten miesiąc (publikacja miesiąca). Odróżnione od „brak wolnych terminów",
  // bo to zupełnie inny komunikat dla klienta — i inna decyzja: czekać czy dzwonić.
  let monthClosed = $state(false);
  /** Dzień, w którym miesiąc otworzy się sam. `null` = salon otworzy ręcznie, nie obiecujemy daty. */
  let monthOpensOn = $state<string | null>(null);
  let monthAvailabilityKey = $state("");
  let monthAvailAbort: AbortController | null = null;
  let monthAvailCriteriaKey = "";
  let autoPickMonthsTried = 0;
  // Gdy użytkownik ręcznie przełączy miesiąc (‹/›), wyłączamy auto-skok „w przód" przez puste
  // miesiące — inaczej cofanie się do miesiąca bez wolnych dni odbijałoby z powrotem do przodu.
  // Reset przy zmianie kryteriów wyszukiwania (pracownik/usługa) — wtedy auto-pick wraca.
  let userTookMonthControl = false;
  let autoPickSearchKey = "";
  let autoSelectedNearest = $state<string | null>(null);

  let slotsAbort: AbortController | null = null;
  let slotRefreshKey = $state(0);
  let slotsCriteriaKey = "";
  let bookingError = $state<string | null>(null);
  let holdSyncing = $state(false);
  let holdAutoRunId = 0;

  let holdInfo = $state<PublicBookingHoldDto | null>(null);
  let salonInfo = $state<PublicBookingSalonInfoDto | null>(null);

  // Zdjęcia inspiracji (opcjonalne, ≤3). Deferred-upload: trzymamy pliki LOKALNIE w przeglądarce
  // (podgląd z blob URL) przez cały lejek; na storage trafiają DOPIERO po potwierdzeniu OTP, autoryzowane
  // krótkożyjącym tokenem grantu (z verify-otp/confirm). Dzięki temu nic nie ląduje w buckecie dla
  // nieukończonych rezerwacji (zero sierot, brak otwartego endpointu do spamowania uploadem).
  let pendingInspirations = $state<PendingInspirationImage[]>([]);
  let inspirationUploading = $state(false);
  let inspirationUploadFailed = $state(false);

  /**
   * Wgrywa lokalnie trzymane zdjęcia inspiracji PO potwierdzeniu rezerwacji. Best-effort: błąd uploadu
   * NIE wywraca potwierdzonej wizyty — pokazujemy tylko miękkie ostrzeżenie. Zwalnia blob-podglądy.
   */
  async function uploadPendingInspirations(
    appointmentId: string,
    uploadToken: string | undefined,
  ): Promise<void> {
    const items = pendingInspirations;
    if (items.length === 0 || !uploadToken) {
      return;
    }
    inspirationUploading = true;
    inspirationUploadFailed = false;
    const ac = new AbortController();
    try {
      for (const item of items) {
        try {
          await dataSource.attachInspiration(appointmentId, uploadToken, item.file, ac.signal);
        } catch (e: unknown) {
          if (isAbortError(e)) return;
          // Miękka porażka (wizyta JEST potwierdzona), ale chcemy wiedzieć, czy upload sypie się
          // masowo — inaczej „nie udało się dodać zdjęć" nigdy do nas nie dotrze.
          reportBookingError(e, {
            operation: "attachInspiration",
            salonSlug,
            extra: { appointmentId },
          });
          inspirationUploadFailed = true;
        } finally {
          URL.revokeObjectURL(item.previewUrl);
        }
      }
    } finally {
      pendingInspirations = [];
      inspirationUploading = false;
    }
  }

  let otpContact = $state("");
  let otpFirstName = $state("");
  let otpLastName = $state("");
  let otpInstagram = $state("");
  let otpCode = $state("");
  // „Umawiam ponownie" — stały klient pomija imię (identyfikacja po kontakcie + OTP). Dotyczy
  // tylko salonów wymagających imienia; backend dopuszcza pusty komplet imię/nazwisko, gdy
  // zweryfikowany kontakt pasuje do istniejącego klienta.
  let otpReturning = $state(false);
  let otpError = $state<string | null>(null);
  let otpBusy = $state(false);
  let otpSent = $state(false);
  let otpVerified = $state(false);
  // true → salon potwierdza wizyty ręcznie; ekran sukcesu informuje o oczekiwaniu na potwierdzenie.
  let requiresManualConfirmation = $state(false);
  let otpCooldownSec = $state(0);
  let otpCooldownTimer: ReturnType<typeof setInterval> | null = null;
  let otpDialogOpen = $state(false);

  // Sesja zweryfikowanego kontaktu (in-session). Gdy pasuje do kanału salonu, oferujemy potwierdzenie
  // rezerwacji bez OTP. `skipOverridden` = użytkownik kliknął „Zmień" i chce zwykłą weryfikację.
  let verifiedSession = $state<VerifiedSession | null>(null);
  let skipOverridden = $state(false);
  let skipBusy = $state(false);

  let nowMs = $state(Date.now());

  // ── Turnstile (tylko gdy bot-check włączony — produkcja) ──────────────────────
  let turnstileContainer: HTMLDivElement | null = $state(null);
  let turnstileWidgetId: string | null = null;
  const turnstileEnabled = $derived(enableBotCheck && isTurnstileEnabled());

  onMount(() => {
    if (!turnstileEnabled) return;
    loadTurnstileScript()
      .then(() => {
        if (!turnstileContainer) return;
        turnstileWidgetId = renderInvisibleTurnstile(turnstileContainer, () => {});
      })
      .catch(() => {});
  });
  onDestroy(() => removeTurnstile(turnstileWidgetId));

  async function freshTurnstileToken(): Promise<string | undefined> {
    if (!turnstileEnabled) return undefined;
    return getFreshTurnstileToken(turnstileWidgetId);
  }

  // ── persystencja holdu w localStorage (gated) ─────────────────────────────────
  const HOLD_STORAGE_KEY = $derived(`booking_saas:hold:${salonSlug}`);
  interface PersistedHold {
    appointmentId: string;
    reservationToken: string;
    expiresAtUtc: string;
    date: string;
    employeeId: string;
    serviceIds: string[];
    slot: string;
  }
  function saveHoldToStorage(): void {
    if (!persistHold || typeof window === "undefined") return;
    if (!holdInfo?.appointmentId || !holdInfo.lease) return;
    const slot = selectedSlot;
    if (!slot || !selectedEmployeeId || selectedServiceIds.length === 0) return;
    const payload: PersistedHold = {
      appointmentId: holdInfo.appointmentId,
      reservationToken: holdInfo.lease.reservationToken ?? "",
      expiresAtUtc:
        typeof holdInfo.lease.expiryTimeUtc === "string"
          ? holdInfo.lease.expiryTimeUtc
          : ((holdInfo.lease.expiryTimeUtc as unknown as Date)?.toISOString?.() ?? ""),
      date: selectedDate,
      employeeId: selectedEmployeeId,
      serviceIds: selectedServiceIds,
      slot,
    };
    try {
      window.localStorage.setItem(HOLD_STORAGE_KEY, JSON.stringify(payload));
    } catch {
      /* quota / private-mode */
    }
  }
  function clearHoldFromStorage(): void {
    if (!persistHold || typeof window === "undefined") return;
    try {
      window.localStorage.removeItem(HOLD_STORAGE_KEY);
    } catch {
      /* ignore */
    }
  }
  function tryRestoreHoldFromStorage(): void {
    if (!persistHold || typeof window === "undefined") return;
    try {
      const raw = window.localStorage.getItem(HOLD_STORAGE_KEY);
      if (!raw) return;
      const parsed = JSON.parse(raw) as PersistedHold;
      const expires = new Date(parsed.expiresAtUtc);
      if (
        !parsed.appointmentId ||
        isNaN(expires.getTime()) ||
        expires <= new Date()
      ) {
        clearHoldFromStorage();
        return;
      }
      selectedDate = parsed.date;
      selectedEmployeeId = parsed.employeeId;
      selectedServiceIds = Array.isArray(parsed.serviceIds) ? parsed.serviceIds : [];
      selectedSlot = parsed.slot;
      holdInfo = {
        appointmentId: parsed.appointmentId,
        lease: {
          reservationToken: parsed.reservationToken,
          expiryTimeUtc: expires.toISOString(),
        },
      };
    } catch {
      clearHoldFromStorage();
    }
  }
  let holdStorageSyncReady = $state(false);
  onMount(() => {
    tryRestoreHoldFromStorage();
    holdStorageSyncReady = true;
    // Sesja zweryfikowanego kontaktu (np. z wcześniejszej rezerwacji / panelu w tej karcie).
    verifiedSession = loadVerifiedSession(salonSlug);
  });
  $effect(() => {
    if (!holdStorageSyncReady) return;
    if (holdInfo?.appointmentId && holdInfo.lease) saveHoldToStorage();
    else clearHoldFromStorage();
  });

  // ── pochodne ──────────────────────────────────────────────────────────────────
  const hasService = $derived(selectedServiceIds.length > 0);
  /** Usługa „główna" (primary) = pierwsza wybrana — używana do ładowania pracowników i pojedynczych wyświetleń. */
  const primaryServiceId = $derived(selectedServiceIds[0] ?? "");
  /** Wybrane usługi w kolejności combo. */
  const pickedServices = $derived(
    selectedServiceIds
      .map((id) => services.find((s) => s.id === id))
      .filter((s): s is BookingServiceDto => !!s),
  );
  const pickedService = $derived(pickedServices[0]);
  /** Łączny czas combo (suma) — steruje siatką slotów i podsumowaniem. */
  const comboDurationMinutes = $derived(
    pickedServices.reduce((sum, s) => sum + (s.durationInMinutes ?? 0), 0),
  );
  /** Klucz kryteriów usług do efektów (kolejność istotna). */
  const serviceIdsKey = $derived(selectedServiceIds.join(","));
  const pickedEmployee = $derived(
    employees.find((e) => e.id === selectedEmployeeId),
  );
  const browseMonthTitle = $derived(
    `${MONTHS_PL[browseMonth - 1] ?? ""} ${browseYear}`,
  );
  const availabilityScale = $derived(
    computeAvailabilityScale(monthAvailability),
  );
  const monthDays = $derived(
    buildMonthDays(browseYear, browseMonth, monthAvailability, availabilityScale),
  );
  const dateSelectionLocked = $derived(
    !hasService || !selectedEmployeeId || comboDurationMinutes <= 0,
  );
  const verifyChannel = $derived(salonInfo?.customerVerificationChannel ?? 0);
  const requireName = $derived(salonInfo?.requireCustomerName ?? false);
  const collectInstagram = $derived(salonInfo?.collectInstagramHandle ?? false);
  // Domyślnie false — funkcja jest opt-in; picker pokazujemy tylko gdy salon ją włączył.
  const collectInspirations = $derived(salonInfo?.collectInspirationImages ?? false);
  // Regulamin salonu (null/puste = brak własnego — OtpDialog pokaże domyślny link do /regulamin).
  const termsOfService = $derived(salonInfo?.termsOfService?.trim() || null);
  const salonChannel = $derived(verifyChannel === 0 ? "phone" : "email");
  // Pominięcie OTP tylko gdy: jest pasująca sesja zweryfikowanego kontaktu, salon NIE wymaga imienia
  // (bez dialogu nie mamy imienia), user nie kliknął „Zmień". Hold sprawdzamy w handlerze.
  const skipEligible = $derived(
    !skipOverridden && !requireName && isSkipEligible(verifiedSession, salonChannel),
  );
  const verifiedContactLabel = $derived(
    skipEligible && verifiedSession?.contact
      ? maskContact(salonChannel, verifiedSession.contact)
      : null,
  );
  const salonDisplayName = $derived(
    salonInfo?.name ?? titleFromSlug(salonSlug),
  );
  // Krok „Osoba" jest pierwszy. Znika tylko dla salonu solo (dokładnie 1 pracownik → auto-wybór);
  // podczas ładowania i przy 0 lub >1 pracownikach pokazujemy go.
  const showEmployeeStep = $derived(
    employeesLoading || employees.length !== 1,
  );
  const canGoPrevMonth = $derived(
    !(
      browseYear === new Date().getFullYear() &&
      browseMonth === new Date().getMonth() + 1
    ),
  );
  /** Ostatni miesiąc, w którym cokolwiek da się zarezerwować (miesiąc zawierający dziś + horyzont). */
  const maxBrowseMonthIdx = $derived.by((): number => {
    const last = new Date();
    last.setDate(
      last.getDate() +
        (salonInfo?.bookingHorizonDays ?? DEFAULT_BOOKING_HORIZON_DAYS),
    );
    return last.getFullYear() * 12 + last.getMonth();
  });
  // Blokada nawigacji „w przód" poza horyzont rezerwacji salonu.
  const canGoNextMonth = $derived(
    browseYear * 12 + (browseMonth - 1) < maxBrowseMonthIdx,
  );
  const footerPrimaryDisabled = $derived(
    !hasService ||
      otpVerified ||
      !selectedSlot ||
      holdSyncing ||
      !holdInfo?.appointmentId,
  );

  const holdRemainingSec = $derived.by((): number | null => {
    const exp = holdInfo?.lease?.expiryTimeUtc;
    if (!exp) return null;
    const ms = new Date(exp as unknown as string).getTime() - nowMs;
    return Math.max(0, Math.ceil(ms / 1000));
  });

  const steps = $derived.by((): Step[] => {
    const arr: Step[] = [];
    if (showEmployeeStep)
      arr.push({
        label: "Osoba",
        state: selectedEmployeeId ? "done" : "active",
      });
    arr.push({
      label: "Usługa",
      state: hasService ? "done" : selectedEmployeeId ? "active" : "todo",
    });
    arr.push({
      label: "Termin",
      state: selectedSlot ? "done" : hasService ? "active" : "todo",
    });
    arr.push({
      label: "Kod",
      state: otpVerified ? "done" : selectedSlot ? "active" : "todo",
      success: otpVerified,
    });
    return arr;
  });

  // ── kreator (layout="wizard") ─────────────────────────────────────────────────
  // Ekrany kreatora: jeden wybór na ekran. „Osoba" znika dla salonu solo (spójnie ze `steps`).
  let currentStep = $state(0);
  const wizardScreens = $derived.by((): Array<"service" | "employee" | "datetime" | "summary"> => {
    const s: Array<"service" | "employee" | "datetime" | "summary"> = [];
    // Kolejność: najpierw pracownik (o ile nie solo), potem usługi.
    if (showEmployeeStep) s.push("employee");
    s.push("service", "datetime", "summary");
    return s;
  });
  const currentScreen = $derived(
    wizardScreens[Math.min(currentStep, wizardScreens.length - 1)],
  );
  // Czy można przejść „Dalej" z bieżącego ekranu (twardo wymuszamy logiczny porządek).
  const canAdvance = $derived.by((): boolean => {
    switch (currentScreen) {
      case "service":
        return hasService;
      case "employee":
        return !!selectedEmployeeId;
      case "datetime":
        return !!selectedSlot && !!holdInfo?.appointmentId && !holdSyncing;
      default:
        return true;
    }
  });
  function wizardNext(): void {
    if (currentStep < wizardScreens.length - 1) currentStep += 1;
  }
  function wizardBack(): void {
    if (currentStep > 0) currentStep -= 1;
  }
  // Gdy liczba ekranów się zmieni (np. salon solo → „Osoba" znika), przytnij indeks.
  $effect(() => {
    if (currentStep > wizardScreens.length - 1)
      currentStep = wizardScreens.length - 1;
  });

  // ── obsługa OTP cooldown ──────────────────────────────────────────────────────
  function clearOtpCooldownTimer(): void {
    if (otpCooldownTimer) {
      clearInterval(otpCooldownTimer);
      otpCooldownTimer = null;
    }
  }
  function beginOtpCooldown(seconds: number): void {
    clearOtpCooldownTimer();
    otpCooldownSec = Math.max(1, Math.ceil(seconds));
    otpCooldownTimer = setInterval(() => {
      otpCooldownSec -= 1;
      if (otpCooldownSec <= 0) {
        clearOtpCooldownTimer();
        otpCooldownSec = 0;
      }
    }, 1000);
  }
  onDestroy(clearOtpCooldownTimer);

  // ── ponowienia po błędzie ─────────────────────────────────────────────────────
  // Każde z nich odświeża TYLKO swoją sekcję. Twardy reset strony (z czyszczeniem cache) zostaje
  // dla awarii fatalnej — tam i tak nie ma czego zachowywać, a tu klientka nie traci wyborów.
  function retryEmployees(): void {
    employeesReloadKey += 1;
  }
  function retryServices(): void {
    servicesReloadKey += 1;
  }
  /**
   * Dostępność miesiąca i godziny dnia idą z jednego licznika: `slotRefreshKey` wchodzi w klucz
   * kryteriów miesiąca i jest czytany przez efekt slotów, więc bump odświeża obie sekcje.
   * Kryteria (dzień/osoba/usługi) się nie zmieniają, więc wybrany slot i hold przeżywają ponowienie.
   */
  function retryAvailability(): void {
    slotRefreshKey += 1;
  }
  function retryHold(): void {
    bookingError = null;
    holdRetryKey += 1;
  }

  // ── akcje ──────────────────────────────────────────────────────────────────────
  function resetHoldAndSlot(): void {
    selectedSlot = null;
    holdInfo = null;
    bookingError = null;
  }
  function shiftBrowseMonth(delta: number, fromUser = false): void {
    if (fromUser) userTookMonthControl = true;
    let y = browseYear;
    let m = browseMonth + delta;
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
    // Górny limit: horyzont rezerwacji salonu (np. próba skoku autopickiem).
    const maxIdx = maxBrowseMonthIdx;
    if (y * 12 + (m - 1) > maxIdx) {
      y = Math.floor(maxIdx / 12);
      m = (maxIdx % 12) + 1;
    }
    browseYear = y;
    browseMonth = m;
    const isCurrentMonth = y === curY && m === curM;
    selectedDate = isCurrentMonth
      ? todayISO()
      : toISODate(new Date(y, m - 1, 1));
    resetHoldAndSlot();
  }
  function selectDay(iso: string, fromUser = false): void {
    if (selectedDate === iso) {
      if (fromUser) autoSelectedNearest = null;
      return;
    }
    selectedDate = iso;
    resetHoldAndSlot();
    if (fromUser) autoSelectedNearest = null;
  }
  /**
   * Przełącza usługę w combo wg reguły grup (patrz {@link toggleServiceSelection}). Zmiana składu
   * resetuje slot (dostępność zależy od pełnego combo). Pracownik zostaje — to on wybiera usługi.
   */
  function toggleService(id: string): void {
    const groupOf = makeGroupResolver(services);
    const toggled = toggleServiceSelection(selectedServiceIds, id, groupOf, MAX_COMBO_SERVICES);
    // Po odznaczeniu usługi głównej usuń dodatki, których nic już nie dopuszcza.
    const next = removeOrphanedAddons(toggled, services);
    // No-op (np. próba dodania ponad limit) — nie resetuj slotu niepotrzebnie.
    const unchanged =
      next.length === selectedServiceIds.length &&
      next.every((x, i) => x === selectedServiceIds[i]);
    if (unchanged) return;
    selectedServiceIds = next;
    resetHoldAndSlot();
    autoSelectedNearest = null;
  }
  /** Wraca podglądem kalendarza do bieżącego miesiąca (nowe wyszukiwanie zaczyna od „teraz"). */
  function resetBrowseToCurrentMonth(): void {
    const now = new Date();
    browseYear = now.getFullYear();
    browseMonth = now.getMonth() + 1;
    selectedDate = todayISO();
    userTookMonthControl = false;
  }
  function selectEmployee(id: string): void {
    if (id === selectedEmployeeId) return;
    selectedEmployeeId = id;
    // Usługi zależą od pracownika (inna oferta/ceny) → zaczynamy wybór usług od zera.
    selectedServiceIds = [];
    // Nowy pracownik = nowe wyszukiwanie: wracamy do bieżącego miesiąca, żeby nie dziedziczyć
    // miesiąca „doskoczonego" auto-pickiem dla poprzedniego pracownika (bug: utknięcie w październiku).
    resetBrowseToCurrentMonth();
    resetHoldAndSlot();
    autoSelectedNearest = null;
  }
  function pickSlot(time: string): void {
    bookingError = null;
    selectedSlot = selectedSlot === time ? null : time;
  }
  function handleOtpClose(): void {
    const expired = holdRemainingSec !== null && holdRemainingSec <= 0;
    otpDialogOpen = false;
    if (expired) resetHoldAndSlot();
  }

  // Powrót z trybu „wpisz kod" do edycji danych (np. zmiana numeru) — kod wpisany na stary
  // kontakt jest już nieaktualny, więc go czyścimy; cooldown zostaje (anti-abuse ponownej wysyłki).
  function editOtpContact(): void {
    otpSent = false;
    otpCode = "";
    otpError = null;
  }

  // Potwierdzenie z podsumowania: jeśli mamy ważną sesję dla tego kanału → bez OTP, inaczej dialog OTP.
  function handleConfirm(): void {
    if (skipEligible) {
      void confirmViaSession();
    } else {
      otpDialogOpen = true;
    }
  }

  // „Zmień" — rezygnacja z pominięcia (np. rezerwacja na inny kontakt) → zwykła weryfikacja OTP.
  function changeContact(): void {
    skipOverridden = true;
    otpDialogOpen = true;
  }

  async function confirmViaSession(): Promise<void> {
    const id = holdInfo?.appointmentId;
    const token = holdInfo?.lease?.reservationToken;
    const sess = verifiedSession;
    if (!id || !token || !sess?.sessionToken) {
      otpDialogOpen = true;
      return;
    }
    bookingError = null;
    skipBusy = true;
    try {
      // Skip-OTP też wyzwala płatny SMS potwierdzający, więc serwer wymaga tu Turnstile — tak samo
      // jak przy /hold i /request-otp. Świeży token pobieramy tuż przed wywołaniem (są jednorazowe).
      const turnstileToken = await freshTurnstileToken();
      const outcome = await dataSource.confirmWithSession(id, {
        token,
        sessionToken: sess.sessionToken,
        turnstileToken,
        instagramNick: collectInstagram ? otpInstagram.trim() || undefined : undefined,
      });
      requiresManualConfirmation = outcome.requiresManualConfirmation;
      otpVerified = true;
      clearHoldFromStorage();
      // Deferred-upload: wgraj trzymane lokalnie zdjęcia inspiracji (best-effort, w tle).
      void uploadPendingInspirations(id, outcome.inspirationUploadToken);
    } catch (e: unknown) {
      if (isAbortError(e)) return;
      const described = reportBookingError(e, {
        operation: "confirmWithSession",
        salonSlug,
        extra: { appointmentId: id },
      });
      // Awaria transportu/serwera NIE znaczy, że sesja jest zła. Skasowanie jej tutaj wymusiłoby
      // kolejny kod OTP, czyli realnie płatnego SMS-a za nasz błąd — pokazujemy więc błąd
      // z ponowieniem i zostawiamy sesję. Dopiero odpowiedź 4xx oznacza sesję nie do użycia.
      if (described.kind === "network" || described.kind === "offline" || described.kind === "server") {
        bookingError = described.message;
        return;
      }
      dropVerifiedSession(salonSlug);
      verifiedSession = null;
      skipOverridden = true;
      otpDialogOpen = true;
    } finally {
      skipBusy = false;
    }
  }

  async function sendOtpRequest(): Promise<void> {
    const id = holdInfo?.appointmentId;
    const token = holdInfo?.lease?.reservationToken;
    otpError = null;
    if (!id || !token) {
      otpError = "Wybierz najpierw godzinę wizyty.";
      return;
    }
    const v = otpContact.trim();
    if (verifyChannel === 0) {
      // Parytet z backendem (PhoneNumber + IsPolish): wymagamy poprawnego polskiego numeru,
      // inaczej backend odrzuciłby żądanie mylącym komunikatem już po stronie serwera.
      if (!isPolishPhone(v)) {
        otpError =
          "Podaj poprawny polski numer telefonu (9 cyfr, np. +48 500 600 700).";
        return;
      }
    } else if (!isValidEmail(v)) {
      otpError = "Podaj poprawny adres e-mail (np. ja@przyklad.pl).";
      return;
    }
    const first = otpFirstName.trim();
    const last = otpLastName.trim();
    // Stały klient („umawiam ponownie") pomija imię — wymagamy go tylko, gdy salon żąda imienia
    // i NIE zaznaczono opcji ponownej rezerwacji.
    if (requireName && !otpReturning && (!first || !last)) {
      otpError = "Podaj imię i nazwisko.";
      return;
    }
    otpBusy = true;
    try {
      const turnstileToken = await freshTurnstileToken();
      await dataSource.requestOtp(id, {
        token,
        phoneNumber: verifyChannel === 0 ? v : undefined,
        email: verifyChannel === 1 ? v : undefined,
        turnstileToken,
        firstName: requireName && !otpReturning ? first : undefined,
        lastName: requireName && !otpReturning ? last : undefined,
        isReturningCustomer: otpReturning || undefined,
      });
      otpSent = true;
      // Backend wydłuża lease do OtpLease — odzwierciedlamy lokalnie dla licznika.
      if (holdInfo?.lease) {
        holdInfo = {
          ...holdInfo,
          lease: {
            ...holdInfo.lease,
            expiryTimeUtc: new Date(
              Date.now() + otpLeaseSeconds * 1000,
            ).toISOString(),
          },
        };
      }
      beginOtpCooldown(OTP_REQUEST_COOLDOWN_SEC);
    } catch (e: unknown) {
      otpError = reportBookingError(e, {
        operation: "requestOtp",
        salonSlug,
        extra: { appointmentId: id, channel: verifyChannel === 0 ? "phone" : "email" },
      }).message;
      const ra = bookingApiRetryAfterSeconds(e);
      if (ra) beginOtpCooldown(ra);
    } finally {
      otpBusy = false;
    }
  }

  async function submitOtp(): Promise<void> {
    const id = holdInfo?.appointmentId;
    const token = holdInfo?.lease?.reservationToken;
    otpError = null;
    if (!id || !token) {
      otpError = "Sesja wygasła — wybierz godzinę ponownie.";
      return;
    }
    const code = otpCode.trim();
    if (code.length < 4) {
      otpError = "Wpisz kod z wiadomości SMS lub e-mail.";
      return;
    }
    otpBusy = true;
    try {
      const outcome = await dataSource.verifyOtp(id, {
        token,
        otp: code,
        firstName: requireName && !otpReturning ? otpFirstName.trim() : undefined,
        lastName: requireName && !otpReturning ? otpLastName.trim() : undefined,
        instagramNick: collectInstagram && !otpReturning
          ? otpInstagram.trim() || undefined
          : undefined,
      });
      requiresManualConfirmation = outcome.requiresManualConfirmation;
      otpVerified = true;
      otpDialogOpen = false;
      clearHoldFromStorage();
      // Deferred-upload: wgraj trzymane lokalnie zdjęcia inspiracji (best-effort, w tle).
      void uploadPendingInspirations(id, outcome.inspirationUploadToken);
      // Zapamiętaj sesję zweryfikowanego kontaktu (in-session) → kolejna rezerwacja na ten kontakt
      // w tej karcie pominie OTP. Token wystawia backend przy verify-otp.
      if (outcome.sessionToken) {
        const contact = otpContact.trim();
        persistVerifiedSession(salonSlug, {
          sessionToken: outcome.sessionToken,
          expiresAtUtc: outcome.sessionExpiresAtUtc ?? "",
          channel: salonChannel,
          contact: salonChannel === "email" ? contact.toLowerCase() : contact,
        });
        verifiedSession = loadVerifiedSession(salonSlug);
      }
    } catch (e: unknown) {
      otpError = reportBookingError(e, {
        operation: "verifyOtp",
        salonSlug,
        extra: { appointmentId: id },
      }).message;
      const ra = bookingApiRetryAfterSeconds(e);
      if (ra) beginOtpCooldown(ra);
    } finally {
      otpBusy = false;
    }
  }

  // ── efekty ───────────────────────────────────────────────────────────────────
  $effect(() => {
    if (salonNotFound) onsalonNotFound?.();
  });

  // Ładowanie salonu (raz na instancję — wrapper kluczuje na slug).
  $effect(() => {
    const ds = dataSource;
    const ctrl = new AbortController();
    initialLoading = true;
    fatalError = null;
    salonNotFound = false;
    bookingLimitReached = false;
    bookingPaused = false;
    bookingPauseMessage = null;
    platformMaintenance = false;
    platformMaintenanceMessage = null;
    ds.loadSalon(ctrl.signal)
      .then((bundle) => {
        // Usługi ładujemy dopiero po wyborze pracownika (cena/czas per-pracownik) — patrz efekt niżej.
        // Pracowników ładuje osobny efekt (wszyscy bookowalni na starcie) — nie zerujemy ich tutaj.
        services = [];
        serviceCategories = bundle.serviceCategories ?? [];
        salonInfo = bundle.salonInfo ?? null;
        // Priorytet: globalny tryb serwisowy platformy → wstrzymanie rezerwacji przez salon → limit.
        if (bundle.salonInfo?.isPlatformMaintenance === true) {
          platformMaintenance = true;
          platformMaintenanceMessage = bundle.salonInfo.platformMaintenanceMessage ?? null;
        } else if (bundle.salonInfo?.isBookingPaused === true) {
          bookingPaused = true;
          bookingPauseMessage = bundle.salonInfo.bookingPauseMessage ?? null;
        } else if (bundle.salonInfo?.isBookingAvailable === false) {
          bookingLimitReached = true;
        }
      })
      .catch((e: unknown) => {
        if (isAbortError(e)) return;
        // 404 to nie awaria, tylko nieistniejący salon — ma własny, spokojny ekran.
        if (classifyBookingError(e) === "not-found") {
          salonNotFound = true;
          return;
        }
        const described = reportBookingError(e, {
          operation: "loadSalon",
          salonSlug,
        });
        fatalError = { title: described.title, message: described.message };
      })
      .finally(() => {
        if (!ctrl.signal.aborted) initialLoading = false;
      });
    return () => ctrl.abort();
  });

  // Wszyscy bookowalni pracownicy — lejek zaczyna od wyboru pracownika (przed usługą). Ładujemy raz
  // na instancję (`dataSource` jest stabilny; wrapper kluczuje na slug). Pusta lista usług → backend
  // zwraca wszystkich bookowalnych.
  $effect(() => {
    const ds = dataSource;
    void employeesReloadKey;
    const ctrl = new AbortController();
    employeesLoading = true;
    employeesError = null;
    ds.loadEmployees([], ctrl.signal)
      .then((list) => {
        if (!ctrl.signal.aborted) employees = list ?? [];
      })
      .catch((e: unknown) => {
        if (isAbortError(e) || ctrl.signal.aborted) return;
        const described = reportBookingError(e, {
          operation: "loadEmployees",
          salonSlug,
        });
        employees = [];
        employeesError = described.message;
      })
      .finally(() => {
        if (!ctrl.signal.aborted) employeesLoading = false;
      });
    return () => ctrl.abort();
  });

  // Auto-wybór pracownika gdy jest dokładnie jeden (solo) — krok „Osoba" znika, przechodzimy do usług.
  $effect(() => {
    if (selectedEmployeeId) return;
    if (employees.length === 1) {
      const first = employees[0]?.id;
      if (first) selectedEmployeeId = first;
    }
  });

  // Usługi wybranego pracownika (cena/czas zresolvowane per-pracownik). Zmiana pracownika resetuje
  // wybór usług (patrz `selectEmployee`), więc lista i selekcja zawsze pasują do jednego pracownika.
  $effect(() => {
    const emp = selectedEmployeeId;
    void servicesReloadKey;
    if (!emp) {
      services = [];
      servicesLoading = false;
      servicesError = null;
      return;
    }
    const ctrl = new AbortController();
    servicesLoading = true;
    servicesError = null;
    dataSource
      .loadServices(emp, ctrl.signal)
      .then((list) => {
        if (!ctrl.signal.aborted) services = list ?? [];
      })
      .catch((e: unknown) => {
        if (isAbortError(e) || ctrl.signal.aborted) return;
        const described = reportBookingError(e, {
          operation: "loadServices",
          salonSlug,
          extra: { employeeId: emp },
        });
        services = [];
        servicesError = described.message;
      })
      .finally(() => {
        if (!ctrl.signal.aborted) servicesLoading = false;
      });
    return () => ctrl.abort();
  });

  // Dostępność miesiąca (kolory kafelków).
  $effect(() => {
    const y = browseYear;
    const mo = browseMonth;
    const emp = selectedEmployeeId;
    const svc = selectedServiceIds;
    const svcKey = serviceIdsKey;
    const refreshKey = slotRefreshKey;
    if (!emp || svc.length === 0) {
      monthAvailability = new Map();
      monthHasWorkingDay = false;
      monthClosed = false;
      monthOpensOn = null;
      monthAvailLoading = false;
      monthAvailError = false;
      monthAvailabilityKey = "";
      monthAvailCriteriaKey = "";
      return;
    }
    const criteriaKey = `${y}|${mo}|${emp}|${svcKey}|${refreshKey}`;
    if (criteriaKey === monthAvailCriteriaKey) return;
    monthAvailCriteriaKey = criteriaKey;
    monthAvailAbort?.abort();
    monthAvailAbort = new AbortController();
    const { signal } = monthAvailAbort;
    monthAvailLoading = true;
    monthAvailError = false;
    dataSource
      .loadMonthAvailability(y, mo, emp, svc, signal)
      .then((res) => {
        const map = new Map<string, number>();
        let hasWorkingDay = false;
        for (const r of res?.days ?? []) {
          if (r.date) map.set(r.date, r.availableCount ?? 0);
          if (r.isWorkingDay) hasWorkingDay = true;
        }
        monthAvailability = map;
        monthHasWorkingDay = hasWorkingDay;
        // Salon świadomie nie otworzył jeszcze tego miesiąca. Bez tego rozróżnienia pusty
        // kalendarz czyta się jak „salon nie pracuje", a nie „zapisy jeszcze nie ruszyły".
        monthClosed = res?.isClosed === true;
        monthOpensOn = res?.opensOn ?? null;
        monthAvailabilityKey = `${y}|${mo}`;
        monthAvailLoading = false;
      })
      .catch((e: unknown) => {
        // Abort = nadeszły nowsze kryteria; nowy przebieg sam ustawi loading/error.
        if (isAbortError(e)) return;
        reportBookingError(e, {
          operation: "loadMonthAvailability",
          salonSlug,
          extra: { year: y, month: mo, employeeId: emp },
        });
        monthAvailability = new Map();
        monthHasWorkingDay = false;
        monthClosed = false;
        monthOpensOn = null;
        monthAvailabilityKey = `${y}|${mo}`;
        monthAvailLoading = false;
        // Bez tego pusta mapa czytała się jako „wszystkie dni unknown" → cały miesiąc klikalny,
        // a klientka nie wiedziała, że dostępność w ogóle się nie pobrała.
        monthAvailError = true;
      });
    return () => monthAvailAbort?.abort();
  });

  // Auto-wybór najbliższego wolnego dnia + komunikat o przeskoku.
  $effect(() => {
    if (dateSelectionLocked) {
      autoPickMonthsTried = 0;
      autoSelectedNearest = null;
      return;
    }
    if (holdInfo?.appointmentId) return;
    // Nowe kryteria wyszukiwania (pracownik/usługa) → auto-pick znów może skakać „w przód".
    const searchKey = `${selectedEmployeeId}|${serviceIdsKey}`;
    if (searchKey !== autoPickSearchKey) {
      autoPickSearchKey = searchKey;
      userTookMonthControl = false;
      autoPickMonthsTried = 0;
    }
    if (monthAvailabilityKey !== `${browseYear}|${browseMonth}`) return;
    const days = monthDays;
    if (monthAvailability.size === 0) return;
    const selected = untrack(() => selectedDate);
    const current = days.find((d) => d.iso === selected);
    if (!current) return;
    if (!current.isPast && current.status !== "none") {
      autoPickMonthsTried = 0;
      return;
    }
    const firstFree = days.find((d) => !d.isPast && d.status !== "none");
    if (firstFree) {
      autoPickMonthsTried = 0;
      selectDay(firstFree.iso);
      autoSelectedNearest =
        firstFree.iso === todayISO() ? null : firstFree.iso;
      return;
    }
    // Skok „w przód" tylko gdy pracownik MA w tym miesiącu grafik (dzień roboczy), ale wszystko zajęte.
    // Pracownik bez grafiku (monthHasWorkingDay=false) → nie skaczemy do października, tylko zostajemy
    // i pokazujemy „brak wolnych terminów" w bieżącym miesiącu.
    if (
      monthHasWorkingDay &&
      !userTookMonthControl &&
      autoPickMonthsTried < 6 &&
      canGoNextMonth
    ) {
      autoPickMonthsTried += 1;
      shiftBrowseMonth(1);
    }
  });

  // Sloty wybranego dnia.
  $effect(() => {
    const date = selectedDate;
    const emp = selectedEmployeeId;
    const svc = selectedServiceIds;
    const svcKey = serviceIdsKey;
    void slotRefreshKey;
    if (!emp || svc.length === 0) {
      slots = [];
      return;
    }
    const criteriaKey = `${date}|${emp}|${svcKey}`;
    if (criteriaKey !== slotsCriteriaKey) {
      slotsCriteriaKey = criteriaKey;
      selectedSlot = null;
      bookingError = null;
      holdInfo = null;
    }
    slotsAbort?.abort();
    slotsAbort = new AbortController();
    const { signal } = slotsAbort;
    slotsLoading = true;
    slotsError = null;
    dataSource
      .loadSlots(date, emp, svc, signal)
      // svc = pełna lista usług combo
      .then((rows) => {
        const fetched = (rows ?? []).filter((r) => r.slot);
        const held = untrack(() => selectedSlot);
        if (held && !fetched.some((r) => r.slot === held)) {
          const previous = untrack(() => slots).find((r) => r.slot === held);
          fetched.push(previous ?? { slot: held, isPreferred: false });
        }
        slots = fetched;
      })
      .catch((e: unknown) => {
        if (isAbortError(e)) return;
        const described = reportBookingError(e, {
          operation: "loadSlots",
          salonSlug,
          extra: { date, employeeId: emp },
        });
        slotsError = described.message;
        slots = [];
      })
      .finally(() => {
        if (!signal.aborted) slotsLoading = false;
      });
    return () => slotsAbort?.abort();
  });

  // Hold (debounce + abort + runId guard).
  $effect(() => {
    const slot = selectedSlot;
    const date = selectedDate;
    const emp = selectedEmployeeId;
    const svc = selectedServiceIds;
    void holdRetryKey;
    // Inspiracje NIE są już częścią holdu (deferred-upload) — hold zależy tylko od slotu/usług/pracownika.
    if (!slot || !emp || svc.length === 0) {
      if (!slot) {
        holdInfo = null;
        bookingError = null;
      }
      holdSyncing = false;
      return;
    }
    holdSyncing = true;
    const runId = ++holdAutoRunId;
    const ac = new AbortController();
    const timer = setTimeout(() => {
      void (async () => {
        if (runId !== holdAutoRunId) return;
        bookingError = null;
        try {
          const startTime = normalizeTimeOnly(slot);
          const turnstileToken = await freshTurnstileToken();
          const body = {
            employeeId: emp,
            serviceIds: svc,
            date,
            startTime,
            turnstileToken,
          };
          const token = untrack(() => holdInfo?.lease?.reservationToken);
          const apptId = untrack(() => holdInfo?.appointmentId);
          if (apptId && token) {
            try {
              const lease = await dataSource.updateHold(
                apptId,
                { token, ...body },
                ac.signal,
              );
              if (runId !== holdAutoRunId) return;
              holdInfo = { appointmentId: apptId, lease };
              slotRefreshKey += 1;
            } catch (e: unknown) {
              if (isAbortError(e)) throw e;
              holdInfo = null;
              if (runId !== holdAutoRunId) return;
              const dto = await dataSource.createHold(body, ac.signal);
              if (runId !== holdAutoRunId) return;
              holdInfo = dto;
              slotRefreshKey += 1;
            }
          } else {
            const dto = await dataSource.createHold(body, ac.signal);
            if (runId !== holdAutoRunId) return;
            holdInfo = dto;
            slotRefreshKey += 1;
          }
        } catch (e: unknown) {
          if (isAbortError(e)) return;
          if (runId !== holdAutoRunId) return;
          const described = reportBookingError(e, {
            operation: "createHold",
            salonSlug,
            extra: { date, slot, employeeId: emp },
          });
          bookingError = described.message;
        } finally {
          if (runId === holdAutoRunId) holdSyncing = false;
        }
      })();
    }, HOLD_DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      ac.abort();
      holdSyncing = false;
    };
  });

  // document.title (gated).
  $effect(() => {
    if (!setDocumentTitle || typeof document === "undefined") return;
    if (salonInfo?.name) document.title = `Rezerwacja — ${salonInfo.name}`;
  });

  // Theming kalendarza z ustawień salonu: akcent (hue palety brand), tło strony, tło karty
  // (z auto-doborem jasnego/ciemnego tekstu) i kolor ceny. Brak wartości → motyw domyślny.
  $effect(() => {
    applyBookingTheme({
      accent: salonInfo?.bookingCalendarColorHex,
      background: salonInfo?.bookingCalendarBackgroundHex,
      surface: salonInfo?.bookingCalendarSurfaceHex,
      price: salonInfo?.bookingCalendarPriceHex,
    });
  });

  // Reset OTP gdy hold zniknął.
  $effect(() => {
    const appt = holdInfo?.appointmentId;
    if (!appt) {
      clearOtpCooldownTimer();
      otpCooldownSec = 0;
      otpContact = "";
      otpFirstName = "";
      otpLastName = "";
      otpInstagram = "";
      otpCode = "";
      otpReturning = false;
      otpError = null;
      otpSent = false;
      otpVerified = false;
      requiresManualConfirmation = false;
      otpDialogOpen = false;
    }
  });

  // Blokada scrolla tła pod modalem + tykający licznik blokady slotu.
  $effect(() => {
    if (typeof document === "undefined" || !otpDialogOpen) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    nowMs = Date.now();
    const tick = setInterval(() => (nowMs = Date.now()), 1000);
    return () => {
      document.body.style.overflow = prev;
      clearInterval(tick);
    };
  });

</script>

{#if turnstileEnabled}
  <div bind:this={turnstileContainer} class="absolute h-0 w-0 overflow-hidden"></div>
{/if}

{#if platformMaintenance}
  <StatusScreen variant="maintenance" salonName={salonInfo?.name} message={platformMaintenanceMessage} />
{:else if bookingPaused}
  <StatusScreen variant="paused" salonName={salonInfo?.name} message={bookingPauseMessage} />
{:else if bookingLimitReached}
  <StatusScreen variant="limit" salonName={salonInfo?.name} />
{:else if salonNotFound}
  <StatusScreen variant="not-found" siteUrl={SITE_URL} />
{:else}
  {#snippet serviceStep()}
    {#if servicesError}
      <InlineError
        message={servicesError}
        onretry={retryServices}
        testid="booking-services-error"
      />
    {:else}
      <ServicePicker
        {services}
        {serviceCategories}
        {selectedServiceIds}
        employeeSelected={!!selectedEmployeeId}
        loading={servicesLoading}
        pickedDuration={comboDurationMinutes || undefined}
        maxServices={MAX_COMBO_SERVICES}
        hideHeading={layout === "wizard"}
        ontoggle={toggleService}
      />
    {/if}
  {/snippet}

  {#snippet employeeStep()}
    {#if employeesError}
      <InlineError
        message={employeesError}
        onretry={retryEmployees}
        testid="booking-employees-error"
      />
    {:else}
      <EmployeePicker
        {employees}
        {selectedEmployeeId}
        loading={employeesLoading}
        hideHeading={layout === "wizard"}
        onselect={selectEmployee}
      />
    {/if}
  {/snippet}

  {#snippet dateTimeStep()}
    <!-- Przy zamkniętym miesiącu ten baner milczy: dotyczy daty z INNEGO miesiąca (auto-pick nie
         czyści go przy ręcznej zmianie), więc obok komunikatu „zapisy ruszają X" czytał się jak
         sprzeczna podpowiedź. Po powrocie na otwarty miesiąc wraca. -->
    {#if autoSelectedNearest && !dateSelectionLocked && !monthClosed}
      <p
        class="mb-3 rounded-2xl border border-brand-200 dark:border-brand-500/30 bg-brand-50 dark:bg-brand-500/10 px-4 py-3 text-sm font-semibold text-brand-800 dark:text-brand-300"
        role="status"
      >
        Najbliższy wolny termin to {formatDatePl(autoSelectedNearest)} — wcześniejsze dni są zajęte.
      </p>
    {/if}

    <AvailabilitySection
      variant={effectiveDateVariant}
      monthTitle={browseMonthTitle}
      days={monthDays}
      {selectedDate}
      {selectedSlot}
      locked={dateSelectionLocked}
      showLegend={!dateSelectionLocked && monthAvailability.size > 0}
      hideHeading={layout === "wizard"}
      canGoPrev={canGoPrevMonth}
      canGoNext={canGoNextMonth}
      {slots}
      slotsLoading={slotsLoading}
      {slotsError}
      syncing={holdSyncing}
      durationMinutes={comboDurationMinutes || undefined}
      prerequisitesMet={!!selectedEmployeeId && hasService}
      {monthHasWorkingDay}
      monthLoading={monthAvailLoading}
      monthError={monthAvailError}
      {monthClosed}
      {monthOpensOn}
      onprev={() => shiftBrowseMonth(-1, true)}
      onnext={() => shiftBrowseMonth(1, true)}
      onselectDay={(iso) => selectDay(iso, true)}
      onpickSlot={pickSlot}
      onretryMonth={retryAvailability}
      onretrySlots={retryAvailability}
    />

    {#if bookingError}
      <div class="mt-4">
        <InlineError
          message={bookingError}
          onretry={selectedSlot ? retryHold : undefined}
          testid="booking-hold-error"
        />
      </div>
    {/if}
  {/snippet}

  {#snippet summaryStep()}
    {#if selectedSlot && collectInspirations}
      <div class="mb-4 rounded-2xl border border-black/5 bg-black/[0.02] px-4 py-3">
        <InspirationsPicker
          bind:pending={pendingInspirations}
          disabled={otpVerified}
        />
      </div>
    {/if}
    <BookingSummary
      {pickedServices}
      comboDurationMinutes={comboDurationMinutes}
      {pickedEmployee}
      {selectedDate}
      {selectedSlot}
      primaryDisabled={footerPrimaryDisabled}
      primaryBusy={holdSyncing || skipBusy}
      {verifiedContactLabel}
      hideHeading={layout === "wizard"}
      onconfirm={handleConfirm}
      onchange={skipEligible ? changeContact : undefined}
    />
  {/snippet}

  {#snippet singleLayout()}
    <div class="mb-6">
      <StepIndicator {steps} />
    </div>
    <div class="space-y-6">
      {#if showEmployeeStep}
        {@render employeeStep()}
      {/if}
      {@render serviceStep()}
      {@render dateTimeStep()}
      {@render summaryStep()}
    </div>
  {/snippet}

  {#snippet wizardLayout()}
    {@const screenTitle =
      currentScreen === "service"
        ? "Wybierz usługę"
        : currentScreen === "employee"
          ? "Wybierz osobę"
          : currentScreen === "datetime"
            ? "Wybierz termin"
            : "Podsumowanie"}
    <div class="flex flex-1 flex-col">
      <div class="mb-5 flex items-center gap-3">
        {#if currentStep > 0}
          <button
            type="button"
            data-testid="booking-wizard-back"
            class="grid size-9 shrink-0 place-items-center rounded-full border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] text-lg font-black text-slate-600 dark:text-slate-400 transition hover:bg-slate-50 dark:hover:bg-white/10"
            aria-label="Wstecz"
            onclick={wizardBack}
          >
            ‹
          </button>
        {/if}
        <div class="min-w-0 flex-1">
          <p class="text-[11px] font-black uppercase tracking-wider text-slate-400 dark:text-slate-500">
            Krok {currentStep + 1} z {wizardScreens.length}
          </p>
          <p class="truncate text-lg font-black text-slate-950 dark:text-slate-100">
            {screenTitle}
          </p>
        </div>
        <div class="flex shrink-0 gap-1.5" aria-hidden="true">
          {#each wizardScreens as _, i (i)}
            <span
              class={`size-2 rounded-full transition ${
                i <= currentStep
                  ? "bg-[var(--accent)]"
                  : "bg-slate-200 dark:bg-white/15"
              }`}
            ></span>
          {/each}
        </div>
      </div>

      <div class="flex-1">
        {#if currentScreen === "service"}
          {@render serviceStep()}
        {:else if currentScreen === "employee"}
          {@render employeeStep()}
        {:else if currentScreen === "datetime"}
          {@render dateTimeStep()}
        {:else}
          {@render summaryStep()}
        {/if}
      </div>

      {#if currentScreen !== "summary"}
        <div class="mt-6 flex gap-2">
          {#if currentStep > 0}
            <button
              type="button"
              class="rounded-full border border-slate-200 dark:border-white/10 bg-[var(--booking-surface)] px-5 py-3.5 text-sm font-black text-slate-700 dark:text-slate-300 transition hover:bg-slate-50 dark:hover:bg-white/10"
              onclick={wizardBack}
            >
              Wstecz
            </button>
          {/if}
          <button
            type="button"
            data-testid="booking-wizard-next"
            class="flex-1 rounded-full bg-[var(--accent)] px-6 py-3.5 text-base font-black text-[var(--accent-contrast)] shadow-lg shadow-brand-900/20 transition hover:-translate-y-0.5 hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:translate-y-0"
            disabled={!canAdvance}
            onclick={wizardNext}
          >
            {currentScreen === "datetime" && holdSyncing ? "Rezerwuję slot…" : "Dalej"}
          </button>
        </div>
      {/if}
    </div>
  {/snippet}

  <div class="relative min-h-dvh bg-[var(--booking-bg)]">
    <div
      class="relative mx-auto flex min-h-dvh w-full max-w-2xl flex-col px-4 py-6 sm:px-6 sm:py-10"
    >
      <header class="mb-5 flex items-center gap-3 px-1 sm:mb-6">
        <div class="min-w-0">
          <p class="text-[11px] font-black uppercase tracking-[0.18em] text-slate-700 dark:text-slate-300">
            Rezerwacja online
          </p>
          <h1
            class="mt-0.5 truncate text-2xl font-black tracking-tight text-slate-950 dark:text-slate-100 sm:text-3xl"
          >
            {salonDisplayName}
          </h1>
        </div>
      </header>

      <div
        class="relative flex flex-1 flex-col overflow-hidden rounded-3xl border border-slate-200/80 dark:border-white/10 bg-[var(--booking-surface)] p-4 shadow-xl shadow-slate-900/5 sm:p-6"
      >
        {#if initialLoading}
          <div class="space-y-5 animate-pulse" aria-busy="true">
            <div class="h-10 rounded-2xl bg-slate-100 dark:bg-white/10"></div>
            <div class="h-28 rounded-3xl bg-slate-100 dark:bg-white/10"></div>
            <div class="h-24 rounded-3xl bg-slate-100 dark:bg-white/10"></div>
          </div>
        {:else if fatalError}
          <!-- Dane salonu nie doszły — nie ma czego pokazać, więc zamiast czerwonego paska
               z techniczną treścią dajemy ekran błędu z twardym resetem (stan + cache + reload). -->
          <ErrorScreen
            variant="panel"
            title={fatalError.title}
            message={fatalError.message}
            correlationId={shortCorrelationId()}
          />
        {:else if layout === "wizard"}
          {@render wizardLayout()}
        {:else}
          {@render singleLayout()}
        {/if}
      </div>

      <footer class="mt-6 flex justify-center pb-2">
        <a
          href={`${SITE_URL}/?utm_source=booking&utm_medium=footer&utm_campaign=powered_by`}
          target="_blank"
          rel="noopener"
          class="inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold text-slate-500 dark:text-slate-400 transition hover:text-slate-900 dark:hover:text-slate-200"
        >
          napędzane przez
          <span class="font-black text-slate-700 dark:text-slate-200">✦ Zapisz.me</span>
        </a>
      </footer>
    </div>

    {#if otpDialogOpen && holdInfo?.appointmentId}
      <OtpDialog
        {verifyChannel}
        {requireName}
        {collectInstagram}
        {termsOfService}
        bind:contact={otpContact}
        bind:firstName={otpFirstName}
        bind:lastName={otpLastName}
        bind:instagramNick={otpInstagram}
        bind:code={otpCode}
        bind:returning={otpReturning}
        busy={otpBusy}
        sent={otpSent}
        error={otpError}
        cooldownSec={otpCooldownSec}
        {holdRemainingSec}
        onsend={sendOtpRequest}
        onverify={submitOtp}
        onedit={editOtpContact}
        onclose={handleOtpClose}
      />
    {/if}

    {#if otpVerified}
      <SuccessScreen
        serviceName={pickedServices.map((s) => s.name ?? "Usługa").join(" + ") || undefined}
        employeeName={pickedEmployee
          ? `${pickedEmployee.firstName ?? ""} ${pickedEmployee.lastName ?? ""}`
          : undefined}
        {selectedDate}
        {selectedSlot}
        durationMinutes={comboDurationMinutes || undefined}
        {requiresManualConfirmation}
      />
      {#if inspirationUploading}
        <p class="mt-3 text-center text-sm opacity-70" role="status">
          Dodajemy Twoje zdjęcia inspiracji…
        </p>
      {:else if inspirationUploadFailed}
        <p class="mt-3 text-center text-sm text-amber-700" role="status">
          Nie udało się dodać niektórych zdjęć inspiracji, ale Twoja rezerwacja jest potwierdzona.
        </p>
      {/if}
    {/if}
  </div>
{/if}
