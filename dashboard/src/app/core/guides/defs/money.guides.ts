import { GuideDef } from '../guide.types';

/**
 * Przewodniki po pieniądzach: zadatki (wpływ) i powiadomienia (koszt).
 *
 * Zadatki są rozbite na DWA przewodniki, a nie jeden dłuższy. Powód jest techniczny i uczciwy:
 * podłączenie Stripe wyprowadza użytkownika na obcą domenę, a przewodnik nie przeżywa wyjścia
 * z aplikacji. Zamiast udawać ciągłość, dzielimy rzecz na dwa etapy, którymi i tak są w praktyce —
 * każdy kończy się czymś skończonym.
 */

/** Etap 1: konto Stripe. Kończy się świadomym wyjściem na Stripe, nie udawaniem, że wróciliśmy. */
export const CONNECT_STRIPE_GUIDE: GuideDef = {
  id: 'connect-stripe',
  title: 'Podłączmy konto do zadatków',
  summary: 'Połączymy salon ze Stripe, żeby dało się pobierać zadatki. Pieniądze idą prosto na Twoje konto.',
  category: 'money',
  roles: ['owner'],
  icon: 'pi pi-credit-card',
  entryRoute: '/admin/settings/deposits',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/deposits',
      popover: {
        title: 'Po co zadatki',
        description:
          'Zadatek to najskuteczniejsze narzędzie przeciwko niedoszłym wizytom — klientka, która zapłaciła z góry, po prostu przychodzi.' +
          '<br><br>Żeby je pobierać, salon musi mieć podłączone konto płatnicze. Tym się teraz zajmiemy.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="deposits-stripe"]',
      popover: {
        title: 'Pieniądze nie przechodzą przez nas',
        description:
          'Zadatki trafiają bezpośrednio na Twoje konto Stripe. My tylko generujemy link do zapłaty — nie przechowujemy Twoich pieniędzy ani danych karty klientki.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="deposits-stripe-connect"]',
      popover: {
        title: 'Tu wychodzimy poza panel',
        description:
          'Kliknięcie przeniesie Cię na stronę Stripe, gdzie podasz dane firmy i numer konta. To zwykle kilka minut i trzeba mieć pod ręką dane do rozliczeń.' +
          '<br><br><strong>Ten przewodnik kończy się tutaj</strong> — po powrocie ze Stripe uruchom <em>„Ustawmy kwotę zadatku"</em>, żeby dokończyć konfigurację.' +
          '<br><br>Nie masz teraz czasu? Zamknij przewodnik i wróć, kiedy będziesz miała dane firmy.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Kolejny krok jest po powrocie',
        description:
          'Gdy Stripe potwierdzi konto, na tej stronie zobaczysz <strong>„Konto gotowe"</strong>.' +
          '<br><br>Wtedy uruchom przewodnik <strong>„Ustawmy kwotę zadatku"</strong> — ustawimy, ile i od kogo pobierasz.',
      },
    },
  ],
};

/** Etap 2: reguły zadatku. Zakłada, że konto Stripe jest już aktywne. */
export const SET_DEPOSIT_AMOUNT_GUIDE: GuideDef = {
  id: 'set-deposit-amount',
  title: 'Ustawmy kwotę zadatku',
  summary: 'Włączymy pobieranie zadatków i ustalimy, ile klientka płaci z góry przy rezerwacji.',
  category: 'money',
  roles: ['owner'],
  icon: 'pi pi-wallet',
  entryRoute: '/admin/settings/deposits',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/deposits',
      popover: {
        title: 'Zanim zaczniemy',
        description:
          'Ten przewodnik zakłada, że konto Stripe jest już podłączone — w karcie powyżej powinno widnieć <strong>„Konto gotowe"</strong>.' +
          '<br><br>Jeśli nie, zamknij i uruchom najpierw <em>„Podłączmy konto do zadatków"</em>.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="deposits-toggle"]',
      popover: {
        title: 'Włącznik zadatków',
        description:
          'Włącza akcję <strong>„Generuj zadatek"</strong> na wizytach. Zadatek nie pobiera się sam — decydujesz przy konkretnej wizycie, czy go zażądać.' +
          '<br><br>Dzięki temu możesz brać zadatki tylko od nowych klientek albo tylko przy dłuższych zabiegach.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="deposits-save"]',
      keepWhenMissing: true,
      popover: {
        title: 'Kwota i nazwa opłaty',
        description:
          'Powyżej ustawiasz sposób naliczania (procent od ceny albo stała kwota) oraz nazwę prawną opłaty.' +
          '<br><br>To wartość <strong>domyślna</strong> — przy każdej wizycie możesz ją zmienić.' +
          '<br><br>Nazwa ma znaczenie: „zadatek" i „zaliczka" różnią się skutkami przy rezygnacji klientki. Jeśli nie wiesz, którą wybrać, sprawdź to przed ustawieniem.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="deposits-save"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Zapisz',
        description: 'Zatwierdź ustawienia zadatku.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'Otwórz dowolną wizytę w kalendarzu — pojawi się tam akcja <strong>„Generuj zadatek"</strong>. Link do zapłaty wyślesz klientce albo skopiujesz ręcznie.' +
          '<br><br>Nie zapomnij dopisać zasad zwrotu zadatku do regulaminu.',
      },
    },
  ],
};

