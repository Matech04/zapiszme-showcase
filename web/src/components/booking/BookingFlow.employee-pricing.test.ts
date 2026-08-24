import { render, screen, fireEvent, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it } from "vitest";
import BookingFlow from "./BookingFlow.svelte";
import type { BookingDataSource } from "../../lib/booking/data-source";
import type {
  BookingEmployeeDto,
  BookingServiceDto,
  PublicBookingSalonInfoDto,
} from "../../lib/booking-openapi-client";

// Sedno poprawki: pracownika wybieramy PRZED usługą, a cena usługi jest zresolvowana per-pracownik.
// Pani Magda ma override 220 zł na usłudze, której domyślna (katalogowa) cena to 180 zł — po jej
// wyborze karta usługi musi pokazać 220 zł, nie 180 zł.

const SLUG = "pricing-salon";
const CATALOG_PRICE = 180;
const MAGDA_PRICE = 220;

const EMPLOYEES: BookingEmployeeDto[] = [
  { id: "emp-ania", firstName: "Ania", lastName: "Kowalska" },
  { id: "emp-magda", firstName: "Magda", lastName: "Nowak" },
];

const SALON_INFO = {
  name: "Salon",
  slug: SLUG,
  customerVerificationChannel: 0,
  isBookingAvailable: true,
  requireCustomerName: false,
  collectInstagramHandle: false,
} as unknown as PublicBookingSalonInfoDto;

function serviceForEmployee(employeeId: string): BookingServiceDto {
  const amount = employeeId === "emp-magda" ? MAGDA_PRICE : CATALOG_PRICE;
  return {
    id: "svc-1",
    name: "Manicure hybrydowy",
    durationInMinutes: 60,
    price: { amount, currency: "PLN" },
  } as unknown as BookingServiceDto;
}

function buildDataSource(): BookingDataSource {
  return {
    salonSlug: SLUG,
    async loadSalon() {
      // Katalog nie jest już źródłem cen w pickerze (usługi biorą się z loadServices per-pracownik).
      return { services: [], serviceCategories: [], salonInfo: SALON_INFO };
    },
    async loadEmployees() {
      return EMPLOYEES;
    },
    async loadServices(employeeId) {
      return [serviceForEmployee(employeeId)];
    },
    async loadMonthAvailability() {
      return { isClosed: false, opensOn: undefined, days: [] };
    },
    async loadSlots() {
      return [];
    },
    async createHold() {
      return { appointmentId: "appt-1", lease: { reservationToken: "t", expiryTimeUtc: new Date(Date.now() + 60000).toISOString() } };
    },
    async updateHold() {
      return { reservationToken: "t", expiryTimeUtc: new Date(Date.now() + 60000).toISOString() };
    },
    async attachInspiration() {
      return { url: "", thumbnailUrl: "", key: "" };
    },
    async requestOtp() {},
    async verifyOtp() {
      return { requiresManualConfirmation: false };
    },
    async confirmWithSession() {
      return { requiresManualConfirmation: false };
    },
  };
}

describe("BookingFlow — cena usługi zależna od pracownika", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
  });

  it("dopóki nie wybrano pracownika, usługi są ukryte za komunikatem", async () => {
    render(BookingFlow, { props: { dataSource: buildDataSource(), salonSlug: SLUG } });

    // Dwóch pracowników → brak auto-wyboru; picker usług prosi o wybór pracownika.
    expect(await screen.findByTestId("booking-service-pick-employee-first")).toBeTruthy();
    expect(screen.queryByTestId("booking-service-svc-1")).toBeNull();
  });

  it("po wyborze pracownika z override pokazuje jego cenę (220 zł), nie katalogową (180 zł)", async () => {
    render(BookingFlow, { props: { dataSource: buildDataSource(), salonSlug: SLUG } });

    await fireEvent.click(await screen.findByTestId("booking-employee-emp-magda"));

    const card = await screen.findByTestId("booking-service-svc-1");
    await waitFor(() => expect(card.textContent).toMatch(/220\s*zł/));
    expect(card.textContent).not.toMatch(/180\s*zł/);
  });

  it("zmiana pracownika przelicza cenę usługi", async () => {
    render(BookingFlow, { props: { dataSource: buildDataSource(), salonSlug: SLUG } });

    // Ania → cena katalogowa 180 zł.
    await fireEvent.click(await screen.findByTestId("booking-employee-emp-ania"));
    const aniaCard = await screen.findByTestId("booking-service-svc-1");
    await waitFor(() => expect(aniaCard.textContent).toMatch(/180\s*zł/));

    // Przełączenie na Magdę → 220 zł.
    await fireEvent.click(await screen.findByTestId("booking-employee-emp-magda"));
    await waitFor(() =>
      expect(screen.getByTestId("booking-service-svc-1").textContent).toMatch(/220\s*zł/),
    );
  });
});
