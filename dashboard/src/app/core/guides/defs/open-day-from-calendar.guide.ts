import { GuideDef } from '../guide.types';

/**
 * „Otwórzmy dzień w kalendarzu" — dla właścicielki, która w kreatorze wybrała „Planuję każdy
 * miesiąc osobno". Jej kalendarz startuje PUSTY (brak grafiku powtarzalnego = zero dni otwartych),
 * więc dopóki nie otworzy pierwszego dnia, klientka nie zobaczy ani jednego wolnego terminu,
 * a panel wygląda na zepsuty.
 *
 * Prowadzi przez KALENDARZ, nie przez hub dostępności — mimo że `SET_SPECIAL_DAY_GUIDE` robi to
 * samo z drugiej strony. Powód jest dosłowny: krok „Jak układasz grafik?" obiecuje jej
 * „wybierasz konkretny dzień i klikasz «Ustaw godziny na ten dzień»” w kalendarzu, a przewodnik,
 * który zaraz potem prowadzi inną drogą, uczy nieufności do własnych instrukcji. Do tego dzień
 * otwiera się tam, gdzie i tak patrzy się na swój tydzień.
 *
 * Oba przewodniki kończą w tym samym formularzu: kalendarz osadza `EmployeeSpecialDaysComponent`
 * w szufladzie (`EmployeeSpecialDayDrawerComponent`), więc kotwice `special-day-*` są wspólne.
 * Różnica jest w zapisie — w szufladzie przycisk należy do jej shella
 * (`form-drawer-submit`), bo wewnętrzny `special-day-save` renderuje się tylko poza trybem
 * osadzonym.
 *
 * Kotwica `calendar-set-day-hours` wisi na trzech wariantach przycisku (podgląd dnia, karta dnia
 * wolnego, agenda) — wszystkie pod warunkiem „wybrany dzień jest wolny”. Stąd pierwszy krok każe
 * wybrać dzień jeszcze nieotwarty: u właścicielki ad-hoc to każdy dzień, ale ktoś z grafikiem
 * powtarzalnym uruchomiłby ten przewodnik w środę i nie znalazł przycisku.
 */
export const OPEN_DAY_FROM_CALENDAR_GUIDE: GuideDef = {
  id: 'open-day-from-calendar',
  title: 'Otwórzmy dzień w kalendarzu',
  summary:
    'Otworzymy pierwszy dzień na zapisy prosto z kalendarza — tak układa się miesiąc, gdy nie prowadzisz stałego grafiku.',
  category: 'availability',
  roles: ['owner', 'manager', 'employee'],
  icon: 'pi pi-calendar-plus',
  entryRoute: '/admin/schedule/:me',
  steps: [
    {
      kind: 'explain',
      route: '/admin/schedule/:me',
      popover: {
        title: 'Twój kalendarz zaczyna pusty',
        description:
          'Nie prowadzisz grafiku powtarzalnego, więc domyślnie nie pracujesz w żaden dzień — otwarte są wyłącznie te, które sama wpiszesz. To nie usterka, tylko Twój wybór z konfiguracji.' +
          '<br><br>Zacznijmy od jednego dnia. <strong>Wybierz w pasku u góry dzień, w którym jeszcze nie przyjmujesz</strong> — potem pokażę, gdzie ustawia się godziny.',
      },
    },
    {
      kind: 'action',
      // Kotwicą jest PASEK DNI, nie sam przycisk „Ustaw godziny na ten dzień”. Przycisk pokazuje
      // się wyłącznie dla dnia zamkniętego, więc przy odtwarzaniu przewodnika na dniu już otwartym
      // krok zadaniowy nie znajdował kotwicy i silnik przerywał całość komunikatem „przewodnik
      // wymaga aktualizacji” — mylącym, bo problemem był stan dnia, nie definicja. Pasek istnieje
      // zawsze, a zadanie i tak zalicza dopiero otwarcie szuflady godzin.
      element: '[data-tour="calendar-day-strip"]',
      advanceOn: { on: 'appear', selector: '[data-tour="special-day-date"]' },
      popover: {
        title: 'Wybierz dzień i otwórz godziny',
        description:
          'Kliknij w pasku dzień, w którym jeszcze nie przyjmujesz — pod kalendarzem pojawi się wtedy przycisk <strong>„Ustaw godziny na ten dzień”</strong>. Kliknij go, a ja poprowadzę dalej.' +
          '<br><br>Dni już otwarte tego przycisku nie mają: ich godziny zmienia się z tego samego panelu, ale wchodzi się w nie przez sam dzień.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="special-day-date"]',
      keepWhenMissing: true,
      popover: {
        title: 'Data jest już podstawiona',
        description:
          'Panel otworzył się na dniu, który wybrałaś w kalendarzu. Możesz go tu zmienić, jeśli się rozmyśliłaś — reszta ustawień dotyczy wyłącznie tej jednej daty.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="special-day-working"]',
      keepWhenMissing: true,
      popover: {
        title: 'Włącz pracę i podaj godziny',
        description:
          'Przełącznik „Pracujesz w tym dniu” otwiera dzień na zapisy — pod nim pojawią się godziny od i do. To one staną się wolnymi terminami dla klientek.' +
          '<br><br>Ten sam przełącznik działa w drugą stronę: wyłączony zamyka dzień, który wcześniej otworzyłaś.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-drawer-submit"]',
      // Szuflada zamyka się dopiero po UDANYM zapisie, więc jej zniknięcie jest jedynym
      // sygnałem odróżniającym sukces od błędu walidacji (np. odwrócone godziny).
      advanceOn: { on: 'disappear', selector: '[data-tour="form-drawer-submit"]' },
      popover: {
        title: 'Zapisz godziny dnia',
        description: 'Kliknij „Zapisz godziny dnia”. Panel zamknie się sam, gdy zapis przejdzie.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Dzień otwarty — klientki go widzą',
        description:
          'Ten dzień jest już w Twoim publicznym kalendarzu rezerwacji. Kolejne otwierasz tak samo: wybierasz datę w pasku i klikasz „Ustaw godziny na ten dzień”.' +
          '<br><br>Gdy zorientujesz się, że któryś tydzień powtarza się co miesiąc, możesz zamiast tego ustawić grafik powtarzalny — jest na to osobny przewodnik.',
      },
    },
  ],
};
