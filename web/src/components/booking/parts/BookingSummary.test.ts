import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import BookingSummary from "./BookingSummary.svelte";
import type { BookingServiceDto } from "../../../lib/booking-openapi-client";

// Regresja: usługa z `hidePrice` nie pokazuje ceny w podsumowaniu kalendarza klienta.
// Wcześniej w miejscu ceny renderował się duży placeholder „—".

function baseProps(service: BookingServiceDto) {
  return {
    pickedServices: [service],
    comboDurationMinutes: service.durationInMinutes ?? 60,
    pickedEmployee: undefined,
    selectedDate: "2026-07-10",
    selectedSlot: null,
    primaryDisabled: true,
    onconfirm: () => {},
  };
}

describe("BookingSummary — ukrywanie ceny", () => {
  it("usługa z hidePrice: cena ani placeholder nie są renderowane", () => {
    const { container } = render(BookingSummary, {
      props: baseProps({
        id: "svc-hidden",
        name: "Konsultacja",
        durationInMinutes: 60,
        price: { amount: 120, currency: "PLN" },
        hidePrice: true,
      }),
    });

    const text = container.textContent ?? "";
    expect(text).not.toContain("120");
    expect(text).not.toContain("—");
    // Nazwa usługi i czas trwania nadal widoczne.
    expect(screen.getByText("Konsultacja")).toBeTruthy();
    expect(text).toContain("60 min");
  });

  it("usługa bez hidePrice: cena jest pokazana", () => {
    const { container } = render(BookingSummary, {
      props: baseProps({
        id: "svc-priced",
        name: "Manicure",
        durationInMinutes: 60,
        price: { amount: 120, currency: "PLN" },
        hidePrice: false,
      }),
    });

    expect(container.textContent ?? "").toContain("120");
  });
});
