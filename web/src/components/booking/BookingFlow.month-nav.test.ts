import { render, screen, fireEvent, waitFor } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import BookingFlow from "./BookingFlow.svelte";
import type { BookingDataSource } from "../../lib/booking/data-source";
import type {
  BookingEmployeeDto,
  BookingServiceDto,
  BookingMonthAvailabilityDto,
  MonthDayAvailabilityDto,
  ServiceCategoryDto,
} from "../../lib/booking-openapi-client";
import { MONTHS_PL, toISODate } from "../../lib/booking/format";

// Regresja: nawigacja miesiącami w pasku terminów (DateStrip / BookingFlow).
//
// Bug produkcyjny: dostępność tylko w jednym miesiącu (np. lipiec). Auto-pick przeskakiwał
// „w przód" przez puste miesiące także po RĘCZNYM kliknięciu strzałek — więc „›" skakało o dwa
// miesiące (lipiec → wrzesień, z pominięciem sierpnia), a „‹" odbijało z powrotem do przodu,
// przez co powrót do lipca wymagał kilku kliknięć.
//
// Test jest niezależny od kalendarza: miesiące liczymy względem „teraz". Dostępność wstawiamy
// tylko w pierwszym przyszłym miesiącu (offset +1) — to odpowiednik „lipca" z raportu.

const SERVICE: BookingServiceDto = {
  id: "svc-1",
  name: "Usługa testowa",
  durationInMinutes: 60,
  price: { amount: 100, currency: "PLN" },
};

const EMPLOYEE: BookingEmployeeDto = { id: "emp-1", firstName: "Anna", lastName: "Testowa" };

const CATEGORIES: ServiceCategoryDto[] = [];

const now = new Date();
/** Pierwszy dzień miesiąca przesuniętego o `offset` względem bieżącego (z poprawnym rolowaniem roku). */
function monthStart(offset: number): Date {
  return new Date(now.getFullYear(), now.getMonth() + offset, 1);
}
/** Tytuł miesiąca tak, jak renderuje go DateStrip (`browseMonthTitle`). */
function titleFor(offset: number): string {
  const d = monthStart(offset);
  return `${MONTHS_PL[d.getMonth()]} ${d.getFullYear()}`;
}

// Miesiąc z jedynymi wolnymi terminami = bieżący + 1.
const AVAIL = monthStart(1);
const AVAIL_YEAR = AVAIL.getFullYear();
const AVAIL_MONTH = AVAIL.getMonth() + 1;

function buildDataSource(): BookingDataSource {
  return {
    salonSlug: "test-salon",
    async loadSalon() {
      return { services: [SERVICE], serviceCategories: CATEGORIES, salonInfo: null };
    },
    async loadEmployees() {
      return [EMPLOYEE];
    },
    async loadServices() {
      return [SERVICE];
    },
    async loadMonthAvailability(year, month): Promise<BookingMonthAvailabilityDto> {
      // Każdy miesiąc zwraca wiersz na każdy dzień (size > 0 — jak realny backend z godzinami pracy).
      // availableCount > 0 TYLKO w miesiącu z terminami; reszta = 0 → status „none".
      const daysInMonth = new Date(year, month, 0).getDate();
      const hasAvailability = year === AVAIL_YEAR && month === AVAIL_MONTH;
      const rows: MonthDayAvailabilityDto[] = [];
      for (let d = 1; d <= daysInMonth; d++) {
        rows.push({
          date: toISODate(new Date(year, month - 1, d)),
          availableCount: hasAvailability ? 3 : 0,
          // Pracownik MA grafik codziennie (dzień roboczy) — bieżący miesiąc bywa tylko zajęty.
          // To zachowuje auto-skok „w przód" do miesiąca z wolnymi terminami.
          isWorkingDay: true,
        });
      }
      return { isClosed: false, opensOn: undefined, days: rows };
    },
    async loadSlots() {
      return [];
    },
    async attachInspiration() {
      throw new Error("nieużywane w teście nawigacji");
    },
    async createHold() {
      throw new Error("nieużywane w teście nawigacji");
    },
    async updateHold() {
      throw new Error("nieużywane w teście nawigacji");
    },
    async requestOtp() {
      throw new Error("nieużywane w teście nawigacji");
    },
    async verifyOtp() {
      throw new Error("nieużywane w teście nawigacji");
    },
    async confirmWithSession() {
      throw new Error("nieużywane w teście nawigacji");
    },
  };
}

describe("BookingFlow — nawigacja miesiącami", () => {
  it("ręczne strzałki przesuwają o dokładnie jeden miesiąc (auto-pick nie skacze po pustych)", async () => {
    render(BookingFlow, {
      props: { dataSource: buildDataSource(), salonSlug: "test-salon", enableBotCheck: false },
    });

    // Wybór usługi → solo-pracownik auto-wybrany → odblokowuje pasek terminów.
    const serviceBtn = await screen.findByTestId("booking-service-svc-1");
    await fireEvent.click(serviceBtn);

    // Auto-pick znajduje najbliższy wolny miesiąc (bieżący jest pusty) — landuje na +1.
    await screen.findByText(titleFor(1));

    // Klik „›" raz → musi wylądować na +2, a NIE przeskoczyć na +3.
    await fireEvent.click(screen.getByLabelText("Następny miesiąc"));
    await waitFor(() => {
      expect(screen.getByText(titleFor(2))).toBeTruthy();
    });
    expect(screen.queryByText(titleFor(3))).toBeNull();

    // Klik „‹" raz → wraca na +1 za jednym kliknięciem (nie odbija do przodu).
    await fireEvent.click(screen.getByLabelText("Poprzedni miesiąc"));
    await waitFor(() => {
      expect(screen.getByText(titleFor(1))).toBeTruthy();
    });
    expect(screen.queryByText(titleFor(2))).toBeNull();
  });
});
