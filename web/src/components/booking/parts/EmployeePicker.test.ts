import { render, screen, fireEvent } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EmployeePicker from "./EmployeePicker.svelte";
import type { BookingEmployee } from "../../../lib/booking/data-source";

function employee(over: Partial<BookingEmployee> = {}): BookingEmployee {
  return {
    id: "e1",
    firstName: "Anna",
    lastName: "Kowalska",
    hasUpcomingSchedule: true,
    ...over,
  } as BookingEmployee;
}

const baseProps = {
  selectedEmployeeId: "",
  loading: false,
  onselect: vi.fn(),
};

describe("EmployeePicker — pracownik bez terminów", () => {
  it("blokuje kafelek pracownika bez grafiku i opisuje dlaczego", async () => {
    const onselect = vi.fn();
    render(EmployeePicker, {
      props: {
        ...baseProps,
        onselect,
        employees: [
          employee(),
          employee({ id: "e2", firstName: "Bartek", hasUpcomingSchedule: false }),
        ],
      },
    });

    const blocked = screen.getByTestId("booking-employee-e2") as HTMLButtonElement;
    expect(blocked.disabled).toBe(true);
    expect(screen.getByTestId("booking-employee-unavailable-e2").textContent).toContain(
      "Brak wolnych terminów",
    );

    await fireEvent.click(blocked);
    expect(onselect).not.toHaveBeenCalled();

    // Pracownik z grafikiem działa normalnie — blokada nie rozlewa się na całą listę.
    const available = screen.getByTestId("booking-employee-e1") as HTMLButtonElement;
    expect(available.disabled).toBe(false);
    await fireEvent.click(available);
    expect(onselect).toHaveBeenCalledWith("e1");
  });

  it("NIE blokuje salonu solo — jedyny pracownik zawsze klikalny, nawet bez grafiku", async () => {
    // Solo: kalendarz ma się otworzyć normalnie i sam pokazać „brak terminów". Zablokowanie
    // jedynego kafelka zostawiłoby klientkę ze ślepą listą bez żadnego wyjaśnienia.
    const onselect = vi.fn();
    render(EmployeePicker, {
      props: {
        ...baseProps,
        onselect,
        employees: [employee({ hasUpcomingSchedule: false })],
      },
    });

    const solo = screen.getByTestId("booking-employee-e1") as HTMLButtonElement;
    expect(solo.disabled).toBe(false);
    expect(screen.queryByTestId("booking-employee-unavailable-e1")).toBeNull();

    await fireEvent.click(solo);
    expect(onselect).toHaveBeenCalledWith("e1");
  });

  it("traktuje brak pola jako dostępnego (starszy backend nie blokuje rezerwacji)", () => {
    render(EmployeePicker, {
      props: {
        ...baseProps,
        employees: [
          employee({ hasUpcomingSchedule: undefined }),
          employee({ id: "e2", hasUpcomingSchedule: undefined }),
        ],
      },
    });

    expect((screen.getByTestId("booking-employee-e1") as HTMLButtonElement).disabled).toBe(false);
    expect((screen.getByTestId("booking-employee-e2") as HTMLButtonElement).disabled).toBe(false);
  });
});
