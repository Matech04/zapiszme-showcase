import { Injectable, signal } from '@angular/core';

/** Prośba o otwarcie panelu wizyty w już zamontowanym kalendarzu. */
export interface AppointmentFocusRequest {
  appointmentId: string;
  /** Czy przy okazji przestawić kalendarz na dzień (i pracownika) wizyty. */
  reveal: boolean;
}

/**
 * Kanał „otwórz tę wizytę" omijający router — dla kliknięć w dzwonku, gdy kalendarz JEST już
 * na ekranie.
 *
 * Wcześniej powiadomienie linkowało na gołą `/admin/schedule?appointment=<id>`, co uruchamiało
 * kaskadę: pushState → redirect na `/admin/schedule/:employeeId` (a ten NISZCZY i odtwarza
 * komponent kalendarza) → zapis dnia w URL → nawigacja czyszcząca paramy. Zmierzone: 4 zapisy
 * URL, dwa pełne montowania kalendarza i ~46 requestów do API — żeby otworzyć drawer, któremu
 * wystarczy jeden. Użytkownik widział to jako miganie URL i przeładowanie kalendarza.
 *
 * Gdy kalendarz jest zamontowany, nawigacja nie wnosi nic: dla `reveal: false` i tak NIE
 * zmieniamy dnia ani pracownika, więc URL nie ma co opisywać. Dzwonek zgłasza wizytę tutaj,
 * kalendarz reaguje efektem i otwiera panel — zero nawigacji.
 *
 * Deep-linki spoza aplikacji (`?appointment=<id>` z profilu klienta, wklejony link) dalej idą
 * ścieżką query-paramów — tam URL jest jedynym nośnikiem intencji.
 */
@Injectable({ providedIn: 'root' })
export class AppointmentFocusService {
  private readonly _requested = signal<AppointmentFocusRequest | null>(null);

  /** Ustawione = kalendarz ma otworzyć tę wizytę. Konsument czyści przez `clear()`. */
  readonly requested = this._requested.asReadonly();

  request(appointmentId: string, reveal: boolean): void {
    this._requested.set({ appointmentId, reveal });
  }

  clear(): void {
    this._requested.set(null);
  }
}
