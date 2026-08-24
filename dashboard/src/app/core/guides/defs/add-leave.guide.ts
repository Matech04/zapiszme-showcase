import { GuideDef } from '../guide.types';

/**
 * „Zgłośmy urlop" — trzeci i ostatni z kafelków huba dostępności.
 *
 * Rozgraniczenie, które ten przewodnik ma utrwalić: urlop to KILKA DNI z rzędu. Zmiana godzin
 * w pojedynczej dacie to „godziny na wybrany dzień" i osobne narzędzie. Mylenie tych dwóch rzeczy
 * jest najczęstszym nieporozumieniem w całej sekcji dostępności.
 */
export const ADD_LEAVE_GUIDE: GuideDef = {
  id: 'add-leave',
  title: 'Zgłośmy urlop',
  summary: 'Zablokujemy rezerwacje na czas kilkudniowej nieobecności — urlopu, chorobowego albo wyjazdu.',
  category: 'availability',
  roles: ['owner', 'manager', 'employee'],
  icon: 'pi pi-calendar-minus',
  entryRoute: '/admin/my-availability/:me',
  steps: [
    {
      kind: 'explain',
      route: '/admin/my-availability/:me',
      popover: {
        title: 'Kilka dni bez rezerwacji',
        description:
          'Urlop wyłącza możliwość zapisania się na cały wskazany okres — nie musisz kasować grafiku ani przestawiać każdego dnia z osobna.' +
          '<br><br><strong>Uwaga na różnicę:</strong> jeśli chodzi o jeden dzień z innymi godzinami, to nie urlop, tylko „godziny na wybrany dzień".',
      },
    },
    {
      kind: 'action',
      route: '/admin/my-availability/:me',
      element: '[data-tour="leaves-card"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Otwórz urlopy i chorobowe',
        description: 'Kliknij ten kafelek — czekam.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      route: '/admin/my-availability/:me/leave-dashboard',
      element: '[data-tour="leave-add"]',
      advanceOn: { on: 'appear', selector: '[data-tour="leave-type"]' },
      popover: {
        title: 'Dodaj nieobecność',
        description: 'Kliknij <strong>Dodaj urlop</strong>.',
        side: 'bottom',
        align: 'end',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="leave-type"]',
      popover: {
        title: 'Rodzaj nieobecności',
        description:
          'Typ jest dla Ciebie — pomaga później rozpoznać, czemu tego tygodnia nie było Cię w salonie. Dla klientek efekt jest identyczny: brak wolnych terminów.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="leave-start"]',
      popover: {
        title: 'Zakres dat',
        description:
          'Data początkowa i końcowa włącznie. Nad polami masz skróty (<em>Dziś</em>, <em>Jutro</em>, <em>Następny tydzień</em>) na najczęstsze przypadki.' +
          '<br><br>Jeśli w tym okresie masz już umówione wizyty, panel Cię o tym uprzedzi — trzeba je przełożyć osobno, urlop ich nie odwoła.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      advanceOn: { on: 'disappear', selector: '[data-tour="leave-type"]' },
      popover: {
        title: 'Zapisz nieobecność',
        description: 'Ustaw zakres i zapisz.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'W tym okresie klientki nie zarezerwują wizyty. Nieobecność widnieje na liście — stamtąd ją edytujesz albo usuwasz, gdy plany się zmienią.' +
          '<br><br>Pamiętaj o wizytach, które były umówione wcześniej — urlop ich nie odwołuje.',
      },
    },
  ],
};
