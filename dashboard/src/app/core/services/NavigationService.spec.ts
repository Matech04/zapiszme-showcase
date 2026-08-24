import { describe, expect, it } from 'vitest';
import { NavigationService, UserRole } from './NavigationService';

describe('NavigationService', () => {
  const visiblePathsFor = (role: UserRole): string[] => {
    const service = new NavigationService();
    service.setUserRole(role);
    return service.filteredMenu().map((item) => item.path);
  };

  it('owner widzi pełne menu (3 sekcje: codzienne + salon + zespół)', () => {
    expect(visiblePathsFor('owner')).toEqual([
      'schedule',
      'my-availability',
      'team',
      'services',
      'customers',
      'settings',
      'guides',
    ]);
  });

  it('owner zawsze widzi „Zespół" (także jako jedyny pracownik)', () => {
    expect(visiblePathsFor('owner')).toContain('team');
  });

  it('manager — to samo menu co owner', () => {
    expect(visiblePathsFor('manager')).toEqual([
      'schedule',
      'my-availability',
      'team',
      'services',
      'customers',
      'settings',
      'guides',
    ]);
  });

  it('employee widzi swoje pozycje + Klienci i Ustawienia (konto), a „Zespół" gdy widzi zespół', () => {
    // Default `_canSeeTeam = true` → pozycja „Zespół" (requiresTeamVisibility) jest widoczna.
    expect(visiblePathsFor('employee')).toEqual([
      'schedule',
      'my-availability',
      'my-services',
      'customers',
      'team',
      'account',
      'guides',
    ]);
  });

  it('employee: „Ustawienia" prowadzą do konta, nie do ustawień salonu', () => {
    const service = new NavigationService();
    service.setUserRole('employee');
    const settings = service.filteredMenu().find((i) => i.label === 'Ustawienia');
    expect(settings?.path).toBe('account');
    expect(visiblePathsFor('employee')).not.toContain('settings');
  });

  it('employee bez widoczności zespołu (OwnCalendarOnly) nie widzi „Zespół", ale widzi Klienci', () => {
    const service = new NavigationService();
    service.setUserRole('employee');
    service.setCanSeeTeam(false);
    expect(service.filteredMenu().map((i) => i.path)).toEqual([
      'schedule',
      'my-availability',
      'my-services',
      'customers',
      'account',
      'guides',
    ]);
  });

  it('kiosk widzi kalendarz i przewodniki', () => {
    expect(visiblePathsFor('kiosk')).toEqual(['schedule', 'guides']);
  });

  // Menu jest płaskie: jedna grupa bez nagłówka. Sidebar/„Więcej" pomijają pusty `label`,
  // więc brak nagłówków „Twój salon"/„Zespół" jest tu egzekwowany, nie tylko w szablonie.
  it('groupedMenu ownera to jedna grupa bez nagłówka, w kolejności sidebara', () => {
    const service = new NavigationService();
    service.setUserRole('owner');
    const groups = service.groupedMenu();

    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe('');
    expect(groups[0].items.map((i) => i.path)).toEqual([
      'schedule',
      'my-availability',
      'team',
      'services',
      'customers',
      'settings',
      'guides',
    ]);
  });

  it('groupedMenu pracownika też jest jedną grupą bez nagłówka', () => {
    const service = new NavigationService();
    service.setUserRole('employee');
    const groups = service.groupedMenu();

    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe('');
  });

  it('groupedMenu bez roli (sesja się ładuje) jest puste — fail-closed', () => {
    expect(new NavigationService().groupedMenu()).toEqual([]);
  });

  it('kalendarz jest aktywny na bazowym /admin (redirect → schedule) i /admin/schedule', () => {
    const service = new NavigationService();
    expect(service.isItemActive('schedule', '/admin')).toBe(true);
    expect(service.isItemActive('schedule', '/admin/schedule')).toBe(true);
    expect(service.isItemActive('schedule', '/admin/schedule/emp-1')).toBe(true);
    expect(service.isItemActive('schedule', '/admin/appointment/abc/edit')).toBe(true);
  });

  it('„Przewodniki" tylko dla ról, które mają jakikolwiek przewodnik', () => {
    // Kiosk i admin systemowy trafiliby na pusty katalog — rejestr nie ma dla nich definicji.
    expect(visiblePathsFor('owner')).toContain('guides');
    expect(visiblePathsFor('employee')).toContain('guides');
    // Recepcja ma jeden przewodnik („Dodajmy wizytę ręcznie"), więc pozycja jej przysługuje.
    expect(visiblePathsFor('kiosk')).toContain('guides');
    expect(visiblePathsFor('systemAdmin')).not.toContain('guides');
  });

  it('menu ownera/managera nie zawiera już usuniętej pozycji „dashboard"', () => {
    expect(visiblePathsFor('owner')).not.toContain('dashboard');
    expect(visiblePathsFor('manager')).not.toContain('dashboard');
  });

  describe('podział mobile: dolny pasek vs „Więcej"', () => {
    it('owner: dolny pasek = tylko Kalendarz + Dostępność (bottomNav), reszta w „Więcej"', () => {
      const service = new NavigationService();
      service.setUserRole('owner');
      expect(service.bottomNavMenu().map((i) => i.path)).toEqual(['schedule', 'my-availability']);
      // Kolejność w „Więcej" idzie za sidebarem — desktop i mobile nie mogą się rozjeżdżać.
      expect(service.moreNavMenu().map((i) => i.path)).toEqual([
        'team',
        'services',
        'customers',
        'settings',
        'guides',
      ]);
    });

    it('manager: identyczny podział jak owner', () => {
      const service = new NavigationService();
      service.setUserRole('manager');
      expect(service.bottomNavMenu().map((i) => i.path)).toEqual(['schedule', 'my-availability']);
    });

    it('employee: dolny pasek = Kalendarz + Dostępność, reszta w „Więcej"', () => {
      const service = new NavigationService();
      service.setUserRole('employee');
      expect(service.bottomNavMenu().map((i) => i.path)).toEqual(['schedule', 'my-availability']);
      expect(service.moreNavMenu().map((i) => i.path)).toEqual([
        'my-services',
        'customers',
        'team',
        'account',
        'guides',
      ]);
    });
  });

  it.each([
    [['Owner'], 'owner'],
    [['Manager'], 'manager'],
    [['Employee'], 'employee'],
    [['Kiosk'], 'kiosk'],
    [['Employee', 'Manager'], 'manager'],
  ] as [string[], UserRole][])('maps session roles %j to %s', (roles, expectedRole) => {
    const service = new NavigationService();

    service.syncRoleFromSession(roles);

    expect(service.getCurrentRole()).toBe(expectedRole);
  });

  /**
   * Rola nieznana (sesja się ładuje) albo wylogowany = puste menu. Wcześniej `mapRoles([])`
   * zwracało 'owner', więc menu właścicielki migało każdemu przy starcie panelu.
   */
  describe('nieznana rola — fail-closed', () => {
    it('świeży serwis (przed sync sesji) ma puste menu', () => {
      const service = new NavigationService();
      expect(service.getCurrentRole()).toBeNull();
      expect(service.filteredMenu()).toEqual([]);
      expect(service.bottomNavMenu()).toEqual([]);
      expect(service.moreNavMenu()).toEqual([]);
    });

    it('setUserRole(null) czyści menu (wylogowanie)', () => {
      const service = new NavigationService();
      service.setUserRole('owner');
      expect(service.filteredMenu().length).toBeGreaterThan(0);
      service.setUserRole(null);
      expect(service.filteredMenu()).toEqual([]);
    });

    it.each([[[]], [['Nieznana']]])('syncRoleFromSession(%j) → null, NIE "owner"', (roles) => {
      const service = new NavigationService();
      service.syncRoleFromSession(roles as string[]);
      expect(service.getCurrentRole()).toBeNull();
    });
  });
  describe('linkPath kalendarza (unikanie podwójnego montowania)', () => {
    // Goły `/admin/schedule` wpada w redirect na `/admin/schedule/:employeeId`, a ten NISZCZY
    // i odtwarza komponent kalendarza — zmierzone 44 requesty zamiast 22 przy każdym kliknięciu
    // „Kalendarz" w menu. `path` zostaje tożsamością pozycji (podświetlenie), `linkPath` celuje.
    it('owner: bez podpowiedzi link zostaje goły (fallback = dzisiejsze zachowanie)', () => {
      const service = new NavigationService();
      service.setUserRole('owner');
      const item = service.filteredMenu().find((i) => i.path === 'schedule');
      expect(item?.linkPath).toBeUndefined();
    });

    it('owner: zapamiętany pracownik trafia do linkPath, path bez zmian', () => {
      const service = new NavigationService();
      service.setUserRole('owner');
      service.setScheduleEmployeeHints(null, 'emp-remembered');
      const item = service.filteredMenu().find((i) => i.path === 'schedule');
      expect(item?.linkPath).toBe('schedule/emp-remembered');
      // `path` musi zostać nietknięty — na nim opiera się isItemActive i badge.
      expect(item?.path).toBe('schedule');
      expect(service.isItemActive('schedule', '/admin/schedule/emp-remembered')).toBe(true);
    });

    it('pracownik: własne id ma pierwszeństwo przed zapamiętanym', () => {
      const service = new NavigationService();
      service.setUserRole('employee');
      service.setScheduleEmployeeHints('emp-self', 'emp-other');
      const item = service.filteredMenu().find((i) => i.path === 'schedule');
      expect(item?.linkPath).toBe('schedule/emp-self');
    });

    it('owner: własne id pracownika NIE nadpisuje zapamiętanego wyboru', () => {
      const service = new NavigationService();
      service.setUserRole('owner');
      service.setScheduleEmployeeHints('emp-self', 'emp-other');
      const item = service.filteredMenu().find((i) => i.path === 'schedule');
      expect(item?.linkPath).toBe('schedule/emp-other');
    });

    it('podpowiedź nie dotyka innych pozycji menu', () => {
      const service = new NavigationService();
      service.setUserRole('owner');
      service.setScheduleEmployeeHints(null, 'emp-1');
      const inne = service.filteredMenu().filter((i) => i.path !== 'schedule');
      expect(inne.every((i) => i.linkPath === undefined)).toBe(true);
    });
  });
});
