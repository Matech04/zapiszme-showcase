// navigation.model.ts
export type UserRole = 'owner' | 'manager' | 'employee' | 'kiosk' | 'systemAdmin';

/** Identyfikator grupy menu. Menu jest dziś płaskie — została jedna grupa (`daily`, bez nagłówka). */
export type NavSection = 'daily' | 'salon' | 'team';

export interface NavGroup {
  section: NavSection;
  /** Tytuł sekcji w UI. Pusty string = grupa bez nagłówka (tak renderujemy dziś całe menu). */
  label: string;
  items: NavItem[];
}

export interface NavItem {
  label: string;
  icon: string;
  /** Względna ścieżka pod `/admin/`, np. `dashboard`. Pomijana dla samych grup. */
  path: string;
  /**
   * Ścieżka użyta w `routerLink`, gdy różni się od `path`. `path` zostaje tożsamością pozycji
   * (podświetlanie aktywnej pozycji, badge), a `linkPath` może być bardziej konkretny.
   *
   * Powód: „Kalendarz" linkujący na gołą `/admin/schedule` wpada w redirect na
   * `/admin/schedule/:employeeId`, a ten NISZCZY i odtwarza komponent kalendarza — zmierzone
   * 44 requesty zamiast 22 i dwa pełne montowania przy każdym kliknięciu w menu.
   */
  linkPath?: string;
  roles: UserRole[];
  /** Pozycja w bottom nav na mobile (gdy obecna). Pierwsze 4 idą do głównego paska, reszta do "Więcej". */
  mobilePriority?: number;
  /**
   * Jawnie przypnij pozycję do dolnego paska mobile. Gdy którakolwiek pozycja bieżącej roli
   * ma tę flagę, bottom-nav = tylko pozycje z `bottomNav: true` (reszta trafia do „Więcej"),
   * zamiast domyślnego cięcia po `mobilePriority` (pierwsze 4). Role bez tej flagi zachowują
   * dotychczasowe zachowanie.
   */
  bottomNav?: boolean;
  /** Numer w odznace (np. liczba oczekujących wizyt). */
  badge?: number;
  /** Wewnętrzne pozycje (sub-nav), gdy dany wpis jest grupą (np. Ustawienia). */
  children?: NavItem[];
  /**
   * Pokaż tylko gdy zalogowany użytkownik widzi cały zespół (`canSeeTeam`). Dla pracownika
   * zależy to od `StaffCalendarVisibilityPolicy` (≥ TeamReadOnly). Owner/manager mają to z definicji.
   */
  requiresTeamVisibility?: boolean;
}
