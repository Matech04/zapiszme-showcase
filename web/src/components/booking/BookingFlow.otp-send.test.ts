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

// Po wysłaniu kodu pola danych (numer/zgoda) chowają się — edytowalne jest tylko pole kodu.
// Zmiana numeru wymaga świadomego „Zmień", co wraca do trybu edycji. Inaczej można czekać na
// kod wysłany na jeden numer, a w polu mieć już inny (mismatch / zła UX).

const SLUG = "otp-send-salon";
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
    requestOtp: vi.fn().mockResolvedValue(undefined),
    verifyOtp: vi.fn(),
    confirmWithSession: vi.fn(),
  };
}

async function sendCode(): Promise<void> {
  await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
  await fireEvent.click(await screen.findByTestId("booking-slot-10:00", {}, { timeout: 3000 }));
  const primary = await screen.findByTestId("booking-footer-primary", {}, { timeout: 3000 });
  await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });
  await fireEvent.click(primary);
  await fireEvent.input(await screen.findByTestId("booking-otp-contact"), {
    target: { value: "+48501234567" },
  });
  await fireEvent.click(screen.getByTestId("booking-otp-consent"));
  await fireEvent.click(screen.getByTestId("booking-otp-send"));
}

describe("BookingFlow — przejście do wpisywania kodu po wysłaniu", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
    vi.clearAllMocks();
  });

  it("po wysłaniu chowa pole kontaktu i pokazuje pole kodu", async () => {
    render(BookingFlow, {
      props: { dataSource: buildDataSource(), salonSlug: SLUG, enableBotCheck: false },
    });

    await sendCode();

    // Pole kodu się pojawia, a pole numeru znika (nie da się go już podmienić).
    await screen.findByTestId("booking-otp-code");
    expect(screen.queryByTestId("booking-otp-contact")).toBeNull();
  });

  it("blokuje numer zagraniczny po stronie UI (nie wysyła żądania, pokazuje błąd)", async () => {
    const ds = buildDataSource();
    render(BookingFlow, {
      props: { dataSource: ds, salonSlug: SLUG, enableBotCheck: false },
    });

    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
    await fireEvent.click(await screen.findByTestId("booking-slot-10:00", {}, { timeout: 3000 }));
    const primary = await screen.findByTestId("booking-footer-primary", {}, { timeout: 3000 });
    await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });
    await fireEvent.click(primary);

    await fireEvent.input(await screen.findByTestId("booking-otp-contact"), {
      target: { value: "+49 500 600 700" }, // numer niemiecki — backend by go odrzucił
    });
    await fireEvent.click(screen.getByTestId("booking-otp-consent"));
    await fireEvent.click(screen.getByTestId("booking-otp-send"));

    // Walidacja po stronie UI — żądanie nie leci, użytkownik widzi czytelny błąd.
    expect(ds.requestOtp).not.toHaveBeenCalled();
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toMatch(/polski numer telefonu/i);
    // Nadal w trybie edycji (pole kontaktu widoczne, brak pola kodu).
    expect(screen.getByTestId("booking-otp-contact")).toBeTruthy();
    expect(screen.queryByTestId("booking-otp-code")).toBeNull();
  });

  it("przycisk Zmień wraca do edycji — pole kontaktu znów dostępne, pole kodu znika", async () => {
    render(BookingFlow, {
      props: { dataSource: buildDataSource(), salonSlug: SLUG, enableBotCheck: false },
    });

    await sendCode();
    await screen.findByTestId("booking-otp-code");

    await fireEvent.click(screen.getByTestId("booking-otp-edit-contact"));

    await screen.findByTestId("booking-otp-contact");
    expect(screen.queryByTestId("booking-otp-code")).toBeNull();
  });
});