/**
 * „Ustawmy przypomnienia" — przewodnik, który celowo mówi o KOSZCIE, nie tylko o przełączniku.
 * SMS-y to jedyna część aplikacji, która wydaje realne pieniądze przy każdej wizycie.
 */
export const SET_REMINDERS_GUIDE: GuideDef = {
  id: 'set-reminders',
  title: 'Ustawmy przypomnienia o wizytach',
  summary: 'Włączymy powiadomienia dla Ciebie i dla klientek — i policzymy, ile to realnie kosztuje.',
  category: 'money',
  roles: ['owner'],
  icon: 'pi pi-bell',
  entryRoute: '/admin/settings/notifications',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/notifications',
      popover: {
        title: 'Przypomnienia zmniejszają liczbę nieobecności',
        description:
          'Klientka, która dostała SMS dzień wcześniej, dużo rzadziej zapomina o wizycie.' +
          '<br><br>Ale każdy SMS kosztuje, więc przejdziemy przez to świadomie — nie włączając wszystkiego naraz.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="notifications-salon"]',
      popover: {
        title: 'Powiadomienia dla Ciebie',
        description:
          'Informacje o nowych rezerwacjach i odwołaniach. E-mail i powiadomienia w panelu są darmowe — te włącz spokojnie.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="notifications-customer"]',
      popover: {
        title: 'Powiadomienia dla klientek — tu są pieniądze',
        description:
          'Jedna wizyta potrafi wygenerować kilka wiadomości: potwierdzenie, przypomnienie 24 h, przypomnienie 2 h, kod weryfikacyjny.' +
          '<br><br>Przy kilkuset wizytach miesięcznie to realna pozycja w kosztach. <strong>Jeśli masz wybierać jedno — zostaw przypomnienie 24 h</strong>, ono ratuje najwięcej wizyt.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Zapisz wybór',
        description: 'Zatwierdź ustawienia powiadomień.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe — teraz pilnuj zużycia',
        description:
          'Treść SMS-ów zmienisz w <strong>Szablony SMS</strong> (zmiany przechodzą przez akceptację operatora, więc nie działają od ręki).' +
          '<br><br>Ile faktycznie wysyłasz, sprawdzisz w <strong>Zużyciu</strong> — jest na to osobny przewodnik. Zajrzyj tam po pierwszym pełnym miesiącu.',
      },
    },
  ],
};

/** „Sprawdźmy zużycie" — krótki, ale to on chroni marżę salonu. */
export const CHECK_USAGE_GUIDE: GuideDef = {
  id: 'check-notification-usage',
  title: 'Sprawdźmy, ile kosztują powiadomienia',
  summary: 'Pokażę, gdzie podejrzeć liczbę wysłanych SMS-ów i e-maili, zanim rachunek Cię zaskoczy.',
  category: 'money',
  roles: ['owner', 'manager'],
  icon: 'pi pi-chart-bar',
  entryRoute: '/admin/settings/usage',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/usage',
      popover: {
        title: 'Jedyny licznik, który kosztuje',
        description:
          'Prawie wszystko w panelu jest darmowe — poza wiadomościami wychodzącymi do klientek. Ten ekran pokazuje, ile ich poszło.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="usage-sms"]',
      popover: {
        title: 'SMS-y — to jest Twój koszt',
        description:
          'Podziel tę liczbę przez liczbę wizyt w miesiącu, a dostaniesz koszt powiadomień na wizytę.' +
          '<br><br>Rośnie szybciej, niż się wydaje: potwierdzenie plus dwa przypomnienia to już trzy wiadomości na jedną wizytę.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="usage-email"]',
      popover: {
        title: 'E-maile są darmowe',
        description:
          'Jeśli koszt SMS-ów zaczyna boleć, rozważ przestawienie części powiadomień na e-mail — skuteczność jest niższa, ale koszt zerowy.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Wracaj tu raz w miesiącu',
        description:
          'Najlepiej po pierwszym pełnym miesiącu pracy — wtedy zobaczysz realny wzorzec i będziesz mogła świadomie przyciąć powiadomienia.',
      },
    },
  ],
};
