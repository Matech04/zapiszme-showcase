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

// Regresja: pracownik BEZ ustalonego grafiku.
//
// Bug produkcyjny: przy wielu pracownikach klik w pracownika bez grafiku przewijał kalendarz
// kilka miesięcy w przód (do limitu MAX_MONTHS_AHEAD, np. październik) z „brak terminów", a po
// powrocie do pracownika Z grafikiem kalendarz TKWIŁ na tym doskoczonym miesiącu zamiast wrócić
// do najbliższego terminu.
//
// Fix: backend oznacza dni robocze (`isWorkingDay`); front skacze „w przód" tylko gdy miesiąc ma
// dzień roboczy (pracownik z grafikiem, ale zajęty). Pracownik bez grafiku → zostajemy w bieżącym
// miesiącu i pokazujemy „Brak wolnych terminów". Zmiana pracownika resetuje podgląd do „teraz".

const SERVICE: BookingServiceDto = {
  id: "svc-1",
  name: "Usługa testowa",
  durationInMinutes: 60,
  price: { amount: 100, currency: "PLN" },
};

const EMP_SCHEDULED: BookingEmployeeDto = { id: "emp-plan", firstName: "Anna", lastName: "Zgrafikiem" };
const EMP_NO_SCHEDULE: BookingEmployeeDto = { id: "emp-brak", firstName: "Bez", lastName: "Grafiku" };
// Grafik JEST, ale w żadnym miesiącu nie ma wolnych miejsc — drugi wariant komunikatu.
const EMP_BOOKED: BookingEmployeeDto = { id: "emp-pelny", firstName: "Zajęta", lastName: "Całkiem" };

const CATEGORIES: ServiceCategoryDto[] = [];

const now = new Date();
function monthStart(offset: number): Date {
  return new Date(now.getFullYear(), now.getMonth() + offset, 1);
}
function titleFor(offset: number): string {
  const d = monthStart(offset);
  return `${MONTHS_PL[d.getMonth()]} ${d.getFullYear()}`;
}

// Pracownik z grafikiem ma wolne terminy DOPIERO w miesiącu bieżący + 2 — wcześniejsze zajęte.
const SCHEDULED_AVAIL = monthStart(2);
const SCHEDULED_AVAIL_YEAR = SCHEDULED_AVAIL.getFullYear();
const SCHEDULED_AVAIL_MONTH = SCHEDULED_AVAIL.getMonth() + 1;

// Dwa RÓŻNE komunikaty — wcześniej oba scenariusze dawały ten sam tekst i klientka nie wiedziała,
// czy przeglądanie dalszych miesięcy ma jakikolwiek sens.
const NO_SCHEDULE_TEXT =
  "Dla tego miesiąca nie przygotowano jeszcze grafiku — terminy pojawią się, gdy salon go uzupełni.";
const FULLY_BOOKED_TEXT = "Wszystkie terminy w tym miesiącu są już zajęte — sprawdź kolejny.";
// Na ostatnim miesiącu okna rezerwacji „sprawdź kolejny" jest radą nie do wykonania —
// przycisk „następny miesiąc" jest wtedy wyłączony.
const LAST_MONTH_TEXT =
  "Wszystkie terminy są już zajęte — to najdalszy miesiąc otwarty na rezerwacje. Zajrzyj tu później, gdy zwolnią się miejsca.";

function monthRows(
  year: number,
  month: number,
  opts: { isWorkingDay: boolean; availableWhen?: (y: number, m: number) => boolean },
): MonthDayAvailabilityDto[] {
  const daysInMonth = new Date(year, month, 0).getDate();
  const hasAvailability = opts.availableWhen?.(year, month) ?? false;
  const rows: MonthDayAvailabilityDto[] = [];
  for (let d = 1; d <= daysInMonth; d++) {
    rows.push({
      date: toISODate(new Date(year, month - 1, d)),
      availableCount: hasAvailability ? 3 : 0,
      isWorkingDay: opts.isWorkingDay,
    });
  }
  return rows;
}

