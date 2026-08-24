import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { rxResource, takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { form, maxLength, pattern, required, submit, validate } from '@angular/forms/signals';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  EMPTY,
  lastValueFrom,
  map,
  Observable,
  switchMap,
} from 'rxjs';
import { MessageService } from 'primeng/api';
import { type SelectOption } from '@shared/ui/forms/form-field-select.component';
import {
  AppointmentConfirmationMode,
  BookingAccessPolicy,
  CustomerVerificationChannel,
  GapFillingMode,
  SalonSettingsClient,
  StaffCalendarVisibilityPolicy,
  TenantDto,
} from '@core/api/api-client';
import { DashboardThemeService } from '@core/theme/dashboard-theme.service';
import { DASHBOARD_DEFAULT_PRIMARY_HEX } from '@core/theme/dashboard-primary-palette';
import { BookingPauseStore } from '@core/services/booking-pause.store';

/** Ten sam wzorzec co slug w API (`RegisterOwnerRequest` / walidacja FluentValidation). */
const SALON_SETTINGS_SLUG_PATTERN = /^[a-zA-Z0-9-]+$/;

/** Maksymalna długość regulaminu — spójna z walidatorem backendu (ValidationLimits.TermsOfService). */
export const TERMS_OF_SERVICE_MAX = 5000;

export interface SalonFormModel {
  name: string;
  slug: string;
  customerVerificationChannel: CustomerVerificationChannel;
  /** Wartość z `<p-select>` — minuty jako string (5, 10, 15, 30). */
  appointmentSlotStepMinutes: string;
  /** Ile dni naprzód klient może rezerwować online. Jako string — pole liczbowe formularza. */
  bookingHorizonDays: string;
  timeZoneId: string;
  currency: string;
  /** "open" | "invite_only" — przekładane na BookingAccessPolicy enum przy zapisie. */
  bookingAccessPolicy: 'open' | 'invite_only';
  /** "automatic" | "manual" — przekładane na AppointmentConfirmationMode enum przy zapisie. */
  appointmentConfirmationMode: 'automatic' | 'manual';
  /** "own" | "team_read" | "team_full" — widoczność kalendarza dla zwykłych pracowników. */
  staffCalendarVisibilityPolicy: 'own' | 'team_read' | 'team_full';
  /** Czy publiczna rezerwacja wymaga od klienta podania imienia i nazwiska. */
  requireCustomerName: boolean;
  /** Czy publiczny formularz pokazuje opcjonalne pole „nick na Instagramie". */
  collectInstagramHandle: boolean;
  /** Czy publiczny formularz pokazuje opcjonalną sekcję „Inspiracje" (zdjęcia od klientki). */
  collectInspirationImages: boolean;
  /** Kolor akcentu publicznego kalendarza rezerwacji (hex #RRGGBB). '' = motyw domyślny. */
  bookingCalendarColorHex: string;
  /** Tło strony (kanwa) publicznego kalendarza. '' = domyślny. */
  bookingCalendarBackgroundHex: string;
  /** Tło karty/paneli publicznego kalendarza. '' = domyślny. */
  bookingCalendarSurfaceHex: string;
  /** Kolor tekstu cen w publicznym kalendarzu. '' = domyślny. */
  bookingCalendarPriceHex: string;
  /** Treść regulaminu salonu pokazywana klientce w publicznej rezerwacji. '' = brak (domyślny regulamin zapisz.me). */
  termsOfService: string;
  /** Tryb minimalizacji danych: gdy true, zakończone/anulowane wizyty z przeszłości są trwale usuwane. */
  doNotRetainAppointmentHistory: boolean;
}

/** Pola motywu kalendarza w modelu formularza (do generycznych setterów). */
export type BookingThemeField =
  | 'bookingCalendarColorHex'
  | 'bookingCalendarBackgroundHex'
  | 'bookingCalendarSurfaceHex'
  | 'bookingCalendarPriceHex';

