/**
 * Demonstracyjne źródło danych — deterministyczne, bez sieci, nie tworzy realnych rezerwacji.
 * Napędza `/demo-kalendarz` przez TEN SAM `BookingFlow` co kalendarz klienta.
 *
 * Spójność: dostępność miesiąca jest liczona z tych samych slotów co wybrany dzień
 * (`generateSlots`), więc kropki na kafelkach zgadzają się z realnie pokazywanymi godzinami.
 */
import type {
  AppointmentSlotDto,
  BookingEmployeeDto,
  BookingServiceDto,
  HoldLease,
  MonthDayAvailabilityDto,
  PublicBookingHoldDto,
  PublicBookingSalonInfoDto,
  ServiceCategoryDto,
} from "../booking-openapi-client";
import type {
  BookingDataSource,
  HoldRequest,
  SalonBundle,
} from "./data-source";
import { parseISODate, startOfDay, toISODate } from "./format";

const CATEGORIES: ServiceCategoryDto[] = [
  { id: "cat-paznokcie", name: "Paznokcie", orderIndex: 0 },
  { id: "cat-brwi-rzesy", name: "Brwi i rzęsy", orderIndex: 1 },
];

const SERVICES: BookingServiceDto[] = [
  {
    id: "svc-mani-hybryda",
    categoryId: "cat-paznokcie",
    name: "Manicure hybrydowy",
    durationInMinutes: 90,
    price: { amount: 120, currency: "PLN" },
  },
  {
    id: "svc-mani-klasyczny",
    categoryId: "cat-paznokcie",
    name: "Manicure klasyczny",
    durationInMinutes: 60,
    price: { amount: 80, currency: "PLN" },
  },
  {
    id: "svc-zel",
    categoryId: "cat-paznokcie",
    name: "Przedłużanie żelem",
    durationInMinutes: 150,
    price: { amount: 200, currency: "PLN" },
  },
  {
    id: "svc-laminacja",
    categoryId: "cat-brwi-rzesy",
    name: "Laminacja brwi",
    durationInMinutes: 60,
    price: { amount: 130, currency: "PLN" },
  },
  {
    id: "svc-henna",
    categoryId: "cat-brwi-rzesy",
    name: "Henna pudrowa brwi",
    durationInMinutes: 45,
    price: { amount: 90, currency: "PLN" },
  },
  {
    id: "svc-lifting",
    categoryId: "cat-brwi-rzesy",
    name: "Lifting rzęs",
    durationInMinutes: 75,
    price: { amount: 160, currency: "PLN" },
  },
];

const EMPLOYEES: BookingEmployeeDto[] = [
  { id: "emp-ania", firstName: "Ania", lastName: "Kowalska" },
  { id: "emp-marta", firstName: "Marta", lastName: "Nowak" },
];

/**
 * Override cen per-pracownik (demo). Klucz = employeeId → { serviceId: kwota }. Marta jest droższa
 * na wybranych usługach — pokazuje, że po wyborze pracownika cena usług przelicza się per-pracownik.
 */
const EMPLOYEE_PRICE_OVERRIDES: Record<string, Record<string, number>> = {
  "emp-marta": {
    "svc-mani-hybryda": 160, // katalog 120
    "svc-zel": 240, // katalog 200
  },
};

/** Usługi z ceną zresolvowaną dla danego pracownika (override lub katalog). */
function servicesForEmployee(employeeId: string): BookingServiceDto[] {
  const overrides = EMPLOYEE_PRICE_OVERRIDES[employeeId] ?? {};
  return SERVICES.map((s) => {
    const amount = s.id ? overrides[s.id] : undefined;
    return amount != null
      ? { ...s, price: { ...s.price, amount } }
      : s;
  });
}

const SALON_INFO: PublicBookingSalonInfoDto = {
  name: "Studio Urody Ania (demo)",
  slug: "demo",
  customerVerificationChannel: 1, // e-mail
  bookingAccessPolicy: 0,
  isBookingAvailable: true,
};

const WORK_START_MIN = 9 * 60;
const WORK_END_MIN = 17 * 60;

function hashStr(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

function serviceDuration(serviceId: string): number {
  return SERVICES.find((s) => s.id === serviceId)?.durationInMinutes ?? 60;
}

function minutesToTime(total: number): string {
  const h = Math.floor(total / 60);
  const m = total % 60;
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
}

/** Deterministyczny zestaw slotów dnia. Niedziele i przeszłość puste. */
function generateSlots(
  iso: string,
  employeeId: string,
  serviceId: string,
  reference: Date = new Date(),
): AppointmentSlotDto[] {
  const d = parseISODate(iso);
  if (!d) return [];
  if (startOfDay(d) < startOfDay(reference)) return [];
  if (d.getDay() === 0) return []; // niedziela — zamknięte

  const dur = serviceDuration(serviceId);
  const step = Math.max(30, Math.ceil(dur / 30) * 30);
  const seed = hashStr(`${iso}|${employeeId}|${serviceId}`);

  const out: AppointmentSlotDto[] = [];
  let idx = 0;
  for (let t = WORK_START_MIN; t + dur <= WORK_END_MIN; t += step) {
    const bit = (seed >> (idx % 24)) & 1;
    if (bit) {
      // ~co trzeci wolny slot oznaczamy jako „polecany" (demo gap-fillingu).
      const isPreferred = ((seed >> idx) & 3) === 0;
      out.push({ slot: minutesToTime(t), isPreferred });
    }
    idx++;
  }
  return out;
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException("Aborted", "AbortError"));
      return;
    }
    const timer = setTimeout(() => {
      signal.removeEventListener("abort", onAbort);
      resolve();
    }, ms);
    function onAbort() {
      clearTimeout(timer);
      reject(new DOMException("Aborted", "AbortError"));
    }
    signal.addEventListener("abort", onAbort, { once: true });
  });
}

