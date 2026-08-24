import { GuideDef } from '../guide.types';

/**
 * Przewodniki wokół publicznej rezerwacji — czyli tego, co decyduje, czy do salonu w ogóle
 * trafiają zapisy.
 */

/**
 * „Udostępnijmy link do rezerwacji" — jedyny przewodnik kończący się czynnością POZA aplikacją.
 *
 * Powód istnienia: salon może mieć komplet grafiku i cennika, a mieć zero rezerwacji, bo nikt
 * nie zna adresu. Dlatego outro nie jest gratulacją, tylko listą miejsc do wklejenia — bez tego
 * „ukończono" nic by nie znaczyło.
 */
export const SHARE_BOOKING_LINK_GUIDE: GuideDef = {
  id: 'share-booking-link',
  title: 'Udostępnijmy link do rezerwacji',
  summary: 'Ustalimy adres Twojej strony zapisów i pokażę, gdzie go wkleić, żeby klientki zaczęły się umawiać same.',
  category: 'booking',
  roles: ['owner'],
  icon: 'pi pi-link',
  entryRoute: '/admin/settings/salon',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/salon',
      popover: {
        title: 'Twój adres zapisów',
        description:
          'Rezerwacje online działają pod własnym adresem — to jego wysyłasz klientkom i wklejasz w social media.' +
          '<br><br>Sprawdzimy, jak wygląda, i ustalimy, gdzie go umieścić.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="salon-slug"]',
      popover: {
        title: 'To jest końcówka Twojego linku',
        description:
          'Cały adres to <strong>zapisz.me/</strong> + to, co tu wpiszesz.' +
          '<br><br>Krótko i rozpoznawalnie — najlepiej nazwa salonu (np. <em>salon-ania</em>). Małe litery, spacje zamieniają się w myślniki.' +
          '<br><br>Jeśli już Ci się podoba, nie musisz nic zmieniać.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      // Ekrany ustawień po zapisie ZOSTAJĄ na miejscu (zmienia się tylko toast), więc nie ma
      // czego obserwować — w odróżnieniu od formularzy w szufladzie idziemy dalej po kliknięciu.
      advanceOn: { on: 'click' },
      popover: {
        title: 'Zapisz',
        description:
          'Kliknij zapis.' +
          '<br><br>Jeśli nazwa jest już zajęta przez inny salon, panel to zgłosi u góry ekranu — wtedy wybierz inną i zapisz ponownie.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Teraz najważniejsze — rozpowszechnij go',
        description:
          'Sam link nic nie zrobi, dopóki nie zobaczą go klientki. Wklej go:' +
          '<br><br>• w <strong>bio na Instagramie</strong> (tam trafia najwięcej zapisów),<br>' +
          '• w <strong>wizytówce Google</strong> jako stronę rezerwacji,<br>' +
          '• w <strong>stopce SMS-ów</strong> i wiadomości do stałych klientek,<br>' +
          '• na Facebooku w przycisku „Zarezerwuj".' +
          '<br><br>Zanim wyślesz — otwórz link u siebie i sprawdź, czy widać wolne terminy.',
      },
    },
  ],
};

/**
 * „Kto i kiedy może się zapisać" — pierwszy z dwóch przewodników po najgęstszym ekranie panelu
 * (zasady rezerwacji mają siedem niezależnych ustawień).
 *
 * Świadomie sam `explain`: tu nie ma jednej rzeczy do zrobienia, są decyzje. Rozbicie na dwa
 * przewodniki po 3–4 kroki bije jeden przewodnik po siedmiu przełącznikach.
 */
