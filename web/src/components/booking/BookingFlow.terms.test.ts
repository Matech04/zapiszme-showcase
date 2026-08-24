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

// Regulamin salonu: gdy tenant ma własną treść regulaminu, OtpDialog pokazuje ją (rozwijaną)
// zamiast sztywnego linku do /regulamin, a checkbox zgody nadal blokuje wysłanie kodu.

const SLUG = "terms-salon";
const TERMS = "Zadatek 50% przy rezerwacji. Odwołanie do 24h przed wizytą.";
const SERVICE: BookingServiceDto = {
  id: "svc-1",
  name: "Strzyżenie",
  durationInMinutes: 60,
  price: { amount: 100, currency: "PLN" },
};
const EMPLOYEE: BookingEmployeeDto = { id: "emp-1", firstName: "Anna", lastName: "Kowalska" };

function salonInfo(terms: string | null): PublicBookingSalonInfoDto {
  return {
    name: "Salon",
    slug: SLUG,
    customerVerificationChannel: 0, // telefon
    isBookingAvailable: true,
    requireCustomerName: false,
    collectInstagramHandle: false,
    termsOfService: terms ?? undefined,
  } as unknown as PublicBookingSalonInfoDto;
}

function buildDataSource(terms: string | null): BookingDataSource {
  return {
    salonSlug: SLUG,
    async loadSalon() {
      return { services: [SERVICE], serviceCategories: [], salonInfo: salonInfo(terms) };
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

async function openOtpDialog(): Promise<void> {
  await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
  await fireEvent.click(await screen.findByTestId("booking-slot-10:00", {}, { timeout: 3000 }));
  const primary = await screen.findByTestId("booking-footer-primary", {}, { timeout: 3000 });
  await waitFor(() => expect((primary as HTMLButtonElement).disabled).toBe(false), { timeout: 3000 });
  await fireEvent.click(primary);
  await screen.findByTestId("booking-otp-contact");
}

describe("BookingFlow — regulamin salonu w OtpDialog", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
    vi.clearAllMocks();
  });

  it("pokazuje treść regulaminu salonu (po rozwinięciu) gdy tenant ją ma", async () => {
    render(BookingFlow, {
      props: { dataSource: buildDataSource(TERMS), salonSlug: SLUG, enableBotCheck: false },
    });

    await openOtpDialog();

    // Toggle regulaminu jest widoczny; treść po rozwinięciu.
    const toggle = await screen.findByTestId("booking-otp-terms-toggle");
    expect(screen.queryByTestId("booking-otp-terms-content")).toBeNull();
    await fireEvent.click(toggle);
    const content = await screen.findByTestId("booking-otp-terms-content");
    expect(content.textContent).toContain("Zadatek 50%");
  });

  it("zgoda nadal blokuje wysłanie kodu, mimo własnego regulaminu", async () => {
    const ds = buildDataSource(TERMS);
    render(BookingFlow, {
      props: { dataSource: ds, salonSlug: SLUG, enableBotCheck: false },
    });

    await openOtpDialog();
    await fireEvent.input(screen.getByTestId("booking-otp-contact"), {
      target: { value: "+48501234567" },
    });

    // Przed zaznaczeniem zgody przycisk wysłania jest zablokowany.
    const send = screen.getByTestId("booking-otp-send") as HTMLButtonElement;
    expect(send.disabled).toBe(true);

    await fireEvent.click(screen.getByTestId("booking-otp-consent"));
    expect(send.disabled).toBe(false);
  });

  it("bez własnego regulaminu pokazuje domyślny link do /regulamin (brak toggle)", async () => {
    render(BookingFlow, {
      props: { dataSource: buildDataSource(null), salonSlug: SLUG, enableBotCheck: false },
    });

    await openOtpDialog();

    expect(screen.queryByTestId("booking-otp-terms-toggle")).toBeNull();
    const link = document.querySelector('a[href="/regulamin"]');
    expect(link).not.toBeNull();
  });
});
