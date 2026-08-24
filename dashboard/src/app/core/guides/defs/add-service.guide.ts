import { GuideDef } from '../guide.types';

/**
 * „Dodajmy usługę" — od pustego katalogu do usługi widocznej w publicznej rezerwacji.
 *
 * Rola `employee` jest tu celowo nieobecna: katalog usług chroni `staffManagementGuard`
 * (owner/manager). Pracownik może się do istniejących usług przypisać („Moje usługi"),
 * ale nie może ich tworzyć — pokazanie mu tego przewodnika kończyłoby się odbiciem od guarda.
 */
export const ADD_SERVICE_GUIDE: GuideDef = {
  id: 'add-service',
  title: 'Dodajmy usługę',
  summary: 'Dodamy pierwszą pozycję do cennika — z nazwą, ceną i czasem trwania, dokładnie tak, jak zobaczy ją klient.',
  category: 'offer',
  roles: ['owner', 'manager'],
  icon: 'pi pi-briefcase',
  entryRoute: '/admin/services',
  steps: [
    {
      kind: 'explain',
      route: '/admin/services',
      popover: {
        title: 'Dodajmy usługę do cennika',
        description:
          'Usługa to jedna pozycja, którą klient wybiera przy rezerwacji. Bez choć jednej usługi nie ma czego zarezerwować.' +
          '<br><br>Dodamy ją razem — zajmie minutę.',
      },
    },
    {
      kind: 'action',
      route: '/admin/services',
      element: '[data-tour="services-new"]',
      advanceOn: { on: 'appear', selector: '[data-tour="service-essentials"]' },
      popover: {
        title: 'Otwórz formularz usługi',
        description: 'Kliknij <strong>Nowa usługa</strong> — otworzy się panel z formularzem. Czekam.',
        side: 'bottom',
        align: 'end',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="service-essentials"]',
      popover: {
        title: 'Trzy rzeczy, które wystarczą',
        description:
          '<strong>Nazwa</strong> — to zobaczy klient na liście.<br>' +
          '<strong>Cena</strong> — wpisz 0, jeśli usługa jest bezpłatna.<br>' +
          '<strong>Czas rezerwacji</strong> — tyle miejsca wizyta zajmie w kalendarzu.' +
          '<br><br>Reszta pól jest opcjonalna; ukryte sekcje możesz uzupełnić później.',
        side: 'right',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-drawer-submit"]',
      // Panel formularza znika dopiero po UDANYM zapisie — to sygnał, że usługa naprawdę
      // powstała. Sam klik nie wystarczy: przy błędzie walidacji panel zostaje otwarty.
      advanceOn: { on: 'disappear', selector: '[data-tour="service-essentials"]' },
      popover: {
        title: 'Zapisz usługę',
        description: 'Wypełnij nazwę, cenę i czas, a potem kliknij <strong>Dodaj usługę</strong>.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe — usługa jest w cenniku',
        description:
          'Klient zobaczy ją przy rezerwacji od razu.' +
          '<br><br>Gdy usług przybędzie, pogrupuj je w kategorie (np. „Paznokcie", „Brwi") — lista zrobi się czytelniejsza.',
      },
    },
  ],
};
