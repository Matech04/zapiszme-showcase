using App.Domain.Common;

namespace App.Domain.Aggregates.TenantAggregate;

public class Tenant : Entity, IAggregateRoot
{
  public String Name { get; private set; } = String.Empty;
  public String Slug { get; private set; } = String.Empty;

  /// <summary>
  /// Bazowa domena white-label klienta (np. <c>"salon-przyklad.pl"</c>); <c>null</c> dla zwykłych
  /// tenantów. Hosty wyprowadzane konwencją: <c>rezerwacja.{CustomDomain}</c> (SPA bookingu) oraz
  /// <c>api.{CustomDomain}</c> (API). Służy do rozwiązania slugu/tenanta z hosta (resolve-host) oraz
  /// autoryzacji wystawienia certyfikatu w Caddy On-Demand TLS. Zawsze znormalizowana (trim + lowercase).
  /// </summary>
  public String? CustomDomain { get; private set; }

  public String TimeZoneId { get; private set; } = "Europe/Warsaw";
  public String Currency { get; private set; } = "PLN";

  /// <summary>
  /// Branża salonu wybrana w kreatorze onboardingu (klucz szablonu z <c>IndustryTemplateCatalog</c>,
  /// np. <c>"nails"</c>, <c>"barber"</c>, <c>"other"</c>). Steruje domyślnym szablonem usług i copy.
  /// <c>null</c> dla salonów sprzed onboardingu / gdy krok pominięto. Znormalizowana (trim + lowercase).
  /// </summary>
  public string? Industry { get; private set; }

  /// <summary>
  /// UTC ukończenia kreatora onboardingu (ostatni krok „Twój link jest gotowy"). <c>null</c> = kreator
  /// w toku lub salon sprzed onboardingu. Steruje twardym guardem panelu: dopóki <c>null</c>, front
  /// kieruje właściciela do <c>/setup</c> zamiast do <c>/admin</c>. Ustawiane raz (idempotentnie).
  /// </summary>
  public DateTime? OnboardingCompletedAt { get; private set; }

  public CustomerVerificationChannel CustomerVerificationChannel { get; private set; } = CustomerVerificationChannel.Phone;

  /// <summary>Grid step in minutes for offered appointment start times (salon-wide).</summary>
  public int AppointmentSlotStepMinutes { get; private set; } = 15;

  /// <summary>
  /// Horyzont rezerwacji online — ile dni naprzód klient może w ogóle zobaczyć i zarezerwować termin.
  ///
  /// Domyślne 120 dni zastępuje wcześniejszą stałą <c>MAX_MONTHS_AHEAD = 3</c>, która żyła wyłącznie
  /// w UI publicznego bookingu (i dało się ją obejść bezpośrednim requestem do API). Uwaga: tamta
  /// reguła sięgała do KOŃCA miesiąca „bieżący + 3", czyli realnie 92–123 dni zależnie od dnia
  /// miesiąca. 120 dni dobrano tak, by wdrożenie nie zawęziło okna istniejącym salonom — 90 dni
  /// odcięłoby rezerwacje, które dziś przechodzą.
  ///
  /// To domyślna reguła krocząca. Pojedynczy miesiąc może ją nadpisać w obie strony przez
  /// <see cref="Aggregates.EmployeeAggregate.MonthPublication"/>.
  /// Dotyczy WYŁĄCZNIE rezerwacji online — personel w panelu wpisuje wizyty bez ograniczenia.
  /// </summary>
  public int BookingHorizonDays { get; private set; } = 120;

  /// <summary>
  /// Czy publiczna rezerwacja wymaga od klienta podania imienia i nazwiska. Domyślnie <c>false</c>
  /// (zachowanie historyczne — gość podaje tylko kanał kontaktu). Gdy <c>true</c>, klient musi
  /// dodatkowo wpisać imię i nazwisko, które trafiają na rekord klienta w CRM.
  /// </summary>
  public bool RequireCustomerName { get; private set; }

  /// <summary>
  /// Czy publiczny formularz rezerwacji pokazuje opcjonalne pole „nick na Instagramie". Domyślnie
  /// <c>false</c>. Pole pozostaje opcjonalne dla klienta — flaga steruje wyłącznie jego widocznością.
  /// Podany nick trafia na rekord klienta w CRM (<see cref="CustomerAggregate.Customer.InstagramNick"/>).
  /// </summary>
  public bool CollectInstagramHandle { get; private set; }

