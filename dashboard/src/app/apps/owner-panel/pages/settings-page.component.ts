import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSessionService } from '@core/auth/auth-session.service';

interface SettingsCard {
  /** Segment trasy potomnej pod `settings` (np. 'salon' → /admin/settings/salon). */
  route: string;
  label: string;
  description: string;
  icon: string;
  cta: string;
  /**
   * Kafel wymaga uprawnień właściciela (backend: polityka `BusinessManagement`). Manager ma
   * `StaffManagement` — widzi/edytuje tylko Zużycie i VAT. Reszta zapisuje pełny `TenantDto`
   * (PUT SalonSettings) i managerowi zwróciłaby 403.
   */
  ownerOnly?: boolean;
}

interface SettingsGroup {
  title: string;
  cards: SettingsCard[];
}

/**
 * Hub Ustawień — landing z pogrupowanymi kaflami prowadzącymi do skupionych pod-stron
 * (wzorzec spójny z hubem dostępności). Zastępuje dawny mega-tab „Salon i marka" oraz płaski pasek
 * zakładek. Deep-link `?tab=<id>` (m.in. powrót ze Stripe: `?tab=deposits`) jest przekierowywany na
 * odpowiednią trasę potomną — dla zachowania zewnętrznych linków bez zmian w backendzie.
 */
