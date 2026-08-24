import { render, screen, fireEvent } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import ServicePicker from "./ServicePicker.svelte";
import type { BookingServiceDto } from "../../../lib/booking-openapi-client";

// Minimalna usługa z 2 zdjęciami + opisem (rzutowanie, bo DTO to klasa NSwag, a komponent
// czyta tylko właściwości).
function service(over: Partial<BookingServiceDto> = {}): BookingServiceDto {
  return {
    id: "s1",
    name: "Strzyżenie",
    price: { amount: 100, currency: "PLN" },
    durationInMinutes: 45,
    isAddon: false,
    description: "Opis testowy",
    images: [
      { url: "https://cdn/a.webp", thumbnailUrl: "https://cdn/a_thumb.webp", orderIndex: 0 },
      { url: "https://cdn/b.webp", thumbnailUrl: "https://cdn/b_thumb.webp", orderIndex: 1 },
    ],
    ...over,
  } as unknown as BookingServiceDto;
}

const baseProps = {
  serviceCategories: [] as any[],
  selectedServiceIds: [] as string[],
  // Pracownik jest już wybrany — usługi są widoczne (gate „najpierw wybierz pracownika" wyłączony).
  employeeSelected: true,
  ontoggle: vi.fn(),
};

describe("ServicePicker — szczegóły usługi (zdjęcia + opis)", () => {
  it('„Pokaż szczegóły" odsłania galerię WSZYSTKICH zdjęć i opis naraz', async () => {
    render(ServicePicker, { props: { ...baseProps, services: [service()] } });

    // Domyślnie zwinięte: ani galerii, ani opisu.
    expect(screen.queryByTestId("booking-service-gallery-s1")).toBeNull();
    expect(screen.queryByTestId("booking-service-desc-s1")).toBeNull();

    const toggle = screen.getByTestId("booking-service-details-toggle-s1");
    expect(toggle.textContent).toContain("Pokaż szczegóły");

    await fireEvent.click(toggle);

    // Po kliknięciu jeden przycisk pokazuje galerię (oba zdjęcia) ORAZ opis.
    const gallery = screen.getByTestId("booking-service-gallery-s1");
    expect(gallery.querySelectorAll("img").length).toBe(2);
    expect(screen.getByTestId("booking-service-desc-s1").textContent).toContain("Opis testowy");
    expect(toggle.textContent).toContain("Ukryj szczegóły");
  });

  it("bez zdjęć i opisu nie ma przycisku szczegółów", () => {
    render(ServicePicker, {
      props: { ...baseProps, services: [service({ images: [], description: undefined })] },
    });
    expect(screen.queryByTestId("booking-service-details-toggle-s1")).toBeNull();
  });
});

describe("ServicePicker — kompaktowa cena widełkowa nie zgniata nazwy", () => {
  it("renderuje kompaktowy zakres w jednej linii (nowrap), a nazwa dostaje resztę (flex-1)", () => {
    render(ServicePicker, {
      props: {
        ...baseProps,
        services: [
          service({
            name: "Przedłużenie bardzo długie 7+",
            price: { amount: 100, currency: "PLN" },
            maxAmount: 120,
            images: [],
            description: undefined,
          }),
        ],
      },
    });

    // Kompaktowy zakres "100–120 zł" (jedno "zł"); regex znosi twardą spację przed "zł" od Intl.
    const price = screen.getByText(/100\s*–\s*120\s*zł/);
    // Cena trzyma się jednej linii (kompaktowa jest wąska) — nie rozjeżdża się w pionie.
    expect(price.className).toContain("whitespace-nowrap");

    // Kolumna nazwy zabiera pozostałą szerokość (flex-1 + min-w-0) i zawija po słowach.
    const nameCol = screen.getByText("Przedłużenie bardzo długie 7+").parentElement;
    expect(nameCol?.className ?? "").toContain("flex-1");
    expect(nameCol?.className ?? "").toContain("min-w-0");
  });
});
