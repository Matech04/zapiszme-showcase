import { GuideDef } from '../guide.types';

/**
 * „Pogrupujmy usługi w kategorie" — porządkowanie cennika, który przestaje być czytelny
 * po kilku pozycjach. Dotyczy zarówno panelu, jak i tego, co widzi klientka przy rezerwacji.
 */
export const GROUP_SERVICES_GUIDE: GuideDef = {
  id: 'group-services',
  title: 'Pogrupujmy usługi w kategorie',
  summary: 'Uporządkujemy cennik w sekcje (np. „Paznokcie", „Brwi"), żeby klientka szybciej znalazła to, po co przyszła.',
  category: 'offer',
  roles: ['owner', 'manager'],
  icon: 'pi pi-tags',
  entryRoute: '/admin/services',
  steps: [
    {
      kind: 'explain',
      route: '/admin/services',
      popover: {
        title: 'Kiedy kategorie zaczynają się opłacać',
        description:
          'Przy trzech usługach kategorie są zbędne. Przy kilkunastu — długa płaska lista męczy klientkę i zmniejsza szansę na rezerwację.' +
          '<br><br>Kategorie widać też przy zapisie online, więc porządkujesz obie strony naraz.',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="services-new-category"]',
      advanceOn: { on: 'appear', selector: '[data-tour="form-drawer-submit"]' },
      popover: {
        title: 'Utwórz kategorię',
        description:
          'Kliknij <strong>Nowa kategoria</strong>.' +
          '<br><br>Nazywaj je językiem klientki („Paznokcie", „Rzęsy"), a nie branżowym skrótem.',
        side: 'bottom',
        align: 'end',
      },
    },
    {
      kind: 'action',
      element: '[data-tour="form-drawer-submit"]',
      advanceOn: { on: 'disappear', selector: '[data-tour="form-drawer-submit"]' },
      popover: {
        title: 'Zapisz kategorię',
        description: 'Wpisz nazwę i zapisz.',
        side: 'top',
        align: 'end',
      },
    },
    {
      kind: 'outro',
      popover: {
        title: 'Teraz przenieś do niej usługi',
        description:
          'Kategoria jest pusta — usługi przypiszesz, edytując każdą z nich i wybierając kategorię w sekcji <strong>„Kategoria i dodatki"</strong>.' +
          '<br><br>Kolejność kategorii i usług w nich zmienisz przyciskiem <strong>Kolejność</strong> — to ona decyduje, co klientka zobaczy na górze listy.',
      },
    },
  ],
};