function buildDataSource(employees: BookingEmployeeDto[]): BookingDataSource {
  return {
    salonSlug: "test-salon",
    async loadSalon() {
      return { services: [SERVICE], serviceCategories: CATEGORIES, salonInfo: null };
    },
    async loadEmployees() {
      return employees;
    },
    async loadServices() {
      return [SERVICE];
    },
    async loadMonthAvailability(year, month, employeeId): Promise<BookingMonthAvailabilityDto> {
      if (employeeId === EMP_NO_SCHEDULE.id) {
        // Brak grafiku: backend zwraca wiersz na każdy dzień (size > 0), ale availableCount=0
        // ORAZ isWorkingDay=false dla wszystkich — to ma NIE wyzwalać skoku w przód.
        return { isClosed: false, opensOn: undefined, days: monthRows(year, month, { isWorkingDay: false }) };
      }
      if (employeeId === EMP_BOOKED.id) {
        // Dni robocze są, ale zero wolnych miejsc w KAŻDYM miesiącu.
        return { isClosed: false, opensOn: undefined, days: monthRows(year, month, { isWorkingDay: true }) };
      }
      // Pracownik z grafikiem: dni robocze codziennie, wolne terminy tylko w +2.
      return { isClosed: false, opensOn: undefined, days: monthRows(year, month, {
        isWorkingDay: true,
        availableWhen: (y, m) => y === SCHEDULED_AVAIL_YEAR && m === SCHEDULED_AVAIL_MONTH,
      }) };
    },
    async loadSlots() {
      return [];
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
  };
}

describe("BookingFlow — pracownik bez grafiku", () => {
  it("nie przeskakuje w przód — zostaje w bieżącym miesiącu i pokazuje „brak terminów”", async () => {
    render(BookingFlow, {
      props: {
        dataSource: buildDataSource([EMP_NO_SCHEDULE]),
        salonSlug: "test-salon",
        enableBotCheck: false,
      },
    });

    // Solo-pracownik → auto-wybór; wybór usługi odblokowuje kalendarz.
    const serviceBtn = await screen.findByTestId("booking-service-svc-1");
    await fireEvent.click(serviceBtn);

    // Komunikat o braku terminów w bieżącym miesiącu (nie skoczyliśmy nigdzie).
    await screen.findByText(NO_SCHEDULE_TEXT);

    // Zostaliśmy na bieżącym miesiącu — NIE doskoczyliśmy do limitu (+3).
    expect(screen.getByText(titleFor(0))).toBeTruthy();
    expect(screen.queryByText(titleFor(3))).toBeNull();
  });

  it("po powrocie z pracownika bez grafiku do pracownika z grafikiem kalendarz wraca do najbliższego terminu", async () => {
    render(BookingFlow, {
      props: {
        dataSource: buildDataSource([EMP_SCHEDULED, EMP_NO_SCHEDULE]),
        salonSlug: "test-salon",
        enableBotCheck: false,
      },
    });

    // 1) Pracownik z grafikiem + usługa → auto-skok do miesiąca z terminami (+2).
    await fireEvent.click(await screen.findByTestId(`booking-employee-${EMP_SCHEDULED.id}`));
    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
    await screen.findByText(titleFor(2));

    // 2) Przełączenie na pracownika bez grafiku RESETUJE podgląd do bieżącego miesiąca
    //    (nie dziedziczymy „doskoczonego” +2) i wybór usługi zaczyna się od zera.
    await fireEvent.click(await screen.findByTestId(`booking-employee-${EMP_NO_SCHEDULE.id}`));
    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
    await screen.findByText(NO_SCHEDULE_TEXT);
    await waitFor(() => expect(screen.getByText(titleFor(0))).toBeTruthy());
    expect(screen.queryByText(titleFor(2))).toBeNull();
    expect(screen.queryByText(titleFor(3))).toBeNull();

    // 3) Powrót do pracownika z grafikiem → znów najbliższy termin (+2), nie utknięcie na „teraz”.
    await fireEvent.click(await screen.findByTestId(`booking-employee-${EMP_SCHEDULED.id}`));
    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));
    await waitFor(() => expect(screen.getByText(titleFor(2))).toBeTruthy());
  });

  it("odróżnia „brak grafiku” od „wszystko zajęte” — inny komunikat dla każdego przypadku", async () => {
    render(BookingFlow, {
      props: {
        dataSource: buildDataSource([EMP_BOOKED]),
        salonSlug: "test-salon",
        enableBotCheck: false,
      },
    });

    await fireEvent.click(await screen.findByTestId("booking-service-svc-1"));

    // Auto-pick doskakuje do końca okna rezerwacji (+MAX_MONTHS_AHEAD). Tam „sprawdź kolejny"
    // byłoby radą nie do wykonania — przycisk „następny miesiąc" jest wyłączony.
    await screen.findByText(LAST_MONTH_TEXT);
    expect(screen.queryByText(NO_SCHEDULE_TEXT)).toBeNull();
    expect(screen.getByLabelText("Następny miesiąc").hasAttribute("disabled")).toBe(true);

    // Cofnięcie o miesiąc: dalej wszystko zajęte, ale JEST dokąd iść dalej → wariant „sprawdź kolejny".
    await fireEvent.click(screen.getByLabelText("Poprzedni miesiąc"));
    await screen.findByText(FULLY_BOOKED_TEXT);
    expect(screen.queryByText(LAST_MONTH_TEXT)).toBeNull();
  });
});