function policyToModel(p: StaffCalendarVisibilityPolicy | undefined): 'own' | 'team_read' | 'team_full' {
  switch (p) {
    case StaffCalendarVisibilityPolicy.TeamFull:
      return 'team_full';
    case StaffCalendarVisibilityPolicy.TeamReadOnly:
      return 'team_read';
    default:
      return 'own';
  }
}

function modelToPolicy(m: 'own' | 'team_read' | 'team_full'): StaffCalendarVisibilityPolicy {
  switch (m) {
    case 'team_full':
      return StaffCalendarVisibilityPolicy.TeamFull;
    case 'team_read':
      return StaffCalendarVisibilityPolicy.TeamReadOnly;
    default:
      return StaffCalendarVisibilityPolicy.OwnCalendarOnly;
  }
}

/**
 * Wspólny stan ustawień salonu współdzielony przez pod-strony Ustawień (Dane salonu / Zasady rezerwacji /
 * Dane przy rezerwacji / Wygląd / Prywatność). Dostarczany na trasie-rodzicu `settings`, więc jest jedną
 * instancją dla całej sekcji.
 *
 * Kluczowy powód istnienia: `SalonSettingsClient.put()` przyjmuje KOMPLETNY `TenantDto`. Rozbicie
 * mega-formularza na pod-strony z osobnymi zapisami zerowałoby pominięte pola — dlatego store trzyma pełny
 * model i wystawia jeden `save()`, który zawsze PUT-uje całość.
 */
@Injectable()
export class SalonSettingsStore {
  private readonly salonClient = inject(SalonSettingsClient);
  private readonly messages = inject(MessageService);
  private readonly dashboardTheme = inject(DashboardThemeService);
  private readonly bookingPauseStore = inject(BookingPauseStore);

  readonly TERMS_OF_SERVICE_MAX = TERMS_OF_SERVICE_MAX;
  readonly CustomerVerificationChannel = CustomerVerificationChannel;
  readonly GapFillingMode = GapFillingMode;

  /** Inline potwierdzenie + stan jednorazowego, trwałego usunięcia istniejącej historii wizyt. */
  readonly purgeConfirming = signal(false);
  readonly purgingHistory = signal(false);

  /** Ostatni wynik sprawdzenia sluga — bez ponownego HTTP dla tego samego ciągu. */
  private readonly slugAvailabilityCache = new Map<string, boolean>();
  private readonly slugAsyncState = signal<'idle' | 'loading' | 'ok' | 'taken' | 'networkError'>('idle');
  private readonly slugAsyncFor = signal<string | null>(null);

  readonly timeZoneOptions: SelectOption[] = SalonSettingsStore.buildTimeZoneOptions();

  /** Presetowane waluty + sentinel "OTHER" przełączający na pole wpisywane ręcznie. */
  readonly currencyOptions: SelectOption[] = [
    { label: 'PLN — Złoty polski', value: 'PLN' },
    { label: 'EUR — Euro', value: 'EUR' },
    { label: 'USD — Dolar amerykański', value: 'USD' },
    { label: 'GBP — Funt brytyjski', value: 'GBP' },
    { label: 'CHF — Frank szwajcarski', value: 'CHF' },
    { label: 'CZK — Korona czeska', value: 'CZK' },
    { label: 'Inna…', value: 'OTHER' },
  ];

  readonly currencyChoice = signal<string>('PLN');
  readonly customCurrency = signal<string>('');
  readonly isCustomCurrency = computed(() => this.currencyChoice() === 'OTHER');

  readonly gapFillingMode = signal<GapFillingMode>(GapFillingMode.Disabled);
  gapFillingBuffer = 0;
  gapFillingLookahead = 1;

  /**
   * Wstrzymanie rezerwacji — operacyjny przełącznik niezależny od głównego „Zapisz" formularza.
   * Zapis jest natychmiastowy (dedykowany endpoint), bo salon włącza go ad hoc (np. na czas zmian
   * w grafiku), a baner w panelu pozwala go szybko wznowić.
   */
  readonly bookingPaused = signal(false);
  bookingPauseMessage = '';
  readonly bookingPauseSaving = signal(false);
  /** Maks. długość komunikatu — spójna z `Tenant.BookingPauseMessageMaxLength` w backendzie. */
  readonly bookingPauseMessageMaxLength = 280;