function randomToken(): string {
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
}

function leaseExpiringInSeconds(seconds: number): HoldLease {
  return {
    reservationToken: randomToken(),
    expiryTimeUtc: new Date(Date.now() + seconds * 1000).toISOString(),
  };
}

export interface MockDataSourceOptions {
  /** Sekundy ważności holdu zwracanego z /hold (parytet z prod HoldTtl). */
  holdSeconds?: number;
  /** Bazowe opóźnienie symulujące latencję sieci. */
  latencyMs?: number;
}

export function mockBookingDataSource(
  options: MockDataSourceOptions = {},
): BookingDataSource {
  const holdSeconds = options.holdSeconds ?? 60;
  const latency = options.latencyMs ?? 220;

  return {
    salonSlug: "demo",

    async loadSalon(signal): Promise<SalonBundle> {
      await delay(latency, signal);
      return {
        services: SERVICES,
        serviceCategories: CATEGORIES,
        salonInfo: SALON_INFO,
      };
    },

    async loadEmployees(_serviceIds, signal) {
      await delay(latency, signal);
      return EMPLOYEES;
    },

    async loadServices(employeeId, signal) {
      await delay(latency, signal);
      return servicesForEmployee(employeeId);
    },

    async loadMonthAvailability(year, month, employeeId, serviceIds, signal) {
      await delay(latency, signal);
      const primary = serviceIds[0] ?? "";
      const lastDay = new Date(year, month, 0).getDate();
      const rows: MonthDayAvailabilityDto[] = [];
      for (let day = 1; day <= lastDay; day++) {
        const dt = new Date(year, month - 1, day);
        const iso = toISODate(dt);
        rows.push({
          date: iso,
          availableCount: generateSlots(iso, employeeId, primary).length,
          // Grafik demo: pon–sob (niedziela zamknięta). Odzwierciedla „dzień roboczy" niezależnie
          // od rezerwacji, spójnie z generateSlots (niedziela → 0 slotów).
          isWorkingDay: dt.getDay() !== 0,
        });
      }
      return { isClosed: false, opensOn: undefined, days: rows };
    },

    async loadSlots(date, employeeId, serviceIds, signal) {
      await delay(latency, signal);
      return generateSlots(date, employeeId, serviceIds[0] ?? "");
    },

    async createHold(_body: HoldRequest, signal): Promise<PublicBookingHoldDto> {
      await delay(latency, signal);
      return {
        appointmentId: `demo-${randomToken()}`,
        lease: leaseExpiringInSeconds(holdSeconds),
      };
    },

    async updateHold(_appointmentId, _body, signal): Promise<HoldLease> {
      await delay(latency, signal);
      return leaseExpiringInSeconds(holdSeconds);
    },

    async attachInspiration(_appointmentId, _uploadToken, file: File, signal) {
      await delay(latency, signal);
      // Demo: nie wgrywamy nigdzie — generujemy lokalny podgląd z obiektu pliku.
      const url = URL.createObjectURL(file);
      return { url, thumbnailUrl: url, key: `demo/${file.name}` };
    },

    async requestOtp(_appointmentId, _body): Promise<void> {
      await delay(latency, new AbortController().signal);
      // Demo: „wysłanie" kodu zawsze się udaje. Akceptowany kod to dowolne ≥4 cyfry.
    },

    async verifyOtp(_appointmentId, body) {
      await delay(latency, new AbortController().signal);
      if (!/^\d{4,}$/.test(body.otp.trim())) {
        throw new Error("Nieprawidłowy kod. W demie podaj dowolne 4+ cyfry.");
      }
      // Demo: salon w trybie automatycznym (wizyta od razu potwierdzona).
      // Wystawiamy „sesję", by demo mogło pokazać pominięcie OTP przy kolejnej rezerwacji.
      return {
        requiresManualConfirmation: false,
        sessionToken: "demo-session-token",
        sessionExpiresAtUtc: new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString(),
        inspirationUploadToken: "demo-inspiration-upload-token",
      };
    },

    async confirmWithSession(_appointmentId, _body) {
      await delay(latency, new AbortController().signal);
      return {
        requiresManualConfirmation: false,
        inspirationUploadToken: "demo-inspiration-upload-token",
      };
    },
  };
}

// Eksport na potrzeby testów.
export const _mockInternals = { generateSlots, hashStr };