@Component({
  selector: 'app-settings-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="admin-page-shell admin-page-pad-for-bottom-nav">
      <div class="admin-page-container">
        @for (group of visibleGroups(); track group.title) {
          <section class="space-y-3">
            <h2 class="admin-section-label text-surface-500 dark:text-surface-400 px-1">{{ group.title }}</h2>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              @for (card of group.cards; track card.route) {
                <a
                  [routerLink]="card.route"
                  [attr.data-testid]="'settings-card-' + card.route"
                  class="group block rounded-3xl border border-surface-200/90 dark:border-surface-100 bg-white/80 dark:bg-surface-50 p-5 sm:p-6 shadow-sm transition-all hover:shadow-md hover:border-primary/50"
                >
                  <div
                    class="w-12 h-12 sm:w-14 sm:h-14 rounded-2xl bg-primary/12 text-primary flex items-center justify-center mb-4 group-hover:scale-105 transition-transform"
                  >
                    <i [class]="card.icon" class="text-xl sm:text-2xl" aria-hidden="true"></i>
                  </div>
                  <h3 class="text-lg sm:text-xl font-bold text-surface-900 mb-1">{{ card.label }}</h3>
                  <p class="text-sm text-surface-600 dark:text-surface-400 leading-relaxed mb-3">
                    {{ card.description }}
                  </p>
                  <span
                    class="inline-flex items-center gap-1 text-sm font-bold text-primary group-hover:gap-2 transition-all"
                  >
                    {{ card.cta }}
                    <i class="pi pi-arrow-right text-xs" aria-hidden="true"></i>
                  </span>
                </a>
              }
            </div>
          </section>
        }
      </div>
    </div>
  `,
})
export class SettingsPageComponent {
  private readonly auth = inject(AuthSessionService);

  private readonly groups: SettingsGroup[] = [
    {
      title: 'Twój salon',
      cards: [
        {
          route: 'salon',
          label: 'Dane salonu',
          description: 'Nazwa, publiczny link do rezerwacji, strefa czasowa i waluta.',
          icon: 'pi pi-building',
          cta: 'Otwórz dane salonu',
          ownerOnly: true,
        },
        {
          route: 'appearance',
          label: 'Wygląd',
          description: 'Kolory publicznego kalendarza rezerwacji oraz akcent tego panelu.',
          icon: 'pi pi-palette',
          cta: 'Otwórz wygląd',
          ownerOnly: true,
        },
        {
          route: 'reception',
          label: 'Konto recepcji',
          description: 'Wspólne konto na laptopa w salonie do obsługi wizyt całego zespołu (bez dostępu do ustawień).',
          icon: 'pi pi-desktop',
          cta: 'Otwórz konto recepcji',
          ownerOnly: true,
        },
      ],
    },
    {
      title: 'Rezerwacje online',
      cards: [
        {
          route: 'booking',
          label: 'Zasady rezerwacji',
          description: 'Dostęp, potwierdzanie wizyt, weryfikacja klienta, wypełnianie luk i kalendarz zespołu.',
          icon: 'pi pi-sliders-h',
          cta: 'Otwórz zasady',
          ownerOnly: true,
        },
        {
          route: 'public-form',
          label: 'Dane przy rezerwacji',
          description: 'Co klient podaje i akceptuje przy rezerwacji: opcjonalne pola (Instagram, inspiracje) i regulamin.',
          icon: 'pi pi-file-edit',
          cta: 'Otwórz dane przy rezerwacji',
          ownerOnly: true,
        },
        {
          route: 'privacy',
          label: 'Prywatność',
          description: 'Przechowywanie historii wizyt i trwałe czyszczenie zebranych danych.',
          icon: 'pi pi-shield',
          cta: 'Otwórz prywatność',
          ownerOnly: true,
        },
      ],
    },
    {
      title: 'Powiadomienia',
      cards: [
        {
          route: 'notifications',
          label: 'Powiadomienia',
          description: 'Które zdarzenia wysyłają powiadomienia do salonu i do klientów.',
          icon: 'pi pi-bell',
          cta: 'Otwórz powiadomienia',
          ownerOnly: true,
        },
        {
          route: 'sms',
          label: 'Szablony SMS',
          description: 'Treść wiadomości SMS z podglądem i akceptacją.',
          icon: 'pi pi-comment',
          cta: 'Otwórz szablony',
          ownerOnly: true,
        },
        {
          route: 'usage',
          label: 'Zużycie SMS / e-mail',
          description: 'Ile wiadomości wysłano w miesiącu i limity pakietu.',
          icon: 'pi pi-chart-bar',
          cta: 'Otwórz zużycie',
        },
      ],
    },
    {
      title: 'Płatności',
      cards: [
        {
          route: 'deposits',
          label: 'Zadatki',
          description: 'Konto Stripe i pobieranie zadatków przy rezerwacji online.',
          icon: 'pi pi-wallet',
          cta: 'Otwórz zadatki',
          ownerOnly: true,
        },
        {
          route: 'vat',
          label: 'Stawki VAT',
          description: 'Definicje stawek VAT przypisywanych do usług.',
          icon: 'pi pi-percentage',
          cta: 'Otwórz stawki VAT',
        },
      ],
    },
    {
      title: 'Konto',
      cards: [
        {
          // Trasa absolutna — „Moje konto" jest poza /admin/settings/*. Dostępne dla każdej roli.
          route: '/admin/account',
          label: 'Moje konto',
          description: 'Zmień swój adres e-mail (login), hasło i nazwę wyświetlaną.',
          icon: 'pi pi-user',
          cta: 'Otwórz moje konto',
        },
      ],
    },
  ];

  /** Owner i (impersonujący) Admin mają pełen wgląd; Manager widzi tylko kafle bez `ownerOnly`. */
  private readonly isOwner = computed(() => {
    const role = this.auth.currentRole();
    return role === 'owner' || role === 'systemAdmin';
  });

  protected readonly visibleGroups = computed<SettingsGroup[]>(() =>
    this.groups
      .map((g) => ({ ...g, cards: g.cards.filter((c) => this.isOwner() || !c.ownerOnly) }))
      .filter((g) => g.cards.length > 0),
  );

  /** Stare identyfikatory zakładek → nowe trasy potomne (dla zewnętrznych/utrwalonych linków). */
  private static readonly LEGACY_TAB_ROUTES: Record<string, string> = {
    theme: 'appearance',
  };

  constructor() {
    // Kompatybilność wsteczna: `?tab=<id>` (m.in. powrót ze Stripe `?tab=deposits`) → trasa potomna.
    const route = inject(ActivatedRoute);
    const router = inject(Router);
    const requested = route.snapshot.queryParamMap.get('tab');
    if (requested) {
      const target = SettingsPageComponent.LEGACY_TAB_ROUTES[requested] ?? requested;
      const exists = this.visibleGroups().some((g) => g.cards.some((c) => c.route === target));
      if (exists) {
        // Hub renderuje się na pustej trasie potomnej — nawiguj względem rodzica `settings`,
        // żeby `target` trafił na /admin/settings/<target>, a nie zagnieździł się pod pustą trasą.
        void router.navigate([target], {
          relativeTo: route.parent ?? route,
          queryParamsHandling: 'preserve',
        });
      }
    }
  }
}
