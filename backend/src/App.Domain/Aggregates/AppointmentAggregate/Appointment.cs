using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.Aggregates.AppointmentAggregate;

// Status na możliwe typy statusu
// Obliczanie EndTime
// Obliczanie TotalPrice
public class Appointment : Entity, ITenantEntity, IAggregateRoot
{
  /// <summary>Maksymalna liczba usług w jednej wizycie-combo.</summary>
  public const int MaxServices = 5;

  /// <summary>
  /// Twardy limit zdjęć inspiracji na wizytę. Anty-abuse: anonimowy upload to koszt storage'u,
  /// więc trzymamy konserwatywnie. Egzekwowany w agregacie (źródło prawdy) + walidatorze + UI.
  /// </summary>
  public const int MaxInspirationImages = 3;

  private readonly List<AppointmentServiceItem> _items = new();
  private readonly List<AppointmentInspirationImage> _inspirationImages = new();

  /// <summary>Pozycje usług wizyty (combo). Zawsze ≥1; pozycja 0 = usługa „główna" (== <see cref="ServiceId"/>).</summary>
  public IReadOnlyCollection<AppointmentServiceItem> Items => _items;

  /// <summary>Zdjęcia inspiracji dołączone przez klientkę przy publicznej rezerwacji (≤<see cref="MaxInspirationImages"/>).</summary>
  public IReadOnlyCollection<AppointmentInspirationImage> InspirationImages => _inspirationImages;

  public Guid TenantId { get; private set; }
  public Guid EmployeeId { get; private set; }

  /// <summary>Usługa „główna" (primary) = pierwsza pozycja combo. Zachowana zdenormalizowanie dla zgodności wstecznej i prostych zapytań.</summary>
  public Guid ServiceId { get; private set; }
  public Guid? CustomerId { get; private set; }
  public DateOnly Date { get; private set; }
  public TimeOnly StartTime { get; private set; }
  public TimeOnly EndTime { get; private set; }

  /// <summary>
  /// Niestandardowy czas trwania wizyty (w minutach) ustawiony przez personel w panelu — nadpisuje
  /// długość bloku w kalendarzu (<see cref="EndTime"/> = <see cref="StartTime"/> + ta wartość), gdy
  /// jedne klientki obsługuje się krócej/dłużej niż standardowy czas usługi. <c>null</c> = czas
  /// standardowy (suma czasów pozycji). Rezerwacja online NIGDY tego nie ustawia — klientka widzi
  /// zawsze standardowy czas usługi. Skrócenie realnie zwalnia przedział (dostępność liczy z EndTime).
  /// </summary>
  public int? CustomDurationMinutes { get; private set; }
  public AppointmentStatus Status { get; private set; } = null!;
  public Money TotalPrice { get; private set; } = null!;

  /// <summary>
  /// Faktyczna kwota pobrana za wizytę, wpisywana przez pracownika na miejscu / po wizycie.
  /// <c>null</c> = jeszcze nieustalona (np. usługa z widełkami cenowymi). Niezależna od
  /// <see cref="TotalPrice"/>, które jest szacunkiem wyznaczonym przy rezerwacji.
  /// </summary>
  public Money? FinalPrice { get; private set; }
  public string AppointmentNotes { get; private set; } = string.Empty;
  public HoldLease? Lease { get; private set; }
  public OtpVerification? OtpVerification { get; private set; }
  public DateTimeOffset? SelfServiceChangedAt { get; private set; }
  public int? SelfServiceChangeKind { get; private set; }
  public AppointmentSource Source { get; private set; }

  /// <summary>
  /// Identyfikator anonimowej sesji klienta (cookie po stronie przeglądarki) — używany
  /// w publicznym booking-flow do auto-anulowania poprzednich aktywnych holdów,
  /// gdy ten sam użytkownik zaczyna nową rezerwację (np. po F5). Null dla appointmentów
  /// utworzonych z panelu lub przez self-service.
  /// </summary>
  public Guid? AnonSessionId { get; private set; }

