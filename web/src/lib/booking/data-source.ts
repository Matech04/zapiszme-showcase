/**
 * Abstrakcja źródła danych rezerwacji.
 *
 * Orkiestrator `BookingFlow` operuje wyłącznie na tym interfejsie, dzięki czemu kalendarz
 * klienta (źródło `apiBookingDataSource`) i demo (`mockBookingDataSource`) używają TEGO SAMEGO
 * komponentu i zachowują się identycznie — różnią się tylko warstwą danych.
 *
 * Wszystkie metody mogą rzucać; orkiestrator mapuje błędy przez `bookingApiErrorMessage`.
 * `AbortSignal` przerywa request rzucając błędem o `name === "AbortError"` (jak fetch).
 */
import type {
  AppointmentSlotDto,
  BookingEmployeeDto,
  BookingServiceDto,
  HoldLease,
  BookingMonthAvailabilityDto,
  MonthDayAvailabilityDto,
  PublicBookingHoldDto,
  PublicBookingSalonInfoDto,
  ServiceCategoryDto,
} from "../booking-openapi-client";

/**
 * Pracownik na publicznej liście.
 *
 * `hasUpcomingSchedule = false` → pracownik nie ma ANI JEDNEGO dnia roboczego w oknie rezerwacji
 * (dziś → koniec miesiąca +3), więc kreator blokuje jego kafelek zamiast wpuszczać klientkę
 * w pusty kalendarz. To grafik, nie wolne sloty: przed wyborem usługi nie znamy czasu trwania,
 * więc zajętości policzyć się nie da.
 *
 * Pole jest opcjonalne celowo — `undefined` (starszy backend) czytamy jako „dostępny", żeby brak
 * danych nigdy nie zablokował rezerwacji.
 */
export type BookingEmployee = BookingEmployeeDto & {
  hasUpcomingSchedule?: boolean;
};

/** Zdjęcie inspiracji już przetworzone i wgrane przez endpoint uploadu (URL + miniatura + klucz). */
export interface InspirationImage {
  url: string;
  thumbnailUrl: string;
  key: string;
}

/**
 * Zdjęcie inspiracji trzymane lokalnie w przeglądarce DO potwierdzenia rezerwacji (deferred-upload).
 * Plik nie trafia na storage dopóki wizyta nie zostanie potwierdzona OTP-em — `previewUrl` to lokalny
 * `URL.createObjectURL(file)` do podglądu (orkiestrator zwalnia go po uploadzie/odrzuceniu).
 */
export interface PendingInspirationImage {
  /** Stabilny identyfikator pozycji (do klucza listy i usuwania). */
  id: string;
  file: File;
  previewUrl: string;
}

/** Maks. liczba zdjęć inspiracji na rezerwację — egzekwowane też serwerowo (agregat + walidator). */
export const MAX_INSPIRATION_IMAGES = 3;

/** Treść /hold i /update — token Turnstile dokleja orkiestrator (gdy bot-check włączony). */
export interface HoldRequest {
  employeeId: string;
  /** Usługi combo (pierwsza = główna). Single-service = 1 element. */
  serviceIds: string[];
  date: string;
  startTime: string;
  turnstileToken?: string;
}

export interface RequestOtpInput {
  token: string;
  phoneNumber?: string;
  email?: string;
  turnstileToken?: string;
  firstName?: string;
  lastName?: string;
  /** „Umawiam ponownie" — stały klient pomija imię, identyfikujemy go po kontakcie po weryfikacji OTP. */
  isReturningCustomer?: boolean;
}

export interface VerifyOtpInput {
  token: string;
  otp: string;
  firstName?: string;
  lastName?: string;
  instagramNick?: string;
}

/** Potwierdzenie rezerwacji ważną sesją zweryfikowanego kontaktu (pomija OTP — brak SMS). */
export interface ConfirmWithSessionInput {
  token: string;
  sessionToken: string;
  /**
   * Token Invisible Turnstile — dokleja orkiestrator, tak samo jak przy /hold i /request-otp.
   * Potwierdzenie sesją wyzwala płatny SMS potwierdzający, więc serwer wymaga tu bot-checku:
   * bez niego botnet rozsiany po adresach mnożył capy per-IP przez liczbę IP.
   */
  turnstileToken?: string;
  firstName?: string;
  lastName?: string;
  instagramNick?: string;
}