  /// <summary>
  /// Czy publiczny formularz rezerwacji pokazuje opcjonalną sekcję „Inspiracje" (klientka dorzuca
  /// zdjęcia fryzury/paznokci). Domyślnie <c>false</c> (opt-in — salon świadomie włącza funkcję).
  /// Gdy <c>false</c>, web ukrywa picker, a serwer odrzuca upload (defense-in-depth).
  /// </summary>
  public bool CollectInspirationImages { get; private set; }

  /// <summary>
  /// Kolor akcentu publicznego kalendarza rezerwacji w formacie hex <c>#RRGGBB</c>. <c>null</c> =
  /// domyślny motyw zapisz.me. Web wyprowadza z HUE tego koloru całą paletę OKLCH (stała jasność i
  /// chroma per stopień), dzięki czemu motyw pozostaje czytelny dla dowolnego wyboru salonu.
  /// </summary>
  public string? BookingCalendarColorHex { get; private set; }

  /// <summary>Kolor tła strony (kanwy wokół karty) publicznego kalendarza, hex <c>#RRGGBB</c>. <c>null</c> = domyślny.</summary>
  public string? BookingCalendarBackgroundHex { get; private set; }

  /// <summary>Kolor tła karty/paneli publicznego kalendarza, hex <c>#RRGGBB</c>. <c>null</c> = domyślny. Kolor tekstu dobiera web wg jasności.</summary>
  public string? BookingCalendarSurfaceHex { get; private set; }

  /// <summary>Kolor tekstu cen w publicznym kalendarzu, hex <c>#RRGGBB</c>. <c>null</c> = domyślny.</summary>
  public string? BookingCalendarPriceHex { get; private set; }

  /// <summary>
  /// Treść regulaminu salonu prezentowana klientce w publicznym kalendarzu rezerwacji. Klientka musi
  /// zaakceptować go (checkbox), żeby dokończyć rezerwację. <c>null</c>/puste = brak własnego regulaminu
  /// (web pokazuje wtedy domyślny link do regulaminu zapisz.me). Długi tekst (plain text).
  /// </summary>
  public string? TermsOfService { get; private set; }

  /// <summary>
  /// Polityka dostępu do publicznej rezerwacji online: <see cref="BookingAccessPolicy.Open"/>
  /// pozwala na rezerwacje wszystkim, <see cref="BookingAccessPolicy.InviteOnly"/> wymaga, by
  /// klient miał na CRM oznaczenie `IsWhitelisted`.
  /// </summary>
  public BookingAccessPolicy BookingAccessPolicy { get; private set; } = BookingAccessPolicy.Open;

  /// <summary>
  /// Tryb minimalizacji danych: gdy <c>true</c>, salon nie przechowuje historii wizyt. Cykliczny job
  /// (<c>AppointmentHistoryPurgeHostedService</c>) TRWALE usuwa (hard-delete) wizyty w stanie terminalnym
  /// (<see cref="AppointmentAggregate.AppointmentStatus.Completed"/> /
  /// <see cref="AppointmentAggregate.AppointmentStatus.Canceled"/>) ORAZ z datą w przeszłości
  /// (wg <see cref="TimeZoneId"/>). Wizyty bieżące i przyszłe oraz aktywne holdy pozostają nietknięte —
  /// przypomnienia i obsługa działają normalnie. Domyślnie <c>false</c> (zachowanie historyczne: pełna historia).
  /// </summary>
  public bool DoNotRetainAppointmentHistory { get; private set; }

  /// <summary>
  /// Tryb potwierdzania wizyt: <see cref="AppointmentConfirmationMode.Automatic"/> zatwierdza
  /// wizytę natychmiast po weryfikacji OTP; <see cref="AppointmentConfirmationMode.Manual"/>
  /// wymaga ręcznego zatwierdzenia przez personel.
  /// </summary>
  public AppointmentConfirmationMode AppointmentConfirmationMode { get; private set; } = AppointmentConfirmationMode.Automatic;

  /// <summary>
  /// Pozycja wirtualnej sekcji „Bez kategorii" (usługi bez kategorii) w katalogu, w JEDNEJ
  /// sekwencji z realnymi kategoriami (<see cref="ServiceAggregate.ServiceCategory.OrderIndex"/>).
  /// Domyślnie duża stała (<see cref="UncategorizedOrderDefault"/>), więc dopóki nikt nie przesunie
  /// sekcji, ląduje ona NA KOŃCU (kategorie startują z OrderIndex 0). Ustawiana przez
  /// <see cref="SetUncategorizedOrder"/> w ramach unified-reorder.
  /// </summary>
  public int UncategorizedOrderIndex { get; private set; } = UncategorizedOrderDefault;