  /// <summary>UTC wysłania przypomnienia 24h dla tej wizyty. Null = jeszcze nie wysłano (idempotencja joba).</summary>
  public DateTime? Reminder24hSentAtUtc { get; private set; }

  /// <summary>UTC wysłania przypomnienia ~2h dla tej wizyty. Null = jeszcze nie wysłano.</summary>
  public DateTime? Reminder2hSentAtUtc { get; private set; }

  // --- Zadatek (ortogonalny do Status; patrz AppointmentPaymentStatus) ---

  /// <summary>Kwota zadatku ustalona przy generowaniu linku. Null gdy zadatek nigdy nie był wymagany.</summary>
  public Money? DepositAmount { get; private set; }

  public AppointmentPaymentStatus PaymentStatus { get; private set; } = AppointmentPaymentStatus.NotRequired;

  /// <summary>Identyfikator sesji płatności u operatora (Stripe Checkout session) — korelacja webhooka + idempotencja.</summary>
  public string? PaymentSessionId { get; private set; }

  /// <summary>Adres linku płatności do wysłania klientce (Stripe hosted Checkout URL).</summary>
  public string? PaymentLinkUrl { get; private set; }

  /// <summary>UTC opłacenia zadatku. Null dopóki niezapłacony.</summary>
  public DateTime? PaidAtUtc { get; private set; }

  /// <summary>UTC wygaśnięcia linku płatności (24h od wygenerowania). Po tym czasie link nieważny.</summary>
  public DateTime? LinkExpiresAtUtc { get; private set; }

  /// <summary>
  /// UTC udanego wysłania linku zadatku klientowi. Null = jeszcze nie wysłano (albo wysyłka padła).
  /// Zerowane przy generowaniu nowego linku — stary znacznik nie opisuje nowego linku.
  /// </summary>
  public DateTime? DepositLinkSentAtUtc { get; private set; }

  /// <summary>Kanał ostatniej udanej wysyłki linku (<c>Sms</c> / <c>Email</c>). Null gdy nie wysłano.</summary>
  public string? DepositLinkSentChannel { get; private set; }

  /// <summary>Ile razy w sumie wygenerowano link zadatku dla tej wizyty (aktualny link włącznie).</summary>
  public int DepositLinkAttempts { get; private set; }

  /// <summary>
  /// Ile POPRZEDNICH linków wygasło bez opłaty. Link nadpisany, gdy był jeszcze ważny, NIE liczy się
  /// jako wygasły — personel po prostu wygenerował nowy. Aktualny wygasły link opisuje
  /// <see cref="IsDepositLinkExpired"/>, nie ten licznik.
  /// </summary>
  public int ExpiredDepositLinkCount { get; private set; }

  /// <summary>
  /// Konstruktor single-service (zgodność wsteczna) — endTime/totalPrice są już policzone przez handlera.
  /// Buduje jednoelementowe combo (jedna pozycja). Nowe ścieżki combo używają konstruktora z listą usług.
  /// </summary>
  public Appointment(Guid tenantId, Guid employeeId, Guid serviceId, Guid? customerId, DateOnly date, TimeOnly startTime, TimeOnly endTime, AppointmentStatus status, Money totalPrice, string appointmentNotes, HoldLease? lease, AppointmentSource source = AppointmentSource.Panel)
  {

    Guard.AgainstInvalidTimeRange(startTime, endTime);

    Id = Guid.NewGuid();
    TenantId = tenantId;
    EmployeeId = employeeId;
    ServiceId = serviceId;
    CustomerId = customerId;
    Date = date;
    StartTime = startTime;
    EndTime = endTime;
    Status = status;
    TotalPrice = totalPrice;
    AppointmentNotes = appointmentNotes;
    Lease = lease;
    Source = source;

    var durationMinutes = (int)(endTime - startTime).TotalMinutes;
    _items.Add(new AppointmentServiceItem(tenantId, Id, serviceId, durationMinutes, totalPrice, 0));
  }

