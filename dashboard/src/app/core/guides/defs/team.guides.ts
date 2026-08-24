import { GuideDef } from '../guide.types';

/** Przewodniki po rozbudowie salonu z jednoosobowego na zespołowy. */

/** „Dodajmy pracownika" — konto, zaproszenie mailem i to, co trzeba zrobić PO dodaniu. */
export const ADD_EMPLOYEE_GUIDE: GuideDef = {
  id: 'add-employee',
  title: 'Dodajmy pracownika',
  summary: 'Założymy profil nowej osoby i wyślemy jej zaproszenie do panelu.',
  category: 'team',
  roles: ['owner', 'manager'],
  icon: 'pi pi-user-plus',
  entryRoute: '/admin/team',
  steps: [
    {
      kind: 'explain',
      route: '/admin/team',
      popover: {
        title: 'Nowa osoba w salonie',
        description:
          'Dodanie pracownika robi dwie rzeczy naraz: tworzy jego profil (widoczny przy rezerwacji) i wysyła zaproszenie do panelu.' +
          '<br><br>Przejdziemy przez to i powiem, co trzeba zrobić zaraz potem — bo samo dodanie nie wystarczy.',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="team-add"]',
      // Osobna strona formularza, nie szuflada — czekamy aż wyrenderuje się pole e-mail.
      advanceOn: { on: 'appear', selector: '[data-tour="employee-email"]' },
      popover: {
        title: 'Otwórz formularz',
        description: 'Kliknij <strong>Dodaj specjalistę</strong>.',
        side: 'bottom',
        align: 'end',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="employee-email"]',
      popover: {
        title: 'Adres e-mail jest kluczowy',
        description:
          'Na podany adres poleci link do ustawienia hasła — to nim pracownik zaloguje się do panelu.' +
          '<br><br>Imię i nazwisko zobaczy klientka przy wyborze osoby w rezerwacji, więc wpisz je tak, jak mają być publicznie widoczne.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      // Po zapisie wracamy na listę zespołu, więc formularz znika z ekranu.
      advanceOn: { on: 'disappear', selector: '[data-tour="employee-email"]' },
      popover: {
        title: 'Zapisz',
        description: 'Uzupełnij dane i zapisz. Zaproszenie wyśle się automatycznie.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Zostały dwie rzeczy',
        description:
          'Nowa osoba jest w zespole, ale klientki jeszcze się do niej nie zapiszą. Trzeba jeszcze:' +
          '<br><br>1. <strong>przypisać jej usługi</strong> — inaczej nie ma czego u niej rezerwować,<br>' +
          '2. <strong>ustawić jej grafik</strong> — inaczej nie ma wolnych terminów.' +
          '<br><br>Oba kroki mają własne przewodniki w katalogu.',
      },
    },
  ],
};

/**
 * „Przypiszmy usługi pracownikowi" — cichy powód pustych terminów: osoba jest w zespole,
 * ale nie ma przypisanej żadnej usługi, więc w rezerwacji nie da się jej wybrać.
 */
export const ASSIGN_EMPLOYEE_SERVICES_GUIDE: GuideDef = {
  id: 'assign-employee-services',
  title: 'Przypiszmy usługi pracownikowi',
  summary: 'Ustalimy, kto co wykonuje — bez tego klientka nie zarezerwuje u tej osoby żadnej wizyty.',
  category: 'team',
  roles: ['owner', 'manager'],
  icon: 'pi pi-sitemap',
  entryRoute: '/admin/team',
  steps: [
    {
      kind: 'explain',
      route: '/admin/team',
      popover: {
        title: 'Kto co robi',
        description:
          'Usługa z cennika staje się dostępna u konkretnej osoby dopiero wtedy, gdy zostanie jej przypisana.' +
          '<br><br>To najczęstsza przyczyna zgłoszenia „dodałam pracownika, a klientki nie mogą się do niego zapisać".',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="team-grid"]',
      // Czekamy na POJAWIENIE SIĘ panelu usług, a nie na konkretne kliknięcie: do tego ekranu
      // da się dojść na kilka sposobów, a przewodnik nie powinien narzucać jednego.
      advanceOn: { on: 'appear', selector: '[data-tour="employee-services"]' },
      popover: {
        title: 'Otwórz usługi pracownika',
        description:
          'Na karcie osoby, której chcesz przypisać usługi, otwórz menu <strong>⋮</strong> i wybierz <strong>Usługi</strong>.' +
          '<br><br>(Sam przycisk „Zarządzaj" prowadzi do dostępności, nie do usług.)',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="employee-services"]',
      popover: {
        title: 'Usługi tej osoby',
        description:
          'Zaznacz to, co faktycznie wykonuje. Dla każdej usługi możesz nadpisać <strong>czas</strong> i <strong>cenę</strong> — przydaje się, gdy jedna osoba robi zabieg szybciej albo drożej niż reszta zespołu.' +
          '<br><br>Bez nadpisania obowiązują wartości z cennika.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'Sprawdź efekt na swojej stronie rezerwacji: wybierz tę osobę i zobacz, czy pokazują się właściwe usługi i terminy.' +
          '<br><br>Jeśli terminów brak — brakuje grafiku. Uruchom przewodnik <strong>„Ustawmy grafik powtarzalny"</strong>.',
      },
    },
  ],
};

/** „Uruchommy konto recepcji" — wspólny laptop przy stanowisku, bez dostępu do ustawień salonu. */
export const SETUP_RECEPTION_GUIDE: GuideDef = {
  id: 'setup-reception',
  title: 'Uruchommy konto recepcji',
  summary: 'Założymy wspólne konto na laptopa w salonie — do obsługi wizyt całego zespołu, bez dostępu do ustawień i pieniędzy.',
  category: 'team',
  roles: ['owner'],
  icon: 'pi pi-desktop',
  entryRoute: '/admin/settings/reception',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/reception',
      popover: {
        title: 'Po co osobne konto',
        description:
          'Na wspólnym laptopie przy stanowisku nie chcesz logować się swoim kontem właścicielki — dałoby to każdemu dostęp do ustawień, rozliczeń i danych zespołu.' +
          '<br><br>Konto recepcji widzi kalendarz i obsługuje wizyty. Nic więcej.' +
          '<br><br><strong>Widzisz już „Konto aktywne"?</strong> Recepcja jest gotowa — możesz zamknąć ten przewodnik, a hasło zmienisz na tej samej stronie.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="reception-email"]',
      // Gdy konto recepcji już istnieje, strona pokazuje „Konto aktywne" zamiast formularza —
      // ten krok znika wtedy sam, a następny przerwie przewodnik z komunikatem.
      keepWhenMissing: true,
      popover: {
        title: 'Adres i hasło do współdzielenia',
        description:
          'Ten adres nie musi być prawdziwą skrzynką — to login. Hasło znają wszyscy przy stanowisku, więc traktuj je jak hasło do salonu, nie do konta osobistego.' +
          '<br><br>Zmieniasz je w tym samym miejscu, gdy ktoś odchodzi z pracy.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="reception-create"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Utwórz konto',
        description: 'Podaj adres i hasło, a potem kliknij <strong>Utwórz konto</strong>.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'Zaloguj się tym kontem na salonowym laptopie i zostaw je zalogowane.' +
          '<br><br>Recepcja może dodawać i obsługiwać wizyty całego zespołu — nie zobaczy ustawień, zadatków ani rozliczeń.',
      },
    },
  ],
};