  /// <summary>Domyślny indeks sekcji „Bez kategorii" — duży, by sekcja była ostatnia bez ręcznego sortu.</summary>
  public const int UncategorizedOrderDefault = 1_000_000;

  /// <summary>Ustawia pozycję sekcji „Bez kategorii" w unified-reorder katalogu (0-based, nieujemna).</summary>
  public void SetUncategorizedOrder(int index)
  {
    Guard.AgainstNegative(index, nameof(index));
    UncategorizedOrderIndex = index;
  }

  public GapFillingSettings? GapFillingSettings { get; private set; }

  /// <summary>
  /// Preferencje powiadomień salonu — które powiadomienia są aktywne. Domyślnie <see cref="NotificationSettings.Defaults"/>
  /// (wszystkie poza potwierdzeniem rezerwacji do klienta i przypomnieniem 2h przed wizytą).
  /// </summary>
  public NotificationSettings NotificationSettings { get; private set; } = NotificationSettings.Defaults();

  /// <summary>
  /// Polityka widoczności kalendarza dla zwykłych pracowników. Default `TeamFull` — nowy salon
  /// startuje w trybie zaufania (employee widzi cały zespół i obsługuje cudze wizyty); właścicielka
  /// zawęża do `OwnCalendarOnly` w ustawieniach. Istniejące salony zachowują wartość z kolumny.
  /// Owner / Manager mają dostęp do wszystkich kalendarzy niezależnie od tego pola.
  /// </summary>
  public StaffCalendarVisibilityPolicy StaffCalendarVisibilityPolicy { get; private set; } = StaffCalendarVisibilityPolicy.TeamFull;

  public Subscription Subscription { get; private set; } = Subscription.StartTrial();

  /// <summary>
  /// Ustawienia zadatku (domyślna kwota + instrument prawny). Owned type, zawsze obecny.
  /// Domyślnie <see cref="DepositSettings.Disabled"/> — funkcja wyłączona.
  /// </summary>
  public DepositSettings DepositSettings { get; private set; } = DepositSettings.Disabled();

  /// <summary>
  /// Konto płatności salonu u operatora (Stripe Connect). Null dopóki salon nie rozpocznie
  /// onboardingu. Środki z zadatków trafiają bezpośrednio tutaj — platforma ich nie trzyma.
  /// </summary>
  public MerchantAccount? MerchantAccount { get; private set; }

  /// <summary>Czy salon może realnie pobierać zadatki: funkcja włączona i konto płatności gotowe.</summary>
  public bool CanAcceptDeposits =>
    DepositSettings.Enabled && MerchantAccount is not null && MerchantAccount.CanAcceptPayments;

  /// <summary>Tworzy/zastępuje powiązanie z kontem płatności operatora (po utworzeniu konta).</summary>
  public void ConnectMerchantAccount(string provider, string accountId) =>
    MerchantAccount = new MerchantAccount(provider, accountId);

  /// <summary>Aktualizuje stan onboardingu konta (po powrocie z onboardingu / webhooku operatora).</summary>
  public void UpdateMerchantAccountStatus(MerchantOnboardingStatus onboardingStatus, bool chargesEnabled)
  {
    if (MerchantAccount is null)
    {
      throw new InvalidOperationException("Brak połączonego konta płatności — nie można zaktualizować statusu.");
    }

    MerchantAccount.UpdateStatus(onboardingStatus, chargesEnabled);
  }

  /// <summary>
  /// Oznacza tenanta utworzonego przez publiczny tryb demo (<c>POST /api/demo/start</c>).
  /// Demo-tenanty są efemeryczne: hard-kasowane przez <c>DemoTenantCleanupHostedService</c> po
  /// TTL, nie wysyłają realnych powiadomień (e-mail / SMS) i nie mają dostępu do płatności.
  /// Domyślnie <c>false</c> — zwykły, "prawdziwy" tenant.
  /// </summary>
  public bool IsDemo { get; private set; }

  /// <summary>
  /// UTC utworzenia demo-tenanta. Null dla zwykłych tenantów. Sterownik TTL cleanupu —
  /// pozwala zmienić okno życia w configu bez zaszywania go w danych.
  /// </summary>
  public DateTime? DemoCreatedAtUtc { get; private set; }

