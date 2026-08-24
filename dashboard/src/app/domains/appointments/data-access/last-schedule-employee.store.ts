import { Injectable } from '@angular/core';

/**
 * Zapamiętuje ostatnio wybranego pracownika w kalendarzu, żeby wyjście do ustawień i powrót
 * na `/admin/schedule` (link w nawigacji nie niesie `:employeeId`) nie zrzucały widoku na
 * pierwszego pracownika z listy.
 *
 * Klucz jest zawężony do `userId`. Konto „Recepcja" (Kiosk) bywa współdzielonym terminalem,
 * a bez tego zawężenia wybór jednej osoby przenosiłby się na kolejną zalogowaną — i sugerował,
 * czyj kalendarz ktoś wcześniej oglądał.
 *
 * Zapisany identyfikator to jedynie PODPOWIEDŹ: wołający musi sprawdzić, czy pracownik nadal
 * istnieje na jego liście (mógł zostać zdeaktywowany albo należeć do innego salonu po zmianie
 * sesji wsparcia). Sam store niczego nie autoryzuje.
 */
@Injectable({ providedIn: 'root' })
export class LastScheduleEmployeeStore {
  private static readonly KeyPrefix = 'zapisz.schedule.employee';

  read(userId: string | null | undefined): string | null {
    if (!userId) return null;
    try {
      const raw = globalThis.localStorage?.getItem(this.keyFor(userId));
      return raw && raw.trim() !== '' ? raw : null;
    } catch {
      /* tryb prywatny / brak dostępu do storage */
      return null;
    }
  }

  save(userId: string | null | undefined, employeeId: string | null | undefined): void {
    if (!userId || !employeeId) return;
    try {
      globalThis.localStorage?.setItem(this.keyFor(userId), employeeId);
    } catch {
      /* tryb prywatny / quota */
    }
  }

  private keyFor(userId: string): string {
    return `${LastScheduleEmployeeStore.KeyPrefix}:${userId}`;
  }
}
