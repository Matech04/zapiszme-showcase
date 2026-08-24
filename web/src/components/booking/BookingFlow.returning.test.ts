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

// „Umawiam ponownie": salon wymaga imienia, ale stały klient zaznacza opcję → pola imienia znikają,
// a request-otp leci tylko z kontaktem + flagą isReturningCustomer (bez imienia/nazwiska).

const SLUG = "returning-salon";
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
  customerVerificationChannel: 0, // telefon
  isBookingAvailable: true,
  requireCustomerName: true, // salon WYMAGA imienia
  collectInstagramHandle: false,
} as unknown as PublicBookingSalonInfoDto;

function buildDataSource(requestOtp: BookingDataSource["requestOtp"]): BookingDataSource {
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
    requestOtp,
    verifyOtp: vi.fn(),
    confirmWithSession: vi.fn(),
  };
}

async function openOtpDialog(): Promise<void> {
  await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
  await fireEvent.click(await screen.findByTestId("booking-slot-10:00", {}, { timeout: 3000 }));
  const primary = await screen.findByTestId("booking-footer-primary", {}, { timeout: 3000 });
  await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });
  await fireEvent.click(primary);
  await screen.findByTestId("booking-otp-contact");
}

describe("BookingFlow — umawiam ponownie (stały klient)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
    vi.clearAllMocks();
  });

  it("zaznaczenie chowa pola imienia i wysyła request-otp bez imienia + z flagą returning", async () => {
    const requestOtp = vi.fn().mockResolvedValue(undefined);
    render(BookingFlow, {
      props: { dataSource: buildDataSource(requestOtp), salonSlug: SLUG, enableBotCheck: false },
    });

    await openOtpDialog();

    // Salon wymaga imienia → pola imienia widoczne, checkbox „umawiam ponownie" dostępny.
    expect(screen.getByTestId("booking-otp-first-name")).toBeTruthy();
    await fireEvent.click(screen.getByTestId("booking-otp-returning"));
    // Po zaznaczeniu pola imienia znikają.
    expect(screen.queryByTestId("booking-otp-first-name")).toBeNull();

    await fireEvent.input(screen.getByTestId("booking-otp-contact"), {
      target: { value: "+48501234567" },
    });
    await fireEvent.click(screen.getByTestId("booking-otp-consent"));
    await fireEvent.click(screen.getByTestId("booking-otp-send"));

    await waitFor(() => expect(requestOtp).toHaveBeenCalled());
    const [, body] = requestOtp.mock.calls[0];
    expect(body.isReturningCustomer).toBe(true);
    expect(body.firstName).toBeUndefined();
    expect(body.lastName).toBeUndefined();
    expect(body.phoneNumber).toBe("+48501234567");
  });

  it("zaznaczenie chowa też pole Instagrama (konto już istnieje — tylko numer)", async () => {
    const requestOtp = vi.fn().mockResolvedValue(undefined);
    const ds = buildDataSource(requestOtp);
    // Salon NIE wymaga imienia, ale zbiera Instagram → opcja „umawiam ponownie" musi i tak być
    // dostępna, a jej zaznaczenie ma schować pole Instagrama (zostaje tylko kontakt).
    const igSalon = {
      ...SALON_INFO,
      requireCustomerName: false,
      collectInstagramHandle: true,
    } as PublicBookingSalonInfoDto;
    ds.loadSalon = async () => ({
      services: [SERVICE],
      serviceCategories: [],
      salonInfo: igSalon,
    });
    render(BookingFlow, {
      props: { dataSource: ds, salonSlug: SLUG, enableBotCheck: false },
    });

    await openOtpDialog();

    // Salon zbiera Instagram → pole widoczne dla nowego klienta, checkbox „ponownie" dostępny.
    expect(screen.getByTestId("booking-otp-instagram")).toBeTruthy();
    expect(screen.getByTestId("booking-otp-returning")).toBeTruthy();
    // „Umawiam ponownie" → pole Instagrama znika (konto istnieje z poprzedniej rezerwacji).
    await fireEvent.click(screen.getByTestId("booking-otp-returning"));
    expect(screen.queryByTestId("booking-otp-instagram")).toBeNull();
    // Zostaje wyłącznie pole kontaktu.
    expect(screen.getByTestId("booking-otp-contact")).toBeTruthy();
  });

  it("bez zaznaczenia wymaga imienia (request-otp nie leci, pokazuje błąd)", async () => {
    const requestOtp = vi.fn().mockResolvedValue(undefined);
    render(BookingFlow, {
      props: { dataSource: buildDataSource(requestOtp), salonSlug: SLUG, enableBotCheck: false },
    });

    await openOtpDialog();

    await fireEvent.input(screen.getByTestId("booking-otp-contact"), {
      target: { value: "+48501234567" },
    });
    await fireEvent.click(screen.getByTestId("booking-otp-consent"));
    await fireEvent.click(screen.getByTestId("booking-otp-send"));

    expect(requestOtp).not.toHaveBeenCalled();
  });
});