  /// <summary>
  /// Konstruktor combo: czas i cena wizyty wynikają z sumy pozycji (każda usługa rozwiązana
  /// wobec pracownika do <see cref="AppointmentServiceLine"/>). Pozycja 0 jest usługą główną.
  /// </summary>
  public Appointment(Guid tenantId, Guid employeeId, Guid? customerId, DateOnly date, TimeOnly startTime, AppointmentStatus status, string appointmentNotes, HoldLease? lease, IReadOnlyList<AppointmentServiceLine> services, AppointmentSource source = AppointmentSource.Panel, int? customDurationMinutes = null)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    EmployeeId = employeeId;
    CustomerId = customerId;
    Date = date;
    Status = status;
    AppointmentNotes = appointmentNotes;
    Lease = lease;
    Source = source;
    // Ustawiamy override PRZED SetServices — ComputeEndTime bierze go pod uwagę. Normalizacja
    // (== standard → null) dzieje się w SetServices po policzeniu sumy pozycji.
    CustomDurationMinutes = NormalizeCustomDuration(customDurationMinutes);
    SetServices(services, startTime);
  }

  private Appointment() { }

  public void Update(Guid employeeId, Guid serviceId, DateOnly date, TimeOnly startTime, TimeOnly endTime, Money totalPrice)
  {
    Guard.AgainstInvalidTimeRange(startTime, endTime);

    EmployeeId = employeeId;
    ServiceId = serviceId;
    Date = date;
    StartTime = startTime;
    EndTime = endTime;
    TotalPrice = totalPrice;

    var durationMinutes = (int)(endTime - startTime).TotalMinutes;
    _items.Clear();
    _items.Add(new AppointmentServiceItem(TenantId, Id, serviceId, durationMinutes, totalPrice, 0));
  }

  /// <summary>
  /// Reschedule combo: zmienia pracownika/datę/godzinę i (opcjonalnie nowy) skład usług.
  /// <paramref name="customDurationMinutes"/>: <c>null</c> = zachowaj bieżący override czasu
  /// (zwykłe przesunięcie terminu nie kasuje niestandardowego bloku); wartość = ustaw nowy.
  /// </summary>
  public void Reschedule(Guid employeeId, DateOnly date, TimeOnly startTime, IReadOnlyList<AppointmentServiceLine> services, int? customDurationMinutes = null)
  {
    EmployeeId = employeeId;
    Date = date;
    if (customDurationMinutes is not null)
    {
      CustomDurationMinutes = NormalizeCustomDuration(customDurationMinutes);
    }
    SetServices(services, startTime);
  }

  /// <summary>
  /// Ustawia skład usług (combo) i przelicza StartTime/EndTime/TotalPrice/ServiceId. Inwarianty:
  /// ≥1, ≤<see cref="MaxServices"/>, unikalne ServiceId. Reguła grup wariantów jest egzekwowana
  /// w warstwie aplikacji (ComboCompositionValidator), bo agregat nie zna grup usług.
  /// </summary>
  public void SetServices(IReadOnlyList<AppointmentServiceLine> services, TimeOnly startTime)
  {
    if (services is null || services.Count == 0)
    {
      throw new AppointmentBookingRuleException("Wizyta musi mieć co najmniej jedną usługę.", ErrorCodes.AppointmentNoServices);
    }

    if (services.Count > MaxServices)
    {
      throw new AppointmentBookingRuleException($"Wizyta może mieć maksymalnie {MaxServices} usług.", ErrorCodes.AppointmentTooManyServices);
    }

    if (services.Select(s => s.ServiceId).Distinct().Count() != services.Count)
    {
      throw new AppointmentBookingRuleException("Usługi w wizycie nie mogą się powtarzać.", ErrorCodes.AppointmentDuplicateService);
    }

    var totalDuration = services.Sum(s => s.DurationMinutes);
    if (totalDuration <= 0)
    {
      // Sama usługa-dodatek (0 min) nie tworzy wizyty — musi być pozycja z czasem trwania.
      throw new AppointmentBookingRuleException(
          "Wizyta musi zawierać usługę z czasem trwania.", ErrorCodes.AppointmentZeroDuration);
    }

    // Override „== standardowa suma" traktujemy jak brak override (czas standardowy) — inaczej
    // zwykłyby się „przyklejone" wartości, gdy personel wpisze dokładnie standard.
    if (CustomDurationMinutes == totalDuration)
    {
      CustomDurationMinutes = null;
    }

    var endTime = ComputeEndTime(startTime, totalDuration);
    Guard.AgainstInvalidTimeRange(startTime, endTime);

    var currency = services[0].Price.Currency;

    StartTime = startTime;
    EndTime = endTime;
    ServiceId = services[0].ServiceId;
    TotalPrice = new Money(services.Sum(s => s.Price.Amount), currency);

    _items.Clear();
    for (var i = 0; i < services.Count; i++)
    {
      _items.Add(new AppointmentServiceItem(TenantId, Id, services[i].ServiceId, services[i].DurationMinutes, services[i].Price, i));
    }
  }

  /// <summary>
  /// Ustawia (lub czyści) niestandardowy czas trwania wizyty — nadpisanie długości bloku przez personel.
  /// <c>null</c> lub wartość równa standardowej sumie czasów pozycji = powrót do czasu standardowego.
  /// Przelicza <see cref="EndTime"/> z bieżącego <see cref="StartTime"/> i pozycji. Rzuca, gdy wartość
  /// jest niedodatnia albo blok „zawija" za północ (<see cref="Guard.AgainstInvalidTimeRange"/>).
  /// Kolizję ze slotem/godzinami pracy sprawdza warstwa aplikacji (AppointmentService).
  /// </summary>
  public void SetCustomDuration(int? minutes)
  {
    var standardSum = _items.Sum(i => i.DurationMinutes);
    var normalized = NormalizeCustomDuration(minutes);
    // Wartość równa standardowi = czas standardowy.
    if (normalized == standardSum)
    {
      normalized = null;
    }

    CustomDurationMinutes = normalized;
    var endTime = ComputeEndTime(StartTime, standardSum);
    Guard.AgainstInvalidTimeRange(StartTime, endTime);
    EndTime = endTime;
  }

  /// <summary>EndTime = StartTime + (override ?? standardowa suma czasów pozycji).</summary>
  private TimeOnly ComputeEndTime(TimeOnly startTime, int standardSumMinutes)
      => startTime.AddMinutes(CustomDurationMinutes ?? standardSumMinutes);

  /// <summary>Górny limit override'u czasu wizyty — spójny z limitem usług (24h). Bez niego absurdalna
  /// wartość (np. 1500 min) zawijała <see cref="TimeOnly.AddMinutes"/> mod 24h: zapisany
  /// <see cref="CustomDurationMinutes"/> rozjeżdżał się z <see cref="EndTime"/>, a część wartości
  /// wpadała w <see cref="Guard.AgainstInvalidTimeRange"/> dając niekontrolowane 500.</summary>
  public const int MaxCustomDurationMinutes = 24 * 60;

  /// <summary>Waliduje override czasu: <c>null</c> przechodzi, wartość musi być dodatnia i ≤ 24h.</summary>
  private static int? NormalizeCustomDuration(int? minutes)
  {
    if (minutes is null)
    {
      return null;
    }

    if (minutes.Value <= 0 || minutes.Value > MaxCustomDurationMinutes)
    {
      throw new AppointmentBookingRuleException(
          "Czas trwania wizyty musi być dodatni i nie może przekraczać 24 godzin.",
          ErrorCodes.AppointmentInvalidDuration);
    }

    return minutes;
  }

  public void ReplaceHoldLease(HoldLease lease)
  {
    Lease = lease;
  }

  /// <summary>
  /// Usuwa dzierżawę slotu (HoldLease) po potwierdzeniu rezerwacji. Dzierżawa to wyłącznie
  /// artefakt lejka rezerwacji (przed potwierdzeniem). Pozostawienie wygasłej dzierżawy na
  /// potwierdzonej wizycie powodowało, że job cyklu życia kasował z bazy potwierdzone wizyty
  /// (status Pending w trybie ręcznego potwierdzania) — patrz VerifyOtpCommand.
  /// </summary>
  public void ClearHoldLease()
  {
    Lease = null;
  }

  /// <summary>Wiąże appointment z anonimową sesją publicznego klienta (do auto-cancel poprzednich holdów).</summary>
  public void SetAnonSession(Guid anonSessionId)
  {
    AnonSessionId = anonSessionId;
  }

  /// <summary>
  /// Ustawia zdjęcia inspiracji wizyty (zastępuje poprzednie). Każda pozycja to już przetworzony
  /// i wgrany obraz (URL główny + miniatura + klucz storage). Egzekwuje twardy cap
  /// <see cref="MaxInspirationImages"/> — agregat jest źródłem prawdy, nawet gdyby front/walidator
  /// został pominięty (anonimowy, niezaufany wektor).
  /// </summary>
  public void SetInspirationImages(IReadOnlyList<AppointmentInspirationLine> images)
  {
    if (images is null || images.Count == 0)
    {
      _inspirationImages.Clear();
      return;
    }

    if (images.Count > MaxInspirationImages)
    {
      throw new AppointmentBookingRuleException(
          $"Można dodać maksymalnie {MaxInspirationImages} zdjęć inspiracji.",
          ErrorCodes.AppointmentTooManyInspirationImages);
    }

    _inspirationImages.Clear();
    foreach (var image in images)
    {
      _inspirationImages.Add(new AppointmentInspirationImage(TenantId, Id, image.Url, image.ThumbnailUrl, image.StorageKey, image.ThumbnailStorageKey));
    }
  }

  /// <summary>
  /// Dokłada POJEDYNCZE, już przetworzone i wgrane zdjęcie inspiracji (deferred-upload: front uploaduje
  /// dopiero po potwierdzeniu OTP, autoryzowany krótkożyjącym tokenem). Inkrementalnie egzekwuje twardy
  /// cap <see cref="MaxInspirationImages"/> — agregat jest źródłem prawdy, każde żądanie uploadu osobno.
  /// </summary>
  public void AddInspirationImage(AppointmentInspirationLine image)
  {
    if (_inspirationImages.Count >= MaxInspirationImages)
    {
      throw new AppointmentBookingRuleException(
          $"Można dodać maksymalnie {MaxInspirationImages} zdjęć inspiracji.",
          ErrorCodes.AppointmentTooManyInspirationImages);
    }

    _inspirationImages.Add(new AppointmentInspirationImage(TenantId, Id, image.Url, image.ThumbnailUrl, image.StorageKey, image.ThumbnailStorageKey));
  }

  /// <summary>
  /// Usuwa wszystkie zdjęcia inspiracji wizyty. Wołane, gdy podgląd przestaje być potrzebny (wizyta
  /// terminalna: Completed/Canceled). Same obiekty w storage kasuje warstwa aplikacji (R2) — agregat
  /// czyści tylko rekordy. Idempotentne.
  /// </summary>
  public void ClearInspirationImages() => _inspirationImages.Clear();

  public void ChangeStatus(AppointmentStatus newStatus)
  {
    // Idempotentny no-op — ustawienie tego samego statusu jest zawsze dozwolone.
    if (Status == newStatus)
    {
      return;
    }

    // Stany terminalne: z Completed i Canceled NIE wychodzimy. Chroni przed „wskrzeszaniem"
    // anulowanych/zakończonych wizyt i — w połączeniu z tym, że Booked osiągalne jest tylko
    // przez zweryfikowaną ścieżkę OTP (VerifyOtp) / panel z nie-terminalnego stanu — domyka
    // obejście maszyny stanów (np. Canceled→Booked z pominięciem OTP). Zgodne z regułami
    // automatycznego lifecycle, które dla Completed/Canceled nie generują już przejść.
    if (Status == AppointmentStatus.Completed)
    {
      throw new AppointmentBookingRuleException(
          "Nie można zmienić statusu wizyty, która została już zakończona.",
          ErrorCodes.AppointmentCompletedCannotBeCanceled);
    }

    if (Status == AppointmentStatus.Canceled)
    {
      throw new AppointmentBookingRuleException(
          "Nie można zmienić statusu anulowanej wizyty.",
          ErrorCodes.AppointmentCompletedCannotBeCanceled);
    }

    Status = newStatus;
  }

  public void ChangeNotes(string newNotes)
  {
    AppointmentNotes = Guard.NormalizeOptionalText(newNotes);
  }

  /// <summary>
  /// Ustawia cenę końcową (faktycznie pobraną) wizyty. Dozwolone dla każdej wizyty poza anulowaną —
  /// w szczególności dla <see cref="AppointmentStatus.Completed"/>, bo dopiero wtedy pracownik zna
  /// kwotę (wizyta domyka się automatycznie po EndTime — zob. AppointmentStatusLifecycleRules).
  /// </summary>
  public void SetFinalPrice(Money finalPrice)
  {
    if (Status == AppointmentStatus.Canceled)
    {
      throw new AppointmentBookingRuleException(
          "Nie można ustawić ceny końcowej dla anulowanej wizyty.",
          ErrorCodes.AppointmentFinalPriceInvalidStatus);
    }

    FinalPrice = finalPrice;
  }

  public void SetOtpVerification(OtpVerification verification)
  {
    OtpVerification = verification;
  }

  /// <summary>
  /// Przypisuje wizytę do klienta. Wykorzystywane np. po publicznej rezerwacji + OTP,
  /// kiedy `CustomerId` był pusty (gość) — po weryfikacji kontakt staje się klientem
  /// w CRM tenanta.
  /// </summary>
  public void AssignCustomer(Guid customerId)
  {
    CustomerId = customerId;
  }

  // kind: 1 = Cancelled, 2 = Rescheduled
  public void RecordSelfServiceChange(int kind)
  {
    SelfServiceChangeKind = kind;
    SelfServiceChangedAt = DateTimeOffset.UtcNow;
  }

  /// <summary>Oznacza, że przypomnienie 24h zostało wysłane — zapobiega ponownej wysyłce przez job.</summary>
  public void MarkReminder24hSent(DateTime nowUtc) => Reminder24hSentAtUtc = nowUtc;

  /// <summary>Oznacza, że przypomnienie ~2h zostało wysłane — zapobiega ponownej wysyłce przez job.</summary>
  public void MarkReminder2hSent(DateTime nowUtc) => Reminder2hSentAtUtc = nowUtc;

  /// <summary>
  /// Zapisuje wygenerowany link zadatku (sesja Checkout u operatora). Regeneracja nadpisuje
  /// poprzedni nieopłacony link. Niedozwolone gdy zadatek już opłacony lub wizyta w stanie terminalnym.
  /// </summary>
  public void GenerateDepositLink(Money amount, string sessionId, string url, DateTime expiresAtUtc, DateTime nowUtc)
  {
    if (PaymentStatus == AppointmentPaymentStatus.Paid)
    {
      throw new AppointmentBookingRuleException(
        "Zadatek za tę wizytę został już opłacony.", ErrorCodes.DepositAlreadyPaid);
    }

    if (Status == AppointmentStatus.Canceled || Status == AppointmentStatus.Completed)
    {
      throw new AppointmentBookingRuleException(
        "Nie można wygenerować zadatku dla zakończonej lub anulowanej wizyty.",
        ErrorCodes.DepositOnTerminalAppointment);
    }

    // Poprzedni link zdążył wygasnąć nieopłacony → to realna, nieudana próba pobrania zadatku.
    // Nadpisanie wciąż ważnego linku nią NIE jest (personel zmienił kwotę, wysłał innym kanałem itp.).
    if (IsDepositLinkExpired(nowUtc))
    {
      ExpiredDepositLinkCount++;
    }

    DepositLinkAttempts++;

    DepositAmount = amount;
    PaymentSessionId = sessionId;
    PaymentLinkUrl = url;
    LinkExpiresAtUtc = expiresAtUtc;
    PaidAtUtc = null;
    PaymentStatus = AppointmentPaymentStatus.AwaitingPayment;

    // Nowy link = nikt go jeszcze nie dostał. Bez tego panel pokazywałby „wysłano" dla linku,
    // którego klient nigdy nie widział.
    DepositLinkSentAtUtc = null;
    DepositLinkSentChannel = null;
  }

  /// <summary>Znaczy udaną wysyłkę aktualnego linku zadatku wskazanym kanałem.</summary>
  public void MarkDepositLinkSent(DateTime nowUtc, string channel)
  {
    DepositLinkSentAtUtc = nowUtc;
    DepositLinkSentChannel = channel;
  }

  /// <summary>Oznacza zadatek jako opłacony. Idempotentne — ponowny webhook to no-op.</summary>
  public void MarkDepositPaid(DateTime nowUtc)
  {
    if (PaymentStatus == AppointmentPaymentStatus.Paid)
    {
      return;
    }

    PaymentStatus = AppointmentPaymentStatus.Paid;
    PaidAtUtc = nowUtc;
  }

  /// <summary>Oznacza opłacony zadatek jako zwrócony. Dozwolone tylko gdy zadatek był opłacony.</summary>
  public void MarkDepositRefunded()
  {
    if (PaymentStatus != AppointmentPaymentStatus.Paid)
    {
      throw new AppointmentBookingRuleException(
        "Nie można zwrócić zadatku, który nie został opłacony.", ErrorCodes.DepositNotPaid);
    }

    PaymentStatus = AppointmentPaymentStatus.Refunded;
  }

  /// <summary>
  /// Usuwa identyfikatory linku płatności (URL skrócony + sesja operatora) oraz ślad jego wysyłki —
  /// używane przy anonimizacji klienta (RODO art. 17). Zachowuje kwotę/status/PaidAt jako dane księgowe.
  /// </summary>
  public void ScrubPaymentLink()
  {
    PaymentLinkUrl = null;
    PaymentSessionId = null;
    DepositLinkSentAtUtc = null;
    DepositLinkSentChannel = null;
  }

  /// <summary>
  /// Czyści notatkę pracownika do wizyty (free-text, potencjalnie dane szczególnej kategorii —
  /// art. 9 RODO). Używane przy anonimizacji klienta (art. 17). Historia wizyty (data, status,
  /// usługa) zostaje — usuwamy tylko treść opisową.
  /// </summary>
  public void ScrubNotes()
  {
    AppointmentNotes = string.Empty;
  }

  /// <summary>
  /// Usuwa zapisaną weryfikację OTP (e-mail/telefon + hash kodu) wpisaną inline w wiersz wizyty.
  /// PII OTP nie ma wartości po wygaśnięciu kodu — kasujemy ją po oknie retencji (RODO,
  /// minimalizacja danych). Sam wiersz wizyty zostaje.
  /// </summary>
  public void ClearOtpVerification()
  {
    OtpVerification = null;
  }

  /// <summary>
  /// Czy link zadatku wygasł (oczekuje na opłatę, a minął termin ważności). Stan „Expired" liczony
  /// pochodnie — nie jest persystowany, by uniknąć dodatkowego background joba.
  /// </summary>
  public bool IsDepositLinkExpired(DateTime nowUtc) =>
    PaymentStatus == AppointmentPaymentStatus.AwaitingPayment
    && LinkExpiresAtUtc.HasValue
    && LinkExpiresAtUtc.Value < nowUtc;
}