export interface SalonBundle {
  services: BookingServiceDto[];
  serviceCategories: ServiceCategoryDto[];
  salonInfo: PublicBookingSalonInfoDto | null;
}

export interface BookingDataSource {
  /** Slug salonu — klucz localStorage holdu i tekst potwierdzenia. */
  readonly salonSlug: string;

  loadSalon(signal: AbortSignal): Promise<SalonBundle>;
  loadEmployees(
    serviceIds: string[],
    signal: AbortSignal,
  ): Promise<BookingEmployee[]>;
  /**
   * Usługi oferowane przez konkretnego pracownika, z ceną/czasem zresolvowanym per-pracownik
   * (override CustomPrice/CustomDuration, fallback do katalogu). Klient wybiera pracownika PRZED
   * usługą, dzięki czemu widzi właściwą cenę TEGO pracownika, a nie domyślną katalogową.
   */
  loadServices(
    employeeId: string,
    signal: AbortSignal,
  ): Promise<BookingServiceDto[]>;
  loadMonthAvailability(
    year: number,
    month: number,
    employeeId: string,
    serviceIds: string[],
    signal: AbortSignal,
  ): Promise<BookingMonthAvailabilityDto>;
  loadSlots(
    date: string,
    employeeId: string,
    serviceIds: string[],
    signal: AbortSignal,
  ): Promise<AppointmentSlotDto[]>;

  createHold(
    body: HoldRequest,
    signal: AbortSignal,
  ): Promise<PublicBookingHoldDto>;
  /** Aktualizacja istniejącego holdu (zmiana slotu w obrębie tej samej sesji). */
  updateHold(
    appointmentId: string,
    body: HoldRequest & { token: string },
    signal: AbortSignal,
  ): Promise<HoldLease>;

  /**
   * Wgrywa pojedyncze zdjęcie inspiracji do JUŻ POTWIERDZONEJ wizyty (deferred-upload). Autoryzuje
   * krótkożyjący token grantu (`uploadToken`) zwrócony z `verifyOtp`/`confirmWithSession`. Wołane
   * dopiero po potwierdzeniu — zero sierot na storage. Rzuca przy nie-obrazie / przekroczeniu rozmiaru.
   */
  attachInspiration(
    appointmentId: string,
    uploadToken: string,
    file: File,
    signal: AbortSignal,
  ): Promise<InspirationImage>;

  requestOtp(appointmentId: string, body: RequestOtpInput): Promise<void>;
  verifyOtp(
    appointmentId: string,
    body: VerifyOtpInput,
  ): Promise<VerifyOtpOutcome>;
  /** Potwierdza hold istniejącą sesją zweryfikowanego kontaktu (bez OTP/SMS). */
  confirmWithSession(
    appointmentId: string,
    body: ConfirmWithSessionInput,
  ): Promise<VerifyOtpOutcome>;
}

/** Wynik weryfikacji OTP — sterownik ekranu sukcesu po stronie klienta. */
export interface VerifyOtpOutcome {
  /**
   * true → salon potwierdza wizyty ręcznie (rezerwacja czeka na akceptację, klient dostanie SMS po
   * potwierdzeniu). false → wizyta jest od razu potwierdzona.
   */
  requiresManualConfirmation: boolean;
  /** Token sesji zweryfikowanego kontaktu wystawiony przy potwierdzeniu (do pominięcia OTP później). */
  sessionToken?: string;
  sessionExpiresAtUtc?: string;
  /**
   * Krótkożyjący token autoryzujący upload zdjęć inspiracji do tej (już potwierdzonej) wizyty.
   * Orkiestrator uploaduje nim trzymane lokalnie pliki PO potwierdzeniu (deferred-upload).
   */
  inspirationUploadToken?: string;
}
