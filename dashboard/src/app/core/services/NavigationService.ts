// core/services/NavigationService.ts
import { Injectable, computed, signal } from '@angular/core';
import { NavGroup, NavItem } from '@core/models/navigation.model';

export type UserRole = 'owner' | 'manager' | 'employee' | 'kiosk' | 'systemAdmin';

@Injectable({
  providedIn: 'root',
})
export class NavigationService {
  /**
   * Rola zalogowanego; `null` = jeszcze nie wiemy (sesja się ładuje) albo wylogowany.
   * NIE dawać tu fallbacku na `'owner'` — menu właścicielki mignęłoby każdemu przy starcie.
   */
  private _currentUserRole = signal<UserRole | null>(null);

  /**
   * Czy zalogowany użytkownik widzi cały zespół. Owner/manager/kiosk = true z definicji;
   * pracownik = true tylko gdy `StaffCalendarVisibilityPolicy` ≥ TeamReadOnly.
   * Ustawiane przez MainLayoutComponent. Default true (bezpiecznie nie ukrywamy przed
   * owner/managerem zanim policy się załaduje; pozycje `requiresTeamVisibility` ma tylko employee).
   */
  private _canSeeTeam = signal<boolean>(true);

  /**
   * Główne menu owner/manager — jedna płaska lista, bez nagłówków sekcji. Podział na „Twój salon"
   * i „Zespół" dzielił sześć pozycji na trzy grupy, przez co skanowanie sidebara wymagało czytania
   * nagłówków zamiast ikon. Kolejność jest teraz jedynym nośnikiem hierarchii: od codziennego
   * użycia (Kalendarz, Dostępność) przez zasoby salonu po konfigurację na końcu.
   *
   * `mobilePriority` (1..N) rządzi kolejnością w sheecie „Więcej"; `bottomNav` przypina pozycję
   * do dolnego paska (Kalendarz + Dostępność). Trzymamy je zgodne z kolejnością sidebara, żeby
   * desktop i mobile nie rozjeżdżały się w układzie.
   *
   * „Zgłoś" (feedback) jest poza tym menu — przeniesione do dropdown użytkownika (avatar menu).
   */
  private readonly _allMenuItems: NavItem[] = [
    {
      label: 'Kalendarz',
      icon: 'pi pi-calendar',
      path: 'schedule',
      roles: ['owner', 'manager', 'kiosk'],
      mobilePriority: 1,
      bottomNav: true,
    },
    {
      label: 'Dostępność',
      icon: 'pi pi-clock',
      path: 'my-availability',
      roles: ['owner', 'manager'],
      mobilePriority: 2,
      bottomNav: true,
    },
    {
      label: 'Zespół',
      icon: 'pi pi-users',
      path: 'team',
      roles: ['owner', 'manager'],
      mobilePriority: 3,
    },
    {
      label: 'Usługi',
      icon: 'pi pi-briefcase',
      path: 'services',
      roles: ['owner', 'manager'],
      mobilePriority: 4,
    },
    {
      label: 'Klienci',
      icon: 'pi pi-id-card',
      path: 'customers',
      roles: ['owner', 'manager'],
      mobilePriority: 5,
    },
    {
      label: 'Ustawienia',
      icon: 'pi pi-cog',
      path: 'settings',
      roles: ['owner', 'manager'],
      mobilePriority: 6,
    },
    {
      // Katalog przewodników zadaniowych. Sama strona filtruje zawartość po roli, więc
      // pozycję pokazujemy tylko rolom, dla których jakikolwiek przewodnik istnieje —
      // kiosk i admin systemowy trafiliby na pusty ekran.
      label: 'Przewodniki',
      icon: 'pi pi-compass',
      path: 'guides',
      roles: ['owner', 'manager'],
      mobilePriority: 7,
    },

    // — Pozostałe role —

    // System admin:
    {
      label: 'Salony',
      icon: 'pi pi-building',
      path: 'system/tenants',
      roles: ['systemAdmin'],
      mobilePriority: 1,
    },
    {
      label: 'Kody promocyjne',
      icon: 'pi pi-ticket',
      path: 'system/promocodes',
      roles: ['systemAdmin'],
      mobilePriority: 2,
    },
    {
      label: 'Szablony SMS',
      icon: 'pi pi-comment',
      path: 'system/sms-templates',
      roles: ['systemAdmin'],
      mobilePriority: 3,
    },

    // Pracownik / kiosk:
    // Dolny pasek mobile = Kalendarz + Dostępność (bottomNav), reszta trafia do „Więcej".
    {
      label: 'Kalendarz',
      icon: 'pi pi-calendar',
      path: 'schedule',
      roles: ['employee'],
      mobilePriority: 1,
      bottomNav: true,
    },
    {
      label: 'Dostępność',
      icon: 'pi pi-clock',
      path: 'my-availability',
      roles: ['employee'],
      mobilePriority: 2,
      bottomNav: true,
    },
    {
      label: 'Moje usługi',
      icon: 'pi pi-briefcase',
      path: 'my-services',
      roles: ['employee'],
      mobilePriority: 3,
    },
    {
      label: 'Klienci',
      icon: 'pi pi-id-card',
      path: 'customers',
      roles: ['employee'],
      mobilePriority: 4,
    },
    {
      // Roster zespołu (tylko-do-odczytu) — widoczny pracownikowi tylko gdy właściciel
      // włączył widoczność zespołu (StaffCalendarVisibilityPolicy ≥ TeamReadOnly).
      label: 'Zespół',
      icon: 'pi pi-users',
      path: 'team',
      roles: ['employee'],
      mobilePriority: 5,
      requiresTeamVisibility: true,
    },
    {
      // Pracownik nie ma dostępu do ustawień salonu (`settings` = staffManagementGuard).
      // Jedyne ustawienia, jakie ma, to konto: profil, hasło, e-mail (`/admin/account`).
      label: 'Ustawienia',
      icon: 'pi pi-cog',
      path: 'account',
      roles: ['employee'],
      mobilePriority: 6,
    },
    {
      // Pracownik ma własne przewodniki (grafik, godziny na wybrany dzień, urlop) — te dotyczące
      // usług i ustawień salonu katalog przed nim ukrywa.
      // Recepcja widzi tu wyłącznie „Dodajmy wizytę ręcznie" — jedyną czynność, którą wykonuje.
      label: 'Przewodniki',
      icon: 'pi pi-compass',
      path: 'guides',
      roles: ['employee', 'kiosk'],
      mobilePriority: 7,
    },
  ];