  /** Aktualny wybór w pickerze koloru dashboardu (może różnić się od zapisanego do kliknięcia „Zastosuj”). */
  readonly pickHex = signal(this.dashboardTheme.pickerSeedHex());

  readonly accentPresets = [
    { label: 'Złoty', hex: DASHBOARD_DEFAULT_PRIMARY_HEX },
    { label: 'Granat', hex: '#1d4ed8' },
    { label: 'Bordowy', hex: '#be123c' },
    { label: 'Leśny', hex: '#15803d' },
    { label: 'Fiolet', hex: '#7c3aed' },
    { label: 'Morski', hex: '#0d9488' },
  ] as const;

  /** Presety koloru publicznego kalendarza rezerwacji (zapisywane na tenancie). */
  readonly bookingColorPresets = [
    { label: 'Fuksja', hex: '#DB2777' },
    { label: 'Śliwka', hex: '#7C3AED' },
    { label: 'Indygo', hex: '#4F46E5' },
    { label: 'Morski', hex: '#0D9488' },
    { label: 'Szmaragd', hex: '#059669' },
    { label: 'Bursztyn', hex: '#D97706' },
  ] as const;

  /** Dodatkowe kolory motywu kalendarza (tło/surface/cena) — picker per pole. */
  readonly bookingThemeFields: ReadonlyArray<{
    field: BookingThemeField;
    label: string;
    fallback: string;
    hint: string;
  }> = [
    { field: 'bookingCalendarBackgroundHex', label: 'Tło strony', fallback: '#F8F3EC', hint: 'Kanwa wokół karty.' },
    {
      field: 'bookingCalendarSurfaceHex',
      label: 'Tło karty',
      fallback: '#FFFFFF',
      hint: 'Tło karty i paneli; kolor tekstu dobierze się automatycznie pod kontrast.',
    },
    { field: 'bookingCalendarPriceHex', label: 'Kolor ceny', fallback: '#0F766E', hint: 'Tekst cen usług.' },
  ];

  readonly settings = rxResource<TenantDto | undefined, undefined>({
    stream: () => this.salonClient.get(),
    defaultValue: undefined,
  });

  readonly salonModel = signal<SalonFormModel>({
    name: '',
    slug: '',
    customerVerificationChannel: CustomerVerificationChannel.Phone,
    appointmentSlotStepMinutes: '15',
    bookingHorizonDays: '120',
    timeZoneId: 'Europe/Warsaw',
    currency: 'PLN',
    bookingAccessPolicy: 'open',
    appointmentConfirmationMode: 'automatic',
    staffCalendarVisibilityPolicy: 'own',
    requireCustomerName: false,
    collectInstagramHandle: false,
    collectInspirationImages: false,
    bookingCalendarColorHex: '',
    bookingCalendarBackgroundHex: '',
    bookingCalendarSurfaceHex: '',
    bookingCalendarPriceHex: '',
    termsOfService: '',
    doNotRetainAppointmentHistory: false,
  });

  readonly salonForm = form(this.salonModel, (schemaPath) => {
    required(schemaPath.name, { message: 'Nazwa jest wymagana' });
    required(schemaPath.slug, { message: 'Slug jest wymagany' });
    maxLength(schemaPath.slug, 100, { message: 'Maksymalnie 100 znaków' });
    pattern(schemaPath.slug, SALON_SETTINGS_SLUG_PATTERN, {
      message: 'Slug może zawierać litery, cyfry i myślniki',
    });
    validate(schemaPath.slug, () => this.slugAvailabilityClientErrors());
    required(schemaPath.timeZoneId, { message: 'Strefa czasowa jest wymagana' });
    required(schemaPath.currency, { message: 'Waluta jest wymagana' });
    maxLength(schemaPath.termsOfService, TERMS_OF_SERVICE_MAX, {
      message: `Maksymalnie ${TERMS_OF_SERVICE_MAX} znaków`,
    });
    validate(schemaPath.bookingHorizonDays, (ctx) => {
      const n = Number.parseInt(String(ctx.value() ?? ''), 10);
      if (!Number.isInteger(n) || n < 1 || n > 1826) {
        return [{ kind: 'horizonRange', message: 'Horyzont musi być liczbą całkowitą od 1 do 1826 dni.' }];
      }
      return [];
    });
    validate(schemaPath.appointmentSlotStepMinutes, (ctx) => {
      const n = Number.parseInt(String(ctx.value() ?? ''), 10);
      if (!Number.isInteger(n) || n < 1 || n > 240) {
        return [{ kind: 'slotStepRange', message: 'Interwał musi być liczbą całkowitą od 1 do 240 minut.' }];
      }
      return [];
    });
  });

