import { GuideDef } from '../guide.types';

/**
 * „Ustawmy godziny na wybrany dzień" — jednorazowy wyjątek od grafiku powtarzalnego
 * („w ten piątek pracuję do 14"), bez ruszania całego grafiku.
 *
 * Najczęstsze nieporozumienie, które ten przewodnik ma rozbroić: wyjątek dotyczy JEDNEJ daty.
 * Kilkudniowa nieobecność to urlop, nie wyjątek — i tak to nazywamy w treści.
 */
export const SET_SPECIAL_DAY_GUIDE: GuideDef = {
  id: 'set-special-day',
  title: 'Ustawmy godziny na wybrany dzień',
  summary: 'Zmienimy godziny w jednym, konkretnym dniu — bez przestawiania grafiku na cały tydzień.',
  category: 'availability',
  roles: ['owner', 'manager', 'employee'],
  icon: 'pi pi-star',
  entryRoute: '/admin/my-availability/:me',
  steps: [
    {
      kind: 'explain',
      route: '/admin/my-availability/:me',
      popover: {
        title: 'Jeden dzień, inne godziny',
        description:
          'Zdarza się, że w konkretnym dniu pracujesz krócej, dłużej albo wcale — a reszta tygodnia zostaje bez zmian.' +
          '<br><br>Do tego służą godziny na wybrany dzień. Ustawimy je razem.' +
          '<br><br><strong>Uwaga:</strong> kilkudniowa nieobecność to urlop, nie wyjątek — na to jest osobny kafelek.',
      },
    },
    {
      kind: 'action',
      route: '/admin/my-availability/:me',
      element: '[data-tour="overrides-card"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Otwórz godziny na wybrany dzień',
        description: 'Kliknij ten kafelek — czekam.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      route: '/admin/my-availability/:me/special-days',
      element: '[data-tour="special-day-date"]',
      popover: {
        title: 'Wybierz datę',
        description:
          'Zacznij od daty, której zmiana ma dotyczyć. Panel od razu pokaże, jakie godziny wynikają dziś z Twojego grafiku i czy masz w tym dniu wizyty.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="special-day-working"]',
      keepWhenMissing: true,
      popover: {
        title: 'Pracujesz w tym dniu?',
        description:
          'Zostaw włączone, żeby ustawić inne godziny.' +
          '<br><br>Wyłącz, jeśli to dzień wolny — klienci nie zarezerwują wtedy żadnego terminu.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="special-day-save"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Zapisz godziny dnia',
        description:
          'Ustaw godziny i kliknij <strong>Zapisz godziny dnia</strong>.' +
          '<br><br>Możesz też skorzystać z szablonu — wypełni godziny jednym kliknięciem.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'Ten dzień pojawi się na liście <strong>Ustawione dni</strong> pod formularzem — stamtąd go edytujesz albo usuwasz.' +
          '<br><br>Grafik powtarzalny został nietknięty; wyjątek działa tylko w tej jednej dacie.',
      },
    },
  ],
};