  /** Płaska lista pozycji menu dla bieżącej roli. Nieznana rola = puste menu (fail-closed). */
  public readonly filteredMenu = computed<NavItem[]>(() => {
    const role = this._currentUserRole();
    if (role === null) return [];
    const canSeeTeam = this._canSeeTeam();
    const scheduleEmployeeId = this._scheduleEmployeeId();
    return this._allMenuItems
      .filter(
        (item) =>
          item.roles.includes(role) && !(item.requiresTeamVisibility && !canSeeTeam),
      )
      .map((item) =>
        item.path === 'schedule' && scheduleEmployeeId
          ? { ...item, linkPath: `schedule/${scheduleEmployeeId}` }
          : item,
      );
  });

  /**
   * Pracownik, którego kalendarz otworzy link „Kalendarz". Bez tego link celuje w gołą
   * `/admin/schedule`, komponent montuje się, wykonuje redirect na trasę z `:employeeId`
   * i montuje się PONOWNIE — cały zestaw zapytań leci dwa razy.
   *
   * To tylko podpowiedź: jeśli pracownik został zdeaktywowany albo pochodzi z innego salonu,
   * kalendarz i tak zweryfikuje id wobec swojej listy i w razie czego przekieruje. Gorszy
   * przypadek = dzisiejsze zachowanie, nie gorsze.
   */
  private readonly _scheduleEmployeeId = computed<string | null>(() => {
    const role = this._currentUserRole();
    // Pracownik/kiosk mają własne id pod ręką; owner/manager biorą ostatnio oglądanego.
    const own = this._currentEmployeeId();
    if (own && (role === 'employee' || role === 'kiosk')) return own;
    return this._rememberedScheduleEmployeeId();
  });

  private readonly _currentEmployeeId = signal<string | null>(null);
  private readonly _rememberedScheduleEmployeeId = signal<string | null>(null);

  /** Ustawiane przez MainLayoutComponent — serwis nie sięga sam po sesję ani localStorage. */
  public setScheduleEmployeeHints(
    currentEmployeeId: string | null,
    rememberedEmployeeId: string | null,
  ): void {
    this._currentEmployeeId.set(currentEmployeeId);
    this._rememberedScheduleEmployeeId.set(rememberedEmployeeId);
  }