  /// <summary>Oznacza tenanta jako efemeryczne demo. <paramref name="nowUtc"/> ustala punkt startu TTL.</summary>
  public void MarkAsDemo(DateTime nowUtc)
  {
    IsDemo = true;
    DemoCreatedAtUtc = nowUtc;
  }

  /// <summary>
  /// Wstrzymanie rezerwacji przez salon. Gdy <c>true</c>, publiczna rezerwacja online jest zablokowana
  /// (klient nie utworzy holdu ani nie przejdzie OTP — write-flow rzuca <c>BookingPausedException</c>),
  /// a panel pokazuje baner informujący personel, że rezerwacje online są wstrzymane.
  /// Operacyjny przełącznik salonu (np. na czas zmian w grafiku). Domyślnie <c>false</c>.
  /// </summary>
  public bool BookingPaused { get; private set; }

  /// <summary>
  /// Opcjonalny komunikat wstrzymania rezerwacji pokazywany klientom na publicznej stronie rezerwacji
  /// (gdy <see cref="BookingPaused"/>). <c>null</c> = użyj domyślnego tekstu. Max
  /// <see cref="BookingPauseMessageMaxLength"/> znaków.
  /// </summary>
  public string? BookingPauseMessage { get; private set; }

  /// <summary>Maksymalna długość komunikatu wstrzymania rezerwacji.</summary>
  public const int BookingPauseMessageMaxLength = 280;

  /// <summary>
  /// Włącza/wyłącza wstrzymanie rezerwacji i ustawia opcjonalny komunikat dla klientów. Pusty/whitespace
  /// komunikat → <c>null</c> (domyślny tekst). Przy wyłączeniu komunikat jest czyszczony. Komunikat
  /// jest defensywnie przycinany do <see cref="BookingPauseMessageMaxLength"/> (twarda walidacja długości
  /// żyje w walidatorze komendy).
  /// </summary>
  public void SetBookingPause(bool paused, string? message = null)
  {
    BookingPaused = paused;

    if (!paused)
    {
      BookingPauseMessage = null;
      return;
    }

    var trimmed = message?.Trim();
    if (string.IsNullOrEmpty(trimmed))
    {
      BookingPauseMessage = null;
      return;
    }

    BookingPauseMessage = trimmed.Length > BookingPauseMessageMaxLength
        ? trimmed[..BookingPauseMessageMaxLength]
        : trimmed;
  }

  /// <summary>Ustawia branżę salonu (klucz szablonu z katalogu). Pusty/whitespace → <c>null</c>; trim + lowercase.</summary>
  public void SetIndustry(string? industry) =>
      Industry = string.IsNullOrWhiteSpace(industry) ? null : industry.Trim().ToLowerInvariant();

  /// <summary>
  /// Oznacza ukończenie kreatora onboardingu. Idempotentne — pierwszy znacznik wygrywa, kolejne wywołania
  /// są ignorowane (ponowne przejście kreatora nie „odświeża" daty).
  /// </summary>
  public void MarkOnboardingCompleted(DateTime nowUtc)
  {
    OnboardingCompletedAt ??= nowUtc.ToUniversalTime();
  }

  public void StartTrial(int days = Subscription.DefaultTrialDays) =>
      Subscription = Subscription.StartTrial(days);

  public void SetSubscription(Subscription subscription) =>
      Subscription = subscription;

  /// <summary>
  /// Ustawia/czyści bazową domenę white-label (onboarding klienta na własnej domenie).
  /// Normalizacja: trim + lowercase; pusta/whitespace → <c>null</c> (wyłącza white-label dla tenanta).
  /// </summary>
  public void SetCustomDomain(string? customDomain) =>
      CustomDomain = string.IsNullOrWhiteSpace(customDomain)
          ? null
          : customDomain.Trim().ToLowerInvariant();

  public Tenant(string name, string slug, string timeZoneId = "Europe/Warsaw", string currency = "PLN")
  {
    Id = Guid.NewGuid();
    Update(name, slug, null, null, timeZoneId, currency);
  }

  private Tenant() { }

