import { render, screen, fireEvent, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import BookingFlow from "./BookingFlow.svelte";
import type { BookingDataSource } from "../../lib/booking/data-source";
import type {
  BookingEmployeeDto,
  BookingServiceDto,
  MonthDayAvailabilityDto,
} from "../../lib/booking-openapi-client";
import { toISODate } from "../../lib/booking/format";
import { __resetBookingErrorReporting } from "../../lib/booking/error-report";

/**
 * Kalendarz nigdy nie pokazuje klientce surowego błędu.
 *
 * Zgłoszenie z produkcji: wejście na kalendarz stylistki kończyło się czerwonym „Load failed"
 * (tak Safari nazywa zerwany fetch). Dla klientki to komunikat bez treści i bez wyjścia — nie wie,
 * czy to jej internet, czy salon nie działa, i nie ma czego kliknąć. Te testy pilnują, że każda
 * awaria kończy się zrozumiałym komunikatem i przyciskiem ponowienia.
 */

const SERVICE: BookingServiceDto = {
  id: "svc-1",
  name: "Usługa testowa",
  durationInMinutes: 60,
  price: { amount: 100, currency: "PLN" },
};

const EMPLOYEE: BookingEmployeeDto = { id: "emp-1", firstName: "Anna", lastName: "Kowalska" };

/** Dokładnie to, co rzuca przeglądarka przy zerwanym połączeniu — sedno zgłoszenia. */
function networkFailure(): Error {
  return new TypeError("Load failed");
}

function monthRows(year: number, month: number): MonthDayAvailabilityDto[] {
  const daysInMonth = new Date(year, month, 0).getDate();
  const rows: MonthDayAvailabilityDto[] = [];
  for (let d = 1; d <= daysInMonth; d++) {
    rows.push({
      date: toISODate(new Date(year, month - 1, d)),
      availableCount: 3,
      isWorkingDay: true,
    });
  }
  return rows;
}

/** Źródło danych, w którym każdą metodę da się z osobna popsuć. */
function buildDataSource(overrides: Partial<BookingDataSource> = {}): BookingDataSource {
  return {
    salonSlug: "test-salon",
    async loadSalon() {
      return { services: [SERVICE], serviceCategories: [], salonInfo: null };
    },
    async loadEmployees() {
      return [EMPLOYEE];
    },
    async loadServices() {
      return [SERVICE];
    },
    async loadMonthAvailability(year, month) {
      return { isClosed: false, opensOn: undefined, days: monthRows(year, month) };
    },
    async loadSlots() {
      return [{ slot: "10:00", isPreferred: false }];
    },
    async attachInspiration() {
      throw new Error("nieużywane");
    },
    async createHold() {
      throw new Error("nieużywane");
    },
    async updateHold() {
      throw new Error("nieużywane");
    },
    async requestOtp() {
      throw new Error("nieużywane");
    },
    async verifyOtp() {
      throw new Error("nieużywane");
    },
    async confirmWithSession() {
      throw new Error("nieużywane");
    },
    ...overrides,
  };
}

function renderFlow(dataSource: BookingDataSource) {
  return render(BookingFlow, {
    props: { dataSource, salonSlug: "test-salon", enableBotCheck: false },
  });
}

describe("BookingFlow — obsługa awarii", () => {
  beforeEach(() => {
    __resetBookingErrorReporting();
    // Raporty lecą fetchem; w teście przechwytujemy je zamiast wysyłać.
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true }));
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("padnięte dane salonu → ekran błędu z ponowieniem, bez „Load failed” na ekranie", async () => {
    renderFlow(buildDataSource({ loadSalon: () => Promise.reject(networkFailure()) }));

    const screenEl = await screen.findByTestId("booking-error-screen");
    expect(screenEl.textContent).not.toContain("Load failed");
    expect(screenEl.textContent).toContain("połączenie");
    expect(screen.getByTestId("booking-error-retry")).toBeTruthy();
    // Kod zgłoszenia — po nim odnajdujemy to zdarzenie w logu, gdy klientka zadzwoni.
    expect(screenEl.textContent).toContain("Kod zgłoszenia");
  });

  it("awaria jest raportowana do backendu (inaczej nigdy byśmy się o niej nie dowiedzieli)", async () => {
    renderFlow(buildDataSource({ loadSalon: () => Promise.reject(networkFailure()) }));
    await screen.findByTestId("booking-error-screen");

    await waitFor(() => expect(fetch).toHaveBeenCalled());
    const [url, init] = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(String(url)).toContain("/api/booking-diagnostics/client-error");
    const body = JSON.parse(String((init as RequestInit).body));
    expect(body.operation).toBe("loadSalon");
    expect(body.kind).toBe("network");
    expect(body.salonSlug).toBe("test-salon");
  });

  it("404 salonu to nadal spokojny ekran „nie znaleziono”, a nie awaria", async () => {
    const notFound = Object.assign(new Error("nf"), {
      isBookingApiException: true,
      status: 404,
      response: "{}",
      headers: {},
    });
    renderFlow(buildDataSource({ loadSalon: () => Promise.reject(notFound) }));

    await screen.findByText("Nie znaleziono salonu");
    expect(screen.queryByTestId("booking-error-screen")).toBeNull();
  });

  it("padnięta lista osób nie udaje pustego salonu — pokazuje błąd i ponawia po kliknięciu", async () => {
    let attempt = 0;
    // Dwie osoby: przy jednej kreator sam ją wybiera i krok „Osoba" w ogóle nie istnieje.
    const second: BookingEmployeeDto = { id: "emp-2", firstName: "Basia", lastName: "Nowak" };
    renderFlow(
      buildDataSource({
        loadEmployees: () => {
          attempt += 1;
          return attempt === 1
            ? Promise.reject(networkFailure())
            : Promise.resolve([EMPLOYEE, second]);
        },
      }),
    );

    const error = await screen.findByTestId("booking-employees-error");
    expect(error.textContent).not.toContain("Load failed");

    await fireEvent.click(screen.getByTestId("booking-employees-error-retry"));

    // Po udanym ponowieniu wraca normalny wybór osoby — bez przeładowania strony.
    await screen.findByTestId(`booking-employee-${EMPLOYEE.id}`);
    expect(screen.queryByTestId("booking-employees-error")).toBeNull();
  });

  it("padnięte godziny dnia → błąd sekcji z ponowieniem, wybory klientki zostają", async () => {
    let attempt = 0;
    renderFlow(
      buildDataSource({
        loadSlots: () => {
          attempt += 1;
          return attempt === 1
            ? Promise.reject(networkFailure())
            : Promise.resolve([{ slot: "10:00", isPreferred: false }]);
        },
      }),
    );

    // Solo-pracownik → auto-wybór; wybór usługi odblokowuje kalendarz.
    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));

    const error = await screen.findByTestId("booking-slots-error");
    expect(error.textContent).not.toContain("Load failed");

    await fireEvent.click(screen.getByTestId("booking-slots-error-retry"));

    await screen.findByTestId("booking-slot-10:00");
    // Usługa nadal wybrana — ponowienie sekcji nie kasuje postępu.
    expect(screen.queryByTestId("booking-slots-error")).toBeNull();
  });

  it("padnięta dostępność miesiąca → komunikat z ponowieniem zamiast cichego pustego kalendarza", async () => {
    let attempt = 0;
    renderFlow(
      buildDataSource({
        loadMonthAvailability: (year, month) => {
          attempt += 1;
          return attempt === 1
            ? Promise.reject(networkFailure())
            : Promise.resolve({ isClosed: false, opensOn: undefined, days: monthRows(year, month) });
        },
      }),
    );

    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));

    await screen.findByTestId("booking-month-error");
    await fireEvent.click(screen.getByTestId("booking-month-error-retry"));

    await waitFor(() => expect(screen.queryByTestId("booking-month-error")).toBeNull());
  });
});
