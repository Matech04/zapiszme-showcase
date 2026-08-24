import { render, screen, fireEvent, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import BookingFlow from "./BookingFlow.svelte";
import type { BookingDataSource } from "../../lib/booking/data-source";
import type {
  BookingEmployeeDto,
  BookingServiceDto,
  BookingMonthAvailabilityDto,
  MonthDayAvailabilityDto,
  PublicBookingSalonInfoDto,
} from "../../lib/booking-openapi-client";
import { toISODate } from "../../lib/booking/format";

// Kreator (layout="wizard"): jeden wybór na ekran, „Dalej" twardo zablokowane bez wyboru.
// Dwóch pracowników → ekran „Osoba" jest PIERWSZY (employee → service → datetime → summary),
// bo usługi (cena/czas) zależą od wybranego pracownika.

const SLUG = "wizard-salon";
const SERVICE: BookingServiceDto = {
  id: "svc-1",
  name: "Strzyżenie",
  durationInMinutes: 60,
  price: { amount: 100, currency: "PLN" },
};
const EMPLOYEES: BookingEmployeeDto[] = [
  { id: "emp-1", firstName: "Anna", lastName: "Kowalska" },
  { id: "emp-2", firstName: "Beata", lastName: "Nowak" },
];

const SALON_INFO = {
  name: "Salon",
  slug: SLUG,
  customerVerificationChannel: 0,
  isBookingAvailable: true,
  requireCustomerName: false,
  collectInstagramHandle: false,
} as unknown as PublicBookingSalonInfoDto;

function buildDataSource(): BookingDataSource {
  return {
    salonSlug: SLUG,
    async loadSalon() {
      return { services: [SERVICE], serviceCategories: [], salonInfo: SALON_INFO };
    },
    async loadEmployees() {
      return EMPLOYEES;
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
      return [{ slot: "10:00", isPreferred: false }];
    },
    async createHold() {
      return {
        appointmentId: "appt-1",
        lease: {
          reservationToken: "res-tok",
          expiryTimeUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
        },
      };
    },
    async updateHold() {
      return {
        reservationToken: "res-tok",
        expiryTimeUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
      };
    },
    attachInspiration: vi.fn(),
    requestOtp: vi.fn(),
    verifyOtp: vi.fn(),
    confirmWithSession: vi.fn(),
  };
}

describe("BookingFlow — kreator (wizard)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
    vi.clearAllMocks();
  });

  it("prowadzi krok-po-kroku i blokuje Dalej bez wyboru na każdym ekranie", async () => {
    render(BookingFlow, {
      props: { dataSource: buildDataSource(), salonSlug: SLUG, layout: "wizard" as const },
    });

    // Ekran 1 — Osoba: „Dalej" zablokowane dopóki nie wybrano pracownika.
    const next = await screen.findByTestId("booking-wizard-next");
    await waitFor(() => expect((next as HTMLButtonElement).disabled).toBe(true));
    await fireEvent.click(await screen.findByTestId("booking-employee-emp-1"));
    await waitFor(() => expect((next as HTMLButtonElement).disabled).toBe(false));
    await fireEvent.click(next);

    // Ekran 2 — Usługa: znów zablokowane, aż do wyboru usługi.
    const next2 = await screen.findByTestId("booking-wizard-next");
    await waitFor(() => expect((next2 as HTMLButtonElement).disabled).toBe(true));
    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
    await waitFor(() => expect((next2 as HTMLButtonElement).disabled).toBe(false));
    await fireEvent.click(next2);

    // Ekran 3 — Termin: zablokowane bez wyboru godziny; po wyborze + holdzie odblokowuje.
    const next3 = await screen.findByTestId("booking-wizard-next");
    await waitFor(() => expect((next3 as HTMLButtonElement).disabled).toBe(true));
    await fireEvent.click(
      await screen.findByTestId("booking-slot-10:00", {}, { timeout: 3000 }),
    );
    await waitFor(() => expect((next3 as HTMLButtonElement).disabled).toBe(false), {
      timeout: 3000,
    });
    await fireEvent.click(next3);

    // Ekran 4 — Podsumowanie: pojawia się przycisk potwierdzenia, znika pasek „Dalej".
    const primary = await screen.findByTestId("booking-footer-primary");
    expect(primary).toBeTruthy();
    expect(screen.queryByTestId("booking-wizard-next")).toBeNull();
  });
});
