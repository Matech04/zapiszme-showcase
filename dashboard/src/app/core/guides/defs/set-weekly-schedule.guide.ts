import { GuideDef } from '../guide.types';

/**
 * „Ustawmy grafik powtarzalny" — przewodnik zadaniowy prowadzący od huba Dostępności aż do
 * ZAPISANEGO grafiku. Kończy się realnym grafikiem w bazie, nie samą wiedzą, gdzie go szukać.
 *
 * Grafik jest warunkiem, bez którego publiczna rezerwacja nie ma czego pokazać, więc to
 * najważniejszy przewodnik w katalogu.
 */
export const SET_WEEKLY_SCHEDULE_GUIDE: GuideDef = {
  id: 'set-weekly-schedule',
  title: 'Ustawmy grafik powtarzalny',
  summary: 'Przejdziemy razem przez ustawienie godzin pracy na każdy dzień tygodnia — to one stają się wolnymi terminami dla klientów.',
  category: 'availability',
  roles: ['owner', 'manager', 'employee'],
  icon: 'pi pi-calendar',
  entryRoute: '/admin/my-availability/:me',
  steps: [
    {
      kind: 'explain',
      route: '/admin/my-availability/:me',
      popover: {
        title: 'Ustawmy Twój grafik',
        description:
          'Grafik powtarzalny to godziny, które powtarzają się co tydzień — i dokładnie to klienci widzą jako wolne terminy.' +
          '<br><br>Przejdziemy przez to razem. Na końcu będziesz mieć zapisany grafik, nie tylko wiedzę, gdzie go szukać.',
      },
    },
    {
      kind: 'action',
      route: '/admin/my-availability/:me',
      element: '[data-tour="schedules-card"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Otwórz grafiki powtarzalne',
        description: 'Kliknij ten kafelek — czekam.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'action',
      route: '/admin/my-availability/:me/schedules',
      element: '[data-tour="schedules-new"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Dodaj nowy grafik',
        description:
          'Kliknij <strong>Nowy grafik powtarzalny</strong>.' +
          '<br><br>Zwykle wystarczy jeden — kolejny dodajesz dopiero, gdy godziny mają się zmienić od konkretnej daty.',
        side: 'bottom',
        align: 'end',
      },
    },
    {
      kind: 'explain',
      route: '/admin/my-availability/:me/schedules/new',
      element: '[data-tour="slot-mode"]',
      popover: {
        title: 'Najpierw: jak wyznaczasz terminy?',
        description:
          '<strong>Elastyczne godziny</strong> — klient wybiera dowolną wolną godzinę w Twoich blokach pracy.' +
          '<br><br><strong>Ustalone godziny</strong> — klient wybiera tylko z godzin, które sama wpiszesz (np. 9:00, 12:00, 15:00).' +
          '<br><br>Nie wiesz? Zostaw elastyczne — zmienisz to w każdej chwili.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="schedule-days"]',
      popover: {
        title: 'Teraz wypełnij dni',
        description:
          'Ustaw godziny w dniach, w których pracujesz. Dni bez godzin są traktowane jako wolne.' +
          '<br><br>Nie musisz klikać każdego z osobna — wypełnij jeden dzień i skopiuj go na pozostałe.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="schedule-save"]',
      // Zapis w trybie tworzenia wraca na listę grafików — to najpewniejszy sygnał, że
      // grafik naprawdę wylądował w bazie (sam klik mógłby polec na walidacji dni).
      advanceOn: { on: 'route', route: '/admin/my-availability/:me/schedules' },
      popover: {
        title: 'Zapisz grafik',
        description:
          'Gdy godziny się zgadzają — kliknij <strong>Zapisz grafik</strong>.' +
          '<br><br>Jeśli któryś dzień ma błąd, panel go wskaże; popraw i zapisz ponownie. Poczekam.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe — grafik działa',
        description:
          'Od tej chwili klienci widzą wolne terminy w tych godzinach.' +
          '<br><br>Pojedynczy dzień z innymi godzinami ustawisz przewodnikiem <strong>Ustawmy godziny na wybrany dzień</strong> — nie trzeba do tego zmieniać całego grafiku.',
      },
    },
  ],
};
