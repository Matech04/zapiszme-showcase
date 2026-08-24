import { render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import BookingFlow from "./BookingFlow.svelte";
import type { BookingDataSource } from "../../lib/booking/data-source";
import type {
  BookingServiceDto,
  PublicBookingSalonInfoDto,
} from "../../lib/booking-openapi-client";

// Wstrzymanie rezerwacji: gdy salon ma isBookingPaused=true, kalendarz nie ładuje kroku wyboru
// usługi, tylko pokazuje ekran „Rezerwacje chwilowo wstrzymane" (z opcjonalnym komunikatem salonu).

const SLUG = "paused-salon";
const SERVICE: BookingServiceDto = {
  id: "svc-1",
  name: "Strzyżenie",
  durationInMinutes: 60,
  price: { amount: 100, currency: "PLN" },
};

function buildDataSource(salonInfo: PublicBookingSalonInfoDto): BookingDataSource {
  return {
    salonSlug: SLUG,
    async loadSalon() {
      return { services: [SERVICE], serviceCategories: [], salonInfo };
    },
    async loadEmployees() {
      return [];
    },
    async loadServices() {
      return [SERVICE];
    },
    async loadMonthAvailability() {
      return { isClosed: false, opensOn: undefined, days: [] };
    },
    async loadSlots() {
      return [];
    },
    async createHold() {
      throw new Error("nie powinno być wołane gdy rezerwacje wstrzymane");
    },
    async updateHold() {
      throw new Error("nie powinno być wołane gdy rezerwacje wstrzymane");
    },
    attachInspiration: vi.fn(),
    requestOtp: vi.fn(),
    verifyOtp: vi.fn(),
    confirmWithSession: vi.fn(),
  };
}

describe("BookingFlow — wstrzymanie rezerwacji", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
    vi.clearAllMocks();
  });

  it("pokazuje ekran wstrzymania z komunikatem salonu i nie pokazuje wyboru usługi", async () => {
    const salonInfo = {
      name: "Salon Testowy",
      slug: SLUG,
      customerVerificationChannel: 0,
      isBookingAvailable: false,
      requireCustomerName: false,
      collectInstagramHandle: false,
      isBookingPaused: true,
      bookingPauseMessage: "Zmieniamy grafik — zadzwoń, aby umówić wizytę.",
    } as unknown as PublicBookingSalonInfoDto;

    render(BookingFlow, {
      props: { dataSource: buildDataSource(salonInfo), salonSlug: SLUG, enableBotCheck: false },
    });

    await waitFor(() => expect(screen.getByText("Rezerwacje chwilowo wstrzymane")).toBeTruthy());
    expect(screen.getByText("Zmieniamy grafik — zadzwoń, aby umówić wizytę.")).toBeTruthy();
    // Krok wyboru usługi nie jest renderowany.
    expect(screen.queryByTestId("booking-service-svc-1")).toBeNull();
  });

  it("bez komunikatu salonu pokazuje domyślny tekst wstrzymania", async () => {
    const salonInfo = {
      name: "Salon Testowy",
      slug: SLUG,
      customerVerificationChannel: 0,
      isBookingAvailable: false,
      requireCustomerName: false,
      collectInstagramHandle: false,
      isBookingPaused: true,
      bookingPauseMessage: null,
    } as unknown as PublicBookingSalonInfoDto;

    render(BookingFlow, {
      props: { dataSource: buildDataSource(salonInfo), salonSlug: SLUG, enableBotCheck: false },
    });

    await waitFor(() => expect(screen.getByText("Rezerwacje chwilowo wstrzymane")).toBeTruthy());
    expect(
      screen.getByText(/Rezerwacje online są chwilowo wstrzymane/i),
    ).toBeTruthy();
  });

  it("globalny tryb serwisowy platformy ma priorytet nad wstrzymaniem salonu", async () => {
    const salonInfo = {
      name: "Salon Testowy",
      slug: SLUG,
      customerVerificationChannel: 0,
      isBookingAvailable: false,
      requireCustomerName: false,
      collectInstagramHandle: false,
      isBookingPaused: true,
      bookingPauseMessage: "komunikat salonu",
      isPlatformMaintenance: true,
      platformMaintenanceMessage: "Trwa aktualizacja platformy.",
    } as unknown as PublicBookingSalonInfoDto;

    render(BookingFlow, {
      props: { dataSource: buildDataSource(salonInfo), salonSlug: SLUG, enableBotCheck: false },
    });

    await waitFor(() => expect(screen.getByText("Trwają prace serwisowe")).toBeTruthy());
    expect(screen.getByText("Trwa aktualizacja platformy.")).toBeTruthy();
    // Ekran wstrzymania salonu NIE jest pokazywany (maintenance wygrywa).
    expect(screen.queryByText("Rezerwacje chwilowo wstrzymane")).toBeNull();
    expect(screen.queryByTestId("booking-service-svc-1")).toBeNull();
  });
});