export const BOOKING_ACCESS_GUIDE: GuideDef = {
  id: 'booking-access-rules',
  title: 'Ustalmy, kto i kiedy może się zapisać',
  summary: 'Przejdziemy przez dostęp do rezerwacji, jak daleko w przód można się umawiać i co ile minut startują terminy.',
  category: 'booking',
  roles: ['owner'],
  icon: 'pi pi-sliders-h',
  entryRoute: '/admin/settings/booking',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/booking',
      popover: {
        title: 'Zasady rezerwacji — część pierwsza',
        description:
          'Ten ekran ma sporo ustawień, więc podzieliłam go na dwie części.' +
          '<br><br>Teraz: <strong>kto</strong> może się zapisać i <strong>na kiedy</strong>. Potwierdzanie wizyt i weryfikację klienta omawia drugi przewodnik.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="booking-access"]',
      popover: {
        title: 'Dostęp do rezerwacji online',
        description:
          'Główny włącznik zapisów. Wyłącz go, jeśli chcesz chwilowo wstrzymać przyjmowanie rezerwacji — link zostanie, ale klientki zobaczą komunikat zamiast terminów.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="booking-horizon"]',
      popover: {
        title: 'Jak daleko w przód',
        description:
          'Ile dni naprzód klientka może wybrać termin.' +
          '<br><br>Krótki horyzont (np. 30 dni) daje Ci swobodę w planowaniu; długi bywa wygodny dla stałych klientek umawiających się z wyprzedzeniem.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="booking-interval"]',
      popover: {
        title: 'Co ile minut startują terminy',
        description:
          'Przy interwale 15 minut wizyty mogą zaczynać się o 9:00, 9:15, 9:30… Przy 30 — tylko o pełnych połówkach.' +
          '<br><br>Gęstszy interwał = więcej terminów do wyboru, ale też więcej okienek trudnych do zapełnienia.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="booking-gap-filling"]',
      popover: {
        title: 'Wypełnianie luk',
        description:
          'Decyduje, czy system podpowiada klientkom terminy przylegające do już zajętych — dzięki temu dzień układa się w blok zamiast w dziury po 20 minut.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Zapisz ustawienia',
        description: 'Jeśli coś zmieniłaś — zapisz. Jeśli tylko oglądałaś, też kliknij, nic się nie stanie.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'Te ustawienia działają od razu na Twojej stronie zapisów.' +
          '<br><br>Drugą część — <strong>potwierdzanie wizyt i weryfikację klienta</strong> — znajdziesz w katalogu przewodników.',
      },
    },
  ],
};

/** „Jak potwierdzasz wizyty" — druga część zasad rezerwacji. */
export const BOOKING_CONFIRMATION_GUIDE: GuideDef = {
  id: 'booking-confirmation-rules',
  title: 'Ustalmy, jak potwierdzasz wizyty',
  summary: 'Zdecydujemy, czy rezerwacje wpadają od razu jako potwierdzone, czy czekają na Twoją zgodę — i jak weryfikujemy klientkę.',
  category: 'booking',
  roles: ['owner'],
  icon: 'pi pi-check-circle',
  entryRoute: '/admin/settings/booking',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/booking',
      popover: {
        title: 'Zasady rezerwacji — część druga',
        description:
          'Ta część decyduje o dwóch rzeczach: ile masz kontroli nad tym, co wpada do kalendarza, i jak bardzo utrudniamy życie komuś, kto rezerwuje bez zamiaru przyjścia.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="booking-confirmation"]',
      popover: {
        title: 'Potwierdzanie wizyt',
        description:
          '<strong>Automatyczne</strong> — rezerwacja od razu jest wiążąca. Mniej pracy, ale w kalendarzu ląduje wszystko.' +
          '<br><br><strong>Ręczne</strong> — wizyta czeka na Twoją akceptację. Więcej kontroli, ale musisz odpisywać; klientka nie ma pewności od razu.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="booking-verification"]',
      popover: {
        title: 'Weryfikacja klienta',
        description:
          'Kod potwierdzający przy rezerwacji odsiewa fałszywe zapisy i literówki w numerze.' +
          '<br><br>Kosztuje jedną wiadomość na rezerwację — o koszcie przypomni przewodnik o powiadomieniach.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="booking-team-calendar"]',
      keepWhenMissing: true,
      popover: {
        title: 'Kalendarz zespołu',
        description:
          'Ustala, ile pracownik widzi w kalendarzu: tylko swoje wizyty czy cały salon.' +
          '<br><br>Ma znaczenie, gdy pracują u Ciebie osoby, które nie powinny widzieć obłożenia koleżanek.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Zapisz',
        description: 'Zatwierdź wybór.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description:
          'Jeśli wybrałaś potwierdzanie ręczne — nowe rezerwacje pojawią się w kalendarzu jako <strong>oczekujące</strong> i będą czekać na Twoją decyzję.',
      },
    },
  ],
};

/** „Co klient podaje przy rezerwacji" — pola opcjonalne i regulamin (waga prawna). */
export const PUBLIC_FORM_GUIDE: GuideDef = {
  id: 'public-form-fields',
  title: 'Ustalmy, co klientka podaje przy rezerwacji',
  summary: 'Wybierzemy dodatkowe pola formularza i ustawimy regulamin, który klientka akceptuje przy zapisie.',
  category: 'booking',
  roles: ['owner'],
  icon: 'pi pi-id-card',
  entryRoute: '/admin/settings/public-form',
  steps: [
    {
      kind: 'explain',
      route: '/admin/settings/public-form',
      popover: {
        title: 'Formularz rezerwacji',
        description:
          'Im mniej pól, tym więcej dokończonych rezerwacji. Ale niektóre dane realnie oszczędzają czas przy wizycie.' +
          '<br><br>Przejdziemy przez to, co możesz dołożyć — i przez regulamin.',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="public-form-required"]',
      popover: {
        title: 'Dane podstawowe',
        description: 'Minimum potrzebne, żeby wizyta miała sens: kontakt do klientki i sposób powiadomienia jej o terminie.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="public-form-instagram"]',
      popover: {
        title: 'Pola dodatkowe',
        description:
          'Instagram i zdjęcia inspiracji bywają bezcenne przy stylizacjach — klientka pokazuje efekt, który ma w głowie, zanim usiądzie na fotelu.' +
          '<br><br>Każde dodatkowe pole to jednak jedna rzecz więcej do wypełnienia. Włączaj tylko to, czego naprawdę użyjesz.',
        side: 'bottom',
        align: 'start',
      },
    },
    {
      kind: 'explain',
      element: '[data-tour="public-form-terms"]',
      popover: {
        title: 'Regulamin — to nie jest formalność',
        description:
          'Tu wpisujesz zasady, które klientka akceptuje przy rezerwacji: odwoływanie wizyt, spóźnienia, zadatki, przetwarzanie danych.' +
          '<br><br>To jedyne miejsce, w którym możesz się na coś powołać przy sporze. Warto poświęcić mu chwilę.',
        side: 'top',
        align: 'start',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-submit"]',
      advanceOn: { on: 'click' },
      popover: {
        title: 'Zapisz',
        description: 'Zatwierdź ustawienia formularza.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Gotowe',
        description: 'Otwórz swój link do rezerwacji i przejdź formularz jak klientka — najszybciej wychwycisz, czy nie pytasz o za dużo.',
      },
    },
  ],
};
