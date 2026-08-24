import { Injectable, signal } from '@angular/core';
import {
  AppointmentConfirmationMode,
  IndustryTemplateDto,
  SlotGenerationMode,
} from '@core/api/api-client';

/** Edytowalna usługa w kroku „Twoje usługi" (prefill z szablonu branżowego). */
export interface DraftService {
  name: string;
  price: number;
  durationMinutes: number;
  selected: boolean;
}

const DRAFT_PREFIX = 'zapisz.onboarding.draft.';

/**
 * Bufor niezatwierdzonego stanu kreatora, współdzielony między krokami (provided na trasie `/setup`).
 *
 * Po utworzeniu tenanta źródłem prawdy jest backend (`OnboardingStateService`) — tu trzymamy tylko
 * dane wpisane, ale jeszcze niewysłane (np. imię/nazwisko przed `completeProfile`, ceny przed
 * `applyIndustry`). Duplikujemy je do `localStorage` (`zapisz.onboarding.draft.<krok>`), żeby przetrwały
 * odświeżenie strony. NIGDY nie zapisujemy tu hasła ani numeru telefonu.
 */
@Injectable()
export class OnboardingWizardStore {
  // Krok profilu (bufor do czasu wywołania completeProfile w kroku „salon").
  readonly firstName = signal('');
  readonly lastName = signal('');

  // Krok salonu.
  readonly salonName = signal('');
  readonly salonSlug = signal('');

  // Krok branży + usług.
  readonly industries = signal<IndustryTemplateDto[]>([]);
  readonly selectedIndustryKey = signal<string | null>(null);
  readonly draftServices = signal<DraftService[]>([]);

  /**
   * Krok „Jak wyznaczasz terminy?" — kształt POJEDYNCZEGO dnia pracy: siatka co 15 min czy stałe
   * godziny startu. Dotyczy zarówno dni z grafiku powtarzalnego, jak i wklikanych ręcznie
   * (`Employee.SlotGenerationMode` jest podpowiedzią startową dla każdego nowego dnia specjalnego
   * — patrz `employeeMode` w `employee-special-days.component.ts`). Dlatego pytamy o to PRZED
   * pytaniem o powtarzalność i niezależnie od odpowiedzi na tamto.
   *
   * Wybór „planuję każdy miesiąc osobno" NIE mieszka tutaj — leży na kroku „Jak układasz grafik?", bo jest
   * używany i wysyłany na tym samym ekranie (nie musi przeżywać przejścia między krokami).
   */
  readonly slotMode = signal<SlotGenerationMode>(SlotGenerationMode.Grid);

  /**
   * Krok „Jak przyjmujesz zapisy?". Trafia do bazy dopiero na końcu, razem z `complete()` z kroku
   * „Jak układasz grafik?" — sam krok zasad nie ma czego zapisać osobno, bo `CompleteOnboardingCommand`
   * ustawia tryb i domyka onboarding jedną komendą, a domknięcie na piątym kroku wyrzuciłoby
   * właścicielkę z kreatora (`setupGuard`). Dlatego wybór czeka tu, z draftem w localStorage.
   */
  readonly confirmationMode = signal<AppointmentConfirmationMode>(
    AppointmentConfirmationMode.Automatic,
  );

  constructor() {
    this.hydrateProfile();
    this.hydrateIndustry();
    this.hydrateSlotMode();
    this.hydrateRules();
  }

  // ── Profil ──
  persistProfile(): void {
    this.writeDraft('profile', { firstName: this.firstName(), lastName: this.lastName() });
  }

  private hydrateProfile(): void {
    const d = this.readDraft<{ firstName?: string; lastName?: string }>('profile');
    if (d) {
      this.firstName.set(d.firstName ?? '');
      this.lastName.set(d.lastName ?? '');
    }
  }

  // ── Branża / usługi ──
  persistIndustry(): void {
    this.writeDraft('industry', {
      key: this.selectedIndustryKey(),
      services: this.draftServices(),
    });
  }

  private hydrateIndustry(): void {
    const d = this.readDraft<{ key?: string | null; services?: DraftService[] }>('industry');
    if (d) {
      this.selectedIndustryKey.set(d.key ?? null);
      this.draftServices.set(d.services ?? []);
    }
  }

  // ── Tryb wyznaczania terminów ──
  persistSlotMode(): void {
    this.writeDraft('slotMode', { slotMode: this.slotMode() });
  }

  private hydrateSlotMode(): void {
    const d = this.readDraft<{ slotMode?: SlotGenerationMode }>('slotMode');
    if (d && d.slotMode != null) {
      this.slotMode.set(d.slotMode);
    }
  }

  // ── Zasady zapisów ──
  persistRules(): void {
    this.writeDraft('rules', { confirmationMode: this.confirmationMode() });
  }

  private hydrateRules(): void {
    const d = this.readDraft<{ confirmationMode?: AppointmentConfirmationMode }>('rules');
    if (d && d.confirmationMode != null) {
      this.confirmationMode.set(d.confirmationMode);
    }
  }

  /** Czyści wszystkie drafty kreatora (po ukończeniu onboardingu). */
  clearDrafts(): void {
    for (const key of ['profile', 'industry', 'slotMode', 'rules']) {
      try {
        globalThis.localStorage?.removeItem(DRAFT_PREFIX + key);
      } catch {
        /* ignore */
      }
    }
  }

  private writeDraft(key: string, value: unknown): void {
    try {
      globalThis.localStorage?.setItem(DRAFT_PREFIX + key, JSON.stringify(value));
    } catch {
      /* ignore — storage może być niedostępny */
    }
  }

  private readDraft<T>(key: string): T | null {
    try {
      const raw = globalThis.localStorage?.getItem(DRAFT_PREFIX + key);
      return raw ? (JSON.parse(raw) as T) : null;
    } catch {
      return null;
    }
  }
}