  public void Update(
    string name,
    string slug,
    CustomerVerificationChannel? customerVerificationChannel = null,
    int? appointmentSlotStepMinutes = null,
    string? timeZoneId = null,
    string? currency = null,
    BookingAccessPolicy? bookingAccessPolicy = null,
    AppointmentConfirmationMode? appointmentConfirmationMode = null,
    GapFillingSettings? gapFillingSettings = null,
    NotificationSettings? notificationSettings = null,
    StaffCalendarVisibilityPolicy? staffCalendarVisibilityPolicy = null,
    bool? requireCustomerName = null,
    bool? collectInstagramHandle = null,
    bool? collectInspirationImages = null,
    DepositSettings? depositSettings = null,
    string? bookingCalendarColorHex = null,
    string? bookingCalendarBackgroundHex = null,
    string? bookingCalendarSurfaceHex = null,
    string? bookingCalendarPriceHex = null,
    string? termsOfService = null,
    bool? doNotRetainAppointmentHistory = null,
    int? bookingHorizonDays = null)
  {
    Name = Guard.NormalizeRequiredText(name, nameof(name));
    Slug = Guard.ReplaceSpaces(slug);
    if (customerVerificationChannel.HasValue)
    {
      CustomerVerificationChannel = customerVerificationChannel.Value;
    }

    if (appointmentSlotStepMinutes.HasValue)
    {
      Guard.AgainstInvalidAppointmentSlotStepMinutes(appointmentSlotStepMinutes.Value);
      AppointmentSlotStepMinutes = appointmentSlotStepMinutes.Value;
    }

    if (bookingHorizonDays.HasValue)
    {
      Guard.AgainstInvalidBookingHorizonDays(bookingHorizonDays.Value);
      BookingHorizonDays = bookingHorizonDays.Value;
    }

    if (!string.IsNullOrWhiteSpace(timeZoneId))
    {
      var normalizedTimeZoneId = Guard.NormalizeRequiredText(timeZoneId, nameof(timeZoneId));
      _ = TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZoneId);
      TimeZoneId = normalizedTimeZoneId;
    }

    if (!string.IsNullOrWhiteSpace(currency))
    {
      Currency = Guard.NormalizeRequiredText(currency, nameof(currency)).ToUpperInvariant();
    }

    if (bookingAccessPolicy.HasValue)
    {
      BookingAccessPolicy = bookingAccessPolicy.Value;
    }

    if (appointmentConfirmationMode.HasValue)
    {
      AppointmentConfirmationMode = appointmentConfirmationMode.Value;
    }

    if (gapFillingSettings != null)
    {
      GapFillingSettings = gapFillingSettings;
    }

    if (notificationSettings != null)
    {
      NotificationSettings = notificationSettings;
    }

    if (staffCalendarVisibilityPolicy.HasValue)
    {
      StaffCalendarVisibilityPolicy = staffCalendarVisibilityPolicy.Value;
    }

    if (requireCustomerName.HasValue)
    {
      RequireCustomerName = requireCustomerName.Value;
    }

    if (collectInstagramHandle.HasValue)
    {
      CollectInstagramHandle = collectInstagramHandle.Value;
    }

    if (collectInspirationImages.HasValue)
    {
      CollectInspirationImages = collectInspirationImages.Value;
    }

    if (depositSettings != null)
    {
      DepositSettings = depositSettings;
    }

    // null = brak zmiany (konwencja Update); "" = wyczyść do motywu domyślnego; "#RRGGBB" = ustaw.
    static string? NormalizeHex(string raw)
    {
      var hex = raw.Trim();
      return hex.Length == 0 ? null : hex.ToUpperInvariant();
    }
    if (bookingCalendarColorHex != null)
    {
      BookingCalendarColorHex = NormalizeHex(bookingCalendarColorHex);
    }
    if (bookingCalendarBackgroundHex != null)
    {
      BookingCalendarBackgroundHex = NormalizeHex(bookingCalendarBackgroundHex);
    }
    if (bookingCalendarSurfaceHex != null)
    {
      BookingCalendarSurfaceHex = NormalizeHex(bookingCalendarSurfaceHex);
    }
    if (bookingCalendarPriceHex != null)
    {
      BookingCalendarPriceHex = NormalizeHex(bookingCalendarPriceHex);
    }

    // null = brak zmiany (konwencja Update); pusty/whitespace = wyczyść regulamin do null; inaczej trim.
    if (termsOfService != null)
    {
      var trimmed = termsOfService.Trim();
      TermsOfService = trimmed.Length == 0 ? null : trimmed;
    }

    if (doNotRetainAppointmentHistory.HasValue)
    {
      DoNotRetainAppointmentHistory = doNotRetainAppointmentHistory.Value;
    }
  }
}