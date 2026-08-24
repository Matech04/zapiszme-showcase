import { render, screen, fireEvent, waitFor } from "@testing-library/svelte";
import { beforeAll, describe, expect, it } from "vitest";
import InspirationsPicker from "./InspirationsPicker.svelte";
import {
  MAX_INSPIRATION_IMAGES,
  type PendingInspirationImage,
} from "../../../lib/booking/data-source";

// jsdom nie implementuje object URL-i — picker generuje z nich podgląd, więc mockujemy.
beforeAll(() => {
  let n = 0;
  URL.createObjectURL = () => `blob:mock/${n++}`;
  URL.revokeObjectURL = () => {};
});

function pending(id: string): PendingInspirationImage {
  return {
    id,
    file: new File([new Uint8Array([1])], `${id}.png`, { type: "image/png" }),
    previewUrl: `blob:mock/${id}`,
  };
}

function fakeImageFile(name = "hair.png"): File {
  return new File([new Uint8Array([1, 2, 3])], name, { type: "image/png" });
}

describe("InspirationsPicker (deferred-upload)", () => {
  it("trzyma wybrany obraz lokalnie i pokazuje podgląd (bez uploadu)", async () => {
    const { container } = render(InspirationsPicker, {
      props: { pending: [] },
    });

    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    await fireEvent.change(input, { target: { files: [fakeImageFile()] } });

    // Podgląd renderowany z lokalnego blob URL — nic nie zostało wgrane na storage.
    await waitFor(() =>
      expect(container.querySelectorAll("img").length).toBe(1),
    );
    expect(screen.getByText(`1/${MAX_INSPIRATION_IMAGES}`)).toBeTruthy();
  });

  it("odrzuca plik nie będący obrazem (brak podglądu)", async () => {
    const { container } = render(InspirationsPicker, {
      props: { pending: [] },
    });

    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    const notImage = new File(["x"], "doc.pdf", { type: "application/pdf" });
    await fireEvent.change(input, { target: { files: [notImage] } });

    expect(container.querySelectorAll("img").length).toBe(0);
    await screen.findByText(/tylko zdjęcia/i);
  });

  it("nie pozwala dodać ponad limit — przycisk dodawania znika", () => {
    const full = Array.from({ length: MAX_INSPIRATION_IMAGES }, (_, i) => pending(String(i)));
    render(InspirationsPicker, {
      props: { pending: full },
    });

    expect(screen.queryByText("Dodaj")).toBeNull();
    expect(screen.getByText(`${MAX_INSPIRATION_IMAGES}/${MAX_INSPIRATION_IMAGES}`)).toBeTruthy();
  });
});
