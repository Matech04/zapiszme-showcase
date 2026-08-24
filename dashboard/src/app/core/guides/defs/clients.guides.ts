import { GuideDef } from '../guide.types';

/** Przewodniki codziennej obsługi: zapisanie wizyty z telefonu i dopisanie klientki do bazy. */

/**
 * „Dodajmy wizytę ręcznie" — najczęstsza czynność w całym panelu (klientka dzwoni zamiast
 * rezerwować online) i jedyna wspólna dla wszystkich ról, łącznie z recepcją.
 *
 * UWAGA na przepływ: „Dodaj wizytę" NIE prowadzi na osobną stronę, tylko otwiera szufladę
 * (`create-appointment-drawer`) nad kalendarzem. Pełnostronicowy `/admin/schedule/new` istnieje,
 * ale kalendarz z niego nie korzysta — przewodnik celuje w szufladę, bo to ją widzi użytkownik.
 */
export const ADD_APPOINTMENT_GUIDE: GuideDef = {
  id: 'add-appointment',
  title: 'Dodajmy wizytę ręcznie',
  summary: 'Zapiszemy klientkę, która zadzwoniła zamiast rezerwować online — od wyboru usługi po godzinę w kalendarzu.',
  category: 'clients',
  roles: ['owner', 'manager', 'employee', 'kiosk'],
  icon: 'pi pi-calendar-plus',
  entryRoute: '/admin/schedule/:me',
  steps: [
    {
      kind: 'explain',
      route: '/admin/schedule/:me',
      popover: {
        title: 'Zapiszmy wizytę z telefonu',
        description:
          'Nie każda klientka rezerwuje online — część po prostu dzwoni. Wizytę dopisujesz wtedy sama, a system i tak pilnuje, żeby termin był wolny.' +
          '<br><br>Przejdziemy przez to razem.',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="calendar-add"]',
      advanceOn: { on: 'appear', selector: '[data-tour="appointment-services"]' },
      popover: {
        title: 'Otwórz formularz wizyty',
        description:
          'Kliknij <strong>Dodaj wizytę</strong> — z boku wysunie się panel.' +
          '<br><br>Na skróty: kliknięcie w konkretny dzień lub godzinę w kalendarzu otwiera ten sam panel z wypełnionym terminem.',
        side: 'bottom',
        align: 'end',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="appointment-employee"]',
      // Wybór pracownika pojawia się tylko w salonie z zespołem — solo nie ma czego wybierać.
      keepWhenMissing: true,
      popover: {
        title: 'Kto wykonuje wizytę',
        description:
          'Wybór pracownika decyduje, w czyim kalendarzu wyląduje wizyta i które godziny będą wolne.' +
          '<br><br>Pracujesz sama? Tego pola w ogóle nie zobaczysz — ustawi się samo.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="appointment-services"]',
      popover: {
        title: 'Usługa wyznacza długość',
        description:
          'Czas trwania z cennika określa, ile miejsca wizyta zajmie w kalendarzu — nie musisz liczyć godziny zakończenia.' +
          '<br><br>Możesz wybrać kilka usług naraz; czas się zsumuje, a poniżej i tak da się go ręcznie nadpisać.',
        side: 'right',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="appointment-customer"]',
      popover: {
        title: 'Trzy sposoby na klientkę',
        description:
          '<strong>Z listy</strong> — klientka jest już w bazie.<br>' +
          '<strong>Numer telefonu</strong> — nowa osoba; karta założy się sama.<br>' +
          '<strong>Gość</strong> — wizyta bez danych, np. gdy ktoś wpadł z ulicy.' +
          '<br><br>Bez numeru nie wyślemy przypomnienia — „Gościa" używaj tylko wtedy, gdy naprawdę nie masz kontaktu.',
        side: 'right',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="appointment-date"]',
      popover: {
        title: 'Termin',
        description:
          'Po wybraniu daty lista godzin pokaże wyłącznie terminy realnie wolne — z uwzględnieniem grafiku, urlopów i wizyt już umówionych.' +
          '<br><br>Pusta lista oznacza, że tego dnia nie ma miejsca albo nie pracujesz.',
        side: 'right',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-drawer-submit"]',
      // Panel znika dopiero po udanym zapisie — przy zajętym terminie zostaje otwarty,
      // więc sam klik nie odróżniłby sukcesu od kolizji.
      advanceOn: { on: 'disappear', selector: '[data-tour="appointment-services"]' },
      popover: {
        title: 'Zapisz wizytę',
        description:
          'Uzupełnij dane i kliknij <strong>Zarezerwuj</strong>.' +
          '<br><br>Jeśli termin zdążył się zająć, panel to zgłosi — wybierz inną godzinę i zapisz ponownie. Poczekam.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Wizyta jest w kalendarzu',
        description:
          'Kliknij ją, żeby zobaczyć szczegóły, zmienić termin albo wygenerować zadatek.' +
          '<br><br>Jeśli masz włączone powiadomienia, klientka dostanie potwierdzenie i przypomnienie przed wizytą.',
      },
    },
  ],
};

/** „Dodajmy klientkę" — wejście do bazy i historii wizyt. */
export const ADD_CUSTOMER_GUIDE: GuideDef = {
  id: 'add-customer',
  title: 'Dodajmy klientkę do bazy',
  summary: 'Założymy kartę klientki, żeby jej wizyty, kontakt i notatki trzymały się w jednym miejscu.',
  category: 'clients',
  roles: ['owner', 'manager', 'employee'],
  icon: 'pi pi-user-plus',
  entryRoute: '/admin/customers',
  steps: [
    {
      kind: 'explain',
      route: '/admin/customers',
      popover: {
        title: 'Po co osobna karta klientki',
        description:
          'Każda wizyta — także ta z rezerwacji online — dopina się do karty klientki. Dzięki temu przed wizytą widzisz całą historię: co robiłaś, kiedy i za ile.' +
          '<br><br>Klientki rezerwujące online zakładają się same. Tę dodamy ręcznie.',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="customers-add"]',
      advanceOn: { on: 'appear', selector: '[data-tour="customer-name"]' },
      popover: {
        title: 'Otwórz formularz',
        description: 'Kliknij <strong>Dodaj klienta</strong>.',
        side: 'bottom',
        align: 'end',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="customer-phone"]',
      popover: {
        title: 'Telefon jest najważniejszy',
        description:
          'Wszystkie pola są opcjonalne, ale to po numerze telefonu system rozpoznaje powracającą klientkę i na niego wysyła przypomnienia.' +
          '<br><br>Bez numeru karta będzie tylko notatnikiem.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      advanceOn: { on: 'disappear', selector: '[data-tour="customer-name"]' },
      popover: {
        title: 'Zapisz kartę',
        description: 'Uzupełnij dane i zapisz.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'Klientka jest na liście — kliknij wiersz, żeby otworzyć jej profil z historią wizyt i notatkami.' +
          '<br><br>Przy dodawaniu wizyty znajdziesz ją teraz po nazwisku albo numerze.',
      },
    },
  ],
};