  /**
   * Podział menu na dolny pasek mobile vs drawer „Więcej".
   * - Gdy rola ma pozycje z jawnym `bottomNav: true` → pasek = tylko one, reszta do „Więcej".
   *   (owner/manager: Kalendarz + Dostępność; Klienci/Ustawienia/Usługi/Zespół → „Więcej")
   *   (employee: Kalendarz + Dostępność; Moje usługi/Klienci/Zespół/Ustawienia → „Więcej")
   * - W przeciwnym razie zachowanie domyślne: pierwsze 4 po `mobilePriority`, reszta do „Więcej".
   *   (kiosk/systemAdmin)
   */
  private readonly _mobilePartition = computed<{ bottom: NavItem[]; more: NavItem[] }>(() => {
    const sorted = [...this.filteredMenu()].sort(
      (a, b) => (a.mobilePriority ?? 99) - (b.mobilePriority ?? 99),
    );
    const explicit = sorted.filter((i) => i.bottomNav);
    if (explicit.length > 0) {
      const pinned = new Set(explicit);
      return { bottom: explicit, more: sorted.filter((i) => !pinned.has(i)) };
    }
    return { bottom: sorted.slice(0, 4), more: sorted.slice(4) };
  });

  public readonly bottomNavMenu = computed<NavItem[]>(() => this._mobilePartition().bottom);

  public readonly moreNavMenu = computed<NavItem[]>(() => this._mobilePartition().more);

  /** Drawer „Więcej" pogrupowany w sekcje — analogicznie do sidebara desktop. */
  public readonly moreNavGroups = computed<NavGroup[]>(() => {
    const moreItems = new Set(this.moreNavMenu().map((i) => i.path + '/' + i.label));
    return this.groupedMenu()
      .map((g) => ({
        ...g,
        items: g.items.filter((i) => moreItems.has(i.path + '/' + i.label)),
      }))
      .filter((g) => g.items.length > 0);
  });

  /**
   * Menu jako pojedyncza grupa bez nagłówka — tak samo dla każdej roli. Sidebar i sheet „Więcej"
   * pomijają nagłówek przy pustym `label`, więc renderują płaską listę / siatkę kafelków.
   * Kształt `NavGroup[]` zostaje, bo oba szablony iterują po grupach.
   */
  public readonly groupedMenu = computed<NavGroup[]>(() => {
    const items = this.filteredMenu();
    return items.length ? [{ section: 'daily', label: '', items }] : [];
  });

  /** Ustawiane przez MainLayoutComponent na podstawie roli + StaffCalendarVisibilityPolicy. */
  public setCanSeeTeam(value: boolean): void {
    this._canSeeTeam.set(value);
  }

  /**
   * Czy pozycja menu jest aktywna dla danego URL. Wspólna logika dla sidebar desktop
   * i bottom-nav mobile (zapobiega dryfowi: dodanie nowego case w jednym miejscu wystarczy).
   *
   * URL musi być znormalizowany (bez query/hash) — typowo `router.url.split('?')[0]`.
   */
  public isItemActive(itemPath: string, url: string): boolean {
    const base = '/admin/' + itemPath;
    switch (itemPath) {
      case 'schedule':
        // Kalendarz jest stroną domową — aktywny też dla bazowego /admin (redirect → schedule).
        return (
          url === base ||
          url.startsWith(base + '/') ||
          url.startsWith('/admin/appointment/') ||
          url === '/admin'
        );
      case 'customers':
        return url.startsWith('/admin/customers');
      case 'team':
        return (
          url.startsWith('/admin/team') ||
          url.startsWith('/admin/resources/employees') ||
          url.startsWith('/admin/resources/shift-templates')
        );
      case 'services':
        return (
          url.startsWith('/admin/services') ||
          url.startsWith('/admin/resources/categories') ||
          url.startsWith('/admin/resources/service')
        );
      case 'settings':
        return (
          url.startsWith('/admin/settings') ||
          url.startsWith('/admin/salon') ||
          url.startsWith('/admin/vat-rates')
        );
      case 'my-availability':
        return url.startsWith('/admin/my-availability');
      default:
        return url === base || url.startsWith(base + '/');
    }
  }

  public setUserRole(role: UserRole | null): void {
    this._currentUserRole.set(role);
  }

  public getCurrentRole() {
    return this._currentUserRole();
  }

  syncRoleFromSession(roles: string[]): void {
    this.setUserRole(this.mapAppRolesToUserRole(roles));
  }

  /** `null` dla nieznanej listy ról — spójnie z `AuthSessionService.mapRoles`. */
  private mapAppRolesToUserRole(roles: readonly string[]): UserRole | null {
    const r = new Set(roles.map((x) => x.toLowerCase()));
    if (r.has('admin')) {
      return 'systemAdmin';
    }
    if (r.has('owner')) {
      return 'owner';
    }
    if (r.has('manager')) {
      return 'manager';
    }
    if (r.has('employee')) {
      return 'employee';
    }
    if (r.has('kiosk')) {
      return 'kiosk';
    }
    return null;
  }
}