  readonly salonSlugTrimmed = computed(() => this.salonModel().slug.trim());

  readonly salonSlugFormatOk = computed(() => {
    const s = this.salonSlugTrimmed();
    return !!s && s.length <= 100 && SALON_SETTINGS_SLUG_PATTERN.test(s);
  });

  /** Dopóki slug ma poprawny format, wymagamy zakończenia sprawdzenia dostępności (bez spamowania API). */
  readonly slugCheckBlocksSubmit = computed(() => {
    if (!this.salonSlugFormatOk()) {
      return false;
    }
    const slug = this.salonSlugTrimmed();
    const st = this.slugAsyncState();
    const forSlug = this.slugAsyncFor();
    if (st === 'loading') {
      return true;
    }
    if (forSlug !== slug) {
      return true;
    }
    if (st === 'idle') {
      return true;
    }
    return false;
  });

  constructor() {
    effect(() => {
      const dto = this.settings.value();
      if (dto) {
        untracked(() => {
          const step = dto.appointmentSlotStepMinutes;
          const stepStr = step != null && step >= 1 && step <= 240 ? String(step) : '15';
          const tz = dto.timeZoneId?.trim() || 'Europe/Warsaw';
          const ccy = (dto.currency?.trim() || 'PLN').toUpperCase();
          this.salonModel.set({
            name: dto.name ?? '',
            slug: dto.slug ?? '',
            customerVerificationChannel:
              dto.customerVerificationChannel ?? CustomerVerificationChannel.Phone,
            appointmentSlotStepMinutes: stepStr,
            bookingHorizonDays: String(dto.bookingHorizonDays ?? 120),
            timeZoneId: tz,
            currency: ccy,
            bookingAccessPolicy:
              dto.bookingAccessPolicy === BookingAccessPolicy.InviteOnly ? 'invite_only' : 'open',
            appointmentConfirmationMode:
              dto.appointmentConfirmationMode === AppointmentConfirmationMode.Manual ? 'manual' : 'automatic',
            staffCalendarVisibilityPolicy: policyToModel(dto.staffCalendarVisibilityPolicy),
            requireCustomerName: dto.requireCustomerName ?? false,
            collectInstagramHandle: dto.collectInstagramHandle ?? false,
            collectInspirationImages: dto.collectInspirationImages ?? false,
            bookingCalendarColorHex: dto.bookingCalendarColorHex ?? '',
            bookingCalendarBackgroundHex: dto.bookingCalendarBackgroundHex ?? '',
            bookingCalendarSurfaceHex: dto.bookingCalendarSurfaceHex ?? '',
            bookingCalendarPriceHex: dto.bookingCalendarPriceHex ?? '',
            termsOfService: dto.termsOfService ?? '',
            doNotRetainAppointmentHistory: dto.doNotRetainAppointmentHistory ?? false,
          });
          const presetCurrencies = ['PLN', 'EUR', 'USD', 'GBP', 'CHF', 'CZK'];
          if (presetCurrencies.includes(ccy)) {
            this.currencyChoice.set(ccy);
            this.customCurrency.set('');
          } else {
            this.currencyChoice.set('OTHER');
            this.customCurrency.set(ccy);
          }
          const gf = dto.gapFillingSettings;
          this.gapFillingMode.set(gf?.mode ?? GapFillingMode.Disabled);
          this.gapFillingBuffer = gf?.bufferMinutes ?? 0;
          this.gapFillingLookahead = gf?.lookaheadSlots ?? 1;
          this.bookingPaused.set(dto.bookingPaused ?? false);
          this.bookingPauseMessage = dto.bookingPauseMessage ?? '';
        });
      }
    });

    toObservable(this.salonModel)
      .pipe(
        map((m) => m.slug.trim()),
        debounceTime(450),
        distinctUntilChanged(),
        switchMap((slug) => this.resolveSlugAvailabilityAfterDebounce(slug)),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  // --- Waluta / strefa czasowa ---

  onCurrencyChoiceChange(value: string | null): void {
    const v = (value ?? 'PLN').toUpperCase();
    this.currencyChoice.set(v);
    if (v !== 'OTHER') {
      this.customCurrency.set('');
      this.salonModel.update((m) => ({ ...m, currency: v }));
    } else {
      this.salonModel.update((m) => ({ ...m, currency: this.customCurrency() }));
    }
  }

  onCustomCurrencyInput(ev: Event): void {
    const raw = (ev.target as HTMLInputElement).value;
    const upper = raw.toUpperCase().replace(/[^A-Z]/g, '').slice(0, 3);
    this.customCurrency.set(upper);
    if (this.currencyChoice() === 'OTHER') {
      this.salonModel.update((m) => ({ ...m, currency: upper }));
    }
  }

  onTimeZoneChange(value: string | null): void {
    const v = value ?? '';
    this.salonModel.update((m) => ({ ...m, timeZoneId: v || 'Europe/Warsaw' }));
  }

  // --- Settery pól „wyboru" (używane przez <app-setting-choice>) ---

  setBookingAccessPolicy(value: unknown): void {
    this.salonModel.update((m) => ({ ...m, bookingAccessPolicy: value as 'open' | 'invite_only' }));
  }

  setAppointmentConfirmationMode(value: unknown): void {
    this.salonModel.update((m) => ({ ...m, appointmentConfirmationMode: value as 'automatic' | 'manual' }));
  }

  setVerificationChannel(value: unknown): void {
    this.salonModel.update((m) => ({ ...m, customerVerificationChannel: value as CustomerVerificationChannel }));
  }

  setRequireCustomerName(value: unknown): void {
    this.salonModel.update((m) => ({ ...m, requireCustomerName: value as boolean }));
  }

  setCollectInspirationImages(value: unknown): void {
    this.salonModel.update((m) => ({ ...m, collectInspirationImages: value as boolean }));
  }

  setCollectInstagramHandle(value: unknown): void {
    this.salonModel.update((m) => ({ ...m, collectInstagramHandle: value as boolean }));
  }

  setStaffCalendarVisibilityPolicy(value: unknown): void {
    this.salonModel.update((m) => ({
      ...m,
      staffCalendarVisibilityPolicy: value as 'own' | 'team_read' | 'team_full',
    }));
  }

  setDoNotRetainAppointmentHistory(value: unknown): void {
    const v = value as boolean;
    this.salonModel.update((m) => ({ ...m, doNotRetainAppointmentHistory: v }));
    // Schowaj inline-potwierdzenie czyszczenia, gdy owner wraca do „przechowuj".
    if (!v) {
      this.purgeConfirming.set(false);
    }
  }

  setGapFillingMode(value: unknown): void {
    this.gapFillingMode.set(value as GapFillingMode);
  }

  // --- Kolory publicznego kalendarza ---

  onBookingColorInput(ev: Event): void {
    this.setBookingCalendarColor((ev.target as HTMLInputElement).value);
  }

  setBookingCalendarColor(value: string): void {
    this.salonModel.update((m) => ({ ...m, bookingCalendarColorHex: value }));
  }

  onThemeColorInput(field: BookingThemeField, ev: Event): void {
    this.setThemeColor(field, (ev.target as HTMLInputElement).value);
  }

  setThemeColor(field: BookingThemeField, value: string): void {
    this.salonModel.update((m) => ({ ...m, [field]: value }));
  }

  // --- Kolor akcentu dashboardu (localStorage, niezależny od zapisu formularza) ---

  onAccentColorInput(ev: Event): void {
    this.pickHex.set((ev.target as HTMLInputElement).value);
  }

  applyAccentColor(): void {
    if (this.dashboardTheme.setAndPersistPrimaryHex(this.pickHex())) {
      this.messages.add({
        severity: 'success',
        summary: 'Kolor zapisany',
        detail: 'Akcent dashboardu został zaktualizowany w tej przeglądarce.',
        life: 4_000,
      });
    } else {
      this.messages.add({
        severity: 'warn',
        summary: 'Nieprawidłowy kolor',
        detail: 'Wybierz kolor z palety lub zestawu.',
        life: 4_000,
      });
    }
  }

  resetAccentColor(): void {
    this.dashboardTheme.clearCustomPrimary();
    this.pickHex.set(DASHBOARD_DEFAULT_PRIMARY_HEX);
    this.messages.add({
      severity: 'info',
      summary: 'Przywrócono',
      detail: 'Używany jest domyślny motyw aplikacji.',
      life: 4_000,
    });
  }

  applyPresetAccent(hex: string): void {
    this.pickHex.set(hex);
    if (this.dashboardTheme.setAndPersistPrimaryHex(hex)) {
      this.messages.add({
        severity: 'success',
        summary: 'Kolor zapisany',
        detail: 'Zastosowano wybrany zestaw kolorów.',
        life: 3_000,
      });
    }
  }

  // --- Wstrzymanie rezerwacji ---

  onBookingPauseMessageInput(ev: Event): void {
    this.bookingPauseMessage = (ev.target as HTMLTextAreaElement).value;
  }

  /** Wstrzymuje/wznawia rezerwacje natychmiast (dedykowany endpoint, niezależny od „Zapisz"). */
  async toggleBookingPause(paused: boolean): Promise<void> {
    if (this.bookingPauseSaving()) return;
    await this.persistBookingPause(paused, paused ? this.bookingPauseMessage.trim() : undefined);
  }

  /** Zapisuje sam komunikat (gdy rezerwacje są już wstrzymane). */
  async saveBookingPauseMessage(): Promise<void> {
    if (this.bookingPauseSaving()) return;
    await this.persistBookingPause(true, this.bookingPauseMessage.trim());
  }

  private async persistBookingPause(paused: boolean, message: string | undefined): Promise<void> {
    this.bookingPauseSaving.set(true);
    try {
      await lastValueFrom(this.salonClient.setBookingPause({ paused, message }));
      this.bookingPaused.set(paused);
      this.bookingPauseStore.set(paused);
      if (!paused) {
        this.bookingPauseMessage = '';
      }
      this.messages.add({
        severity: 'success',
        summary: paused ? 'Rezerwacje wstrzymane' : 'Rezerwacje wznowione',
        detail: paused
          ? 'Rezerwacje online są teraz zablokowane dla klientów.'
          : 'Rezerwacje online znów są dostępne.',
        life: 4_000,
      });
    } catch {
      this.messages.add({
        severity: 'error',
        summary: 'Błąd zapisu',
        detail: 'Nie udało się zmienić stanu rezerwacji. Spróbuj ponownie.',
        life: 5_000,
      });
    } finally {
      this.bookingPauseSaving.set(false);
    }
  }

  // --- Historia wizyt (purge) ---

  /**
   * Jednorazowo, na żądanie ownera, TRWALE usuwa istniejącą historię wizyt (terminalne + przeszłość).
   * Niezależne od zapisu formularza — to akcja natychmiastowa na backendzie. Scenariusz: salon
   * zapisywał historię, a teraz chce wyczyścić to, co już się nazbierało.
   */
  async purgeAppointmentHistory(): Promise<void> {
    this.purgingHistory.set(true);
    try {
      const result = await lastValueFrom(this.salonClient.purgeAppointmentHistory());
      const n = result?.deletedCount ?? 0;
      this.messages.add({
        severity: 'success',
        summary: 'Historia wyczyszczona',
        detail:
          n > 0
            ? `Trwale usunięto ${n} ${this.visitWord(n)} z historii.`
            : 'Brak wizyt do usunięcia — historia była już pusta.',
      });
      this.purgeConfirming.set(false);
    } catch {
      // errorInterceptor pokaże polski toast; tu tylko utrzymujemy spójny stan panelu.
    } finally {
      this.purgingHistory.set(false);
    }
  }

  /** Polska odmiana „wizyta" po liczebniku (1 wizytę / 2-4 wizyty / 5+ wizyt). */
  private visitWord(n: number): string {
    if (n === 1) {
      return 'wizytę';
    }
    const mod10 = n % 10;
    const mod100 = n % 100;
    return mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20) ? 'wizyty' : 'wizyt';
  }

  // --- Zapis całości (pełny PUT TenantDto) ---

  onSave(): Promise<boolean> {
    return submit(this.salonForm, async () => {
      const current = this.salonForm();
      if (current.invalid() || current.pending()) {
        return;
      }
      if (this.slugCheckBlocksSubmit()) {
        this.messages.add({
          severity: 'warn',
          summary: 'Slug',
          detail: 'Poczekaj na zakończenie sprawdzania dostępności adresu albo popraw błędy w polu slug.',
          life: 5_000,
        });
        return;
      }
      const m = current.value() as SalonFormModel;
      const currency = (m.currency.trim() || 'PLN').toUpperCase();
      if (!/^[A-Z]{3}$/.test(currency)) {
        this.messages.add({
          severity: 'warn',
          summary: 'Nieprawidłowa waluta',
          detail: 'Kod waluty musi mieć dokładnie 3 litery (ISO 4217).',
          life: 5_000,
        });
        return;
      }
      try {
        const slotMinutes = Number.parseInt(m.appointmentSlotStepMinutes, 10);
        const horizonDays = Number.parseInt(m.bookingHorizonDays, 10);
        const gfMode = this.gapFillingMode();
        await lastValueFrom(
          this.salonClient.put({
            name: m.name.trim(),
            slug: m.slug.trim(),
            customerVerificationChannel: m.customerVerificationChannel,
            appointmentSlotStepMinutes: Number.isFinite(slotMinutes) ? slotMinutes : 15,
            bookingHorizonDays: Number.isFinite(horizonDays) ? horizonDays : 120,
            timeZoneId: m.timeZoneId.trim() || 'Europe/Warsaw',
            currency,
            bookingAccessPolicy:
              m.bookingAccessPolicy === 'invite_only'
                ? BookingAccessPolicy.InviteOnly
                : BookingAccessPolicy.Open,
            appointmentConfirmationMode:
              m.appointmentConfirmationMode === 'manual'
                ? AppointmentConfirmationMode.Manual
                : AppointmentConfirmationMode.Automatic,
            requireCustomerName: m.requireCustomerName,
            collectInstagramHandle: m.collectInstagramHandle,
            collectInspirationImages: m.collectInspirationImages,
            bookingCalendarColorHex: m.bookingCalendarColorHex.trim(),
            bookingCalendarBackgroundHex: m.bookingCalendarBackgroundHex.trim(),
            bookingCalendarSurfaceHex: m.bookingCalendarSurfaceHex.trim(),
            bookingCalendarPriceHex: m.bookingCalendarPriceHex.trim(),
            gapFillingSettings: {
              mode: gfMode,
              bufferMinutes: Math.max(0, Math.round(this.gapFillingBuffer || 0)),
              lookaheadSlots: Math.max(1, Math.round(this.gapFillingLookahead || 1)),
            },
            staffCalendarVisibilityPolicy: modelToPolicy(m.staffCalendarVisibilityPolicy),
            termsOfService: m.termsOfService.trim(),
            doNotRetainAppointmentHistory: m.doNotRetainAppointmentHistory,
          }),
        );
        this.messages.add({
          severity: 'success',
          summary: 'Zapisano',
          detail: 'Ustawienia salonu zostały zaktualizowane.',
          life: 4_000,
        });
        this.slugAvailabilityCache.clear();
      } catch {
        this.messages.add({
          severity: 'error',
          summary: 'Błąd zapisu',
          detail: 'Nie udało się zapisać ustawień. Sprawdź dane i uprawnienia.',
          life: 5_000,
        });
      }
    });
  }

  // --- Slug availability / strefy czasowe ---

  private slugAvailabilityClientErrors(): { kind: string; message: string }[] {
    const slug = this.salonModel().slug.trim();
    if (!slug || slug.length > 100 || !SALON_SETTINGS_SLUG_PATTERN.test(slug)) {
      return [];
    }
    if (this.slugAsyncFor() !== slug) {
      return [];
    }
    if (this.slugAsyncState() === 'loading') {
      return [];
    }
    if (this.slugAsyncState() === 'taken') {
      return [
        {
          kind: 'slugTaken',
          message: 'Ten slug jest już zajęty przez inny salon. Wybierz inny publiczny adres.',
        },
      ];
    }
    if (this.slugAsyncState() === 'networkError') {
      return [
        {
          kind: 'slugCheckError',
          message: 'Nie udało się sprawdzić dostępności sluga. Spróbuj ponownie.',
        },
      ];
    }
    return [];
  }

  private resolveSlugAvailabilityAfterDebounce(slug: string): Observable<unknown> {
    const valid = !!slug && slug.length <= 100 && SALON_SETTINGS_SLUG_PATTERN.test(slug);
    if (!valid) {
      this.slugAsyncFor.set(null);
      this.slugAsyncState.set('idle');
      return EMPTY;
    }
    const cached = this.slugAvailabilityCache.get(slug);
    if (cached !== undefined) {
      this.slugAsyncFor.set(slug);
      this.slugAsyncState.set(cached ? 'ok' : 'taken');
      return EMPTY;
    }
    this.slugAsyncFor.set(slug);
    this.slugAsyncState.set('loading');
    return this.salonClient.getSlugAvailability(slug).pipe(
      map((dto) => {
        const available = dto.available === true;
        this.slugAvailabilityCache.set(slug, available);
        this.slugAsyncFor.set(slug);
        this.slugAsyncState.set(available ? 'ok' : 'taken');
      }),
      catchError(() => {
        this.slugAsyncFor.set(slug);
        this.slugAsyncState.set('networkError');
        return EMPTY;
      }),
    );
  }

  /** Buduje listę stref IANA z `Intl.supportedValuesOf('timeZone')`, z fallback'iem dla starszych runtime'ów. */
  private static buildTimeZoneOptions(): SelectOption[] {
    const fallback = [
      'UTC',
      'Europe/Warsaw',
      'Europe/Berlin',
      'Europe/London',
      'Europe/Paris',
      'Europe/Madrid',
      'Europe/Rome',
      'Europe/Amsterdam',
      'Europe/Vienna',
      'Europe/Prague',
      'Europe/Stockholm',
      'Europe/Helsinki',
      'Europe/Athens',
      'Europe/Istanbul',
      'America/New_York',
      'America/Chicago',
      'America/Denver',
      'America/Los_Angeles',
      'America/Toronto',
      'America/Sao_Paulo',
      'Asia/Tokyo',
      'Asia/Shanghai',
      'Asia/Singapore',
      'Asia/Dubai',
      'Australia/Sydney',
      'Pacific/Auckland',
    ];
    let zones: string[] = fallback;
    try {
      const intlAny = Intl as unknown as { supportedValuesOf?: (k: string) => string[] };
      const supported = intlAny.supportedValuesOf?.('timeZone');
      if (Array.isArray(supported) && supported.length > 0) {
        zones = supported;
      }
    } catch {
      // ignore — fallback already set
    }
    return [...new Set(zones)]
      .sort((a, b) => a.localeCompare(b))
      .map((z) => ({ label: z, value: z }));
  }
}
