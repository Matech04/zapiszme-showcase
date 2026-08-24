import { render, screen, fireEvent, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import BookingFlow from "../../BookingFlow.svelte";
import type { BookingDataSource } from "../../../../lib/booking/data-source";
import type {
  BookingEmployeeDto,
  BookingServiceDto,
  BookingMonthAvailabilityDto,
  MonthDayAvailabilityDto,
  PublicBookingSalonInfoDto,
} from "../../../../lib/booking-openapi-client";
import { toISODate, todayISO } from "../../../../lib/booking/format";

// Przełączalne warianty wyboru daty muszą działać w pełnym flow: render dnia + wybór godziny.
// Salon solo (1 pracownik, auto-wybór) → po wyborze usługi auto-pick zostaje na „dziś" (wolne).

const SLUG = "variants-salon";
const SERVICE: BookingServiceDto = {
  id: "svc-1",
  name: "Strzyżenie",
  durationInMinutes: 60,
  price: { amount: 100, currency: "PLN" },
};
const EMPLOYEE: BookingEmployeeDto = { id: "emp-1", firstName: "Anna", lastName: "Kowalska" };
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

describe("Warianty wyboru daty w pełnym flow", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
    vi.clearAllMocks();
  });

  for (const variant of ["grid", "list"] as const) {
    it(`wariant „${variant}": renderuje dzień i pozwala wybrać godzinę`, async () => {
      render(BookingFlow, {
        props: {
          dataSource: buildDataSource(),
          salonSlug: SLUG,
          datePickerVariant: variant,
        },
      });

      await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));

      // Auto-pick zostaje na „dziś" (dzień wolny) — kafelek/wiersz dnia jest obecny.
      const today = todayISO();
      await screen.findByTestId(`booking-day-${today}`, {}, { timeout: 3000 });

      // Wybór godziny działa i zakłada hold → przycisk potwierdzenia odblokowany.
      await fireEvent.click(
        await screen.findByTestId("booking-slot-10:00", {}, { timeout: 3000 }),
      );
      const primary = await screen.findByTestId("booking-footer-primary");
      await waitFor(
        () => expect((primary as HTMLButtonElement).disabled).toBe(false),
        { timeout: 3000 },
      );
    });
  }
});
