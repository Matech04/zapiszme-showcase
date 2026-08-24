using App.Domain.Common;

namespace App.Domain.Aggregates.ServiceAggregate;

public class Service : Entity, ITenantEntity, IAggregateRoot, ISoftDelete
{
  public Guid TenantId { get; private set; }

  /// <summary>
  /// Opcjonalna kategoria. <c>null</c> = usługa "orphan" (bez kategorii), pokazywana
  /// w UI w sekcji "Inne usługi" (public booking) lub w grupie "Bez kategorii" (dashboard).
  /// </summary>
  public Guid? CategoryId { get; private set; }
  public Guid VatRateId { get; private set; }
  public string Name { get; private set; } = string.Empty;
  public Money Price { get; private set; } = null!;

  /// <summary>
  /// Górna granica widełek cenowych (kwota, waluta dziedziczona z <see cref="Price"/>).
  /// <c>null</c> = cena stała. Gdy ustawione, cena prezentowana jest jako zakres
  /// "od {Price} do {PriceMaxAmount}" — faktyczna kwota ustalana jest na miejscu
  /// (zob. cena końcowa wizyty: Appointment.FinalPrice).
  /// </summary>
  public decimal? PriceMaxAmount { get; private set; }

  /// <summary>
  /// Czas „planowany" blokowany w kalendarzu (steruje dostępnością i kolizjami).
  /// Niezależny od przedziału czasu prezentowanego klientce (<see cref="DurationMinMinutes"/>/<see cref="DurationMaxMinutes"/>).
  /// </summary>
  public int DurationInMinutes { get; private set; }

  /// <summary>Dolna granica przedziału czasu wykonania — tylko do wyświetlania. <c>null</c> = czas stały.</summary>
  public int? DurationMinMinutes { get; private set; }

  /// <summary>Górna granica przedziału czasu wykonania — tylko do wyświetlania. <c>null</c> = czas stały.</summary>
  public int? DurationMaxMinutes { get; private set; }

  /// <summary>
  /// Opcjonalna etykieta grupy wariantów (np. „Przedłużanie" dla rozmiarów S/M/L/XL). W jednej wizycie-combo
  /// można wybrać MAX jedną usługę z danej (niepustej) grupy. <c>null</c>/pusta = usługa łączy się ze wszystkim.
  /// Reguła egzekwowana w warstwie aplikacji (ComboCompositionValidator) — usługa nie zna grup innych usług.
  /// </summary>
  public string? ComboGroup { get; private set; }

  /// <summary>
  /// Gdy <c>true</c>, cena usługi nie jest pokazywana klientce (publiczny katalog booking
  /// + panel). Ukrycie realizuje frontend wg tej flagi — backend NADAL zwraca <see cref="Price"/>.
  /// </summary>
  public bool HidePrice { get; private set; }

  /// <summary>
  /// Gdy <c>true</c>, usługa jest „dodatkiem" — dobieranym do usługi głównej, nie rezerwowanym
  /// samodzielnie. Typowo trwa 0 min (czas ukrywany w kalendarzu klienta). Dozwolone powiązania
  /// „która usługa główna może mieć ten dodatek" trzyma usługa GŁÓWNA w <see cref="Addons"/>.
  /// </summary>
  public bool IsAddon { get; private set; }

  /// <summary>
  /// Dozwolone dodatki tej usługi (gdy jest usługą główną). Owned collection — ładowana z agregatem.
  /// Usługa-dodatek (<see cref="IsAddon"/> = true) nie ma własnych dodatków (lista pusta).
  /// </summary>
  private readonly List<ServiceAddon> _addons = new();
  public IReadOnlyCollection<ServiceAddon> Addons => _addons.AsReadOnly();

  /// <summary>
  /// Pozycja usługi przy ręcznym sortowaniu (drag&amp;drop) w obrębie kategorii. Mniejsza = wyżej.
  /// Domyślnie 0; kolejność końcowa: OrderIndex, potem Name.
  /// </summary>
  public int OrderIndex { get; private set; }

  /// <summary>Maksymalna liczba zdjęć w galerii usługi.</summary>
  public const int MaxImages = 5;

  /// <summary>Maksymalna długość opisu usługi (znaki).</summary>
  public const int MaxDescriptionLength = 2000;

  /// <summary>
  /// Opcjonalny opis usługi pokazywany klientce w publicznym katalogu rezerwacji. <c>null</c> = brak.
  /// Trim + pusty→null; górną granicę długości pilnuje też walidator komendy.
  /// </summary>
  public string? Description { get; private set; }

  /// <summary>
  /// Galeria zdjęć usługi (max <see cref="MaxImages"/>). Owned collection — ładowana z agregatem.
  /// Pierwsze zdjęcie wg OrderIndex = cover. Kolejność wyznacza front przy zapisie.
  /// </summary>
  private readonly List<ServiceImage> _images = new();
  public IReadOnlyCollection<ServiceImage> Images => _images.AsReadOnly();

  public bool IsActive { get; private set; } = true;

  /// <summary>
  /// Opcjonalny override domyślnej kwoty zadatku dla tej usługi. <c>null</c> = dziedziczy ustawienia
  /// salonu (<see cref="TenantAggregate.DepositSettings"/>). Zarezerwowane na fazę 2 — logika
  /// override jeszcze nieaktywna, kolumna istnieje by uniknąć ponownej migracji.
  /// </summary>
  public decimal? DepositOverrideValue { get; private set; }

  /// <summary>Ustawia/zeruje override kwoty zadatku dla usługi (faza 2).</summary>
  public void SetDepositOverride(decimal? value)
  {
    if (value.HasValue)
    {
      Guard.AgainstNegative(value.Value, nameof(value));
    }

    DepositOverrideValue = value;
  }

  public Service(
    Guid tenantId,
    Guid? categoryId,
    Guid vatRateId,
    string name,
    Money price,
    int durationInMinutes,
    decimal? priceMaxAmount = null,
    int? durationMinMinutes = null,
    int? durationMaxMinutes = null,
    string? comboGroup = null,
    bool hidePrice = false,
    bool isAddon = false,
    string? description = null)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    Update(categoryId, vatRateId, name, price, durationInMinutes, priceMaxAmount, durationMinMinutes, durationMaxMinutes, comboGroup, hidePrice, isAddon, description);
  }

  private Service() { }

  public void Update(
    Guid? categoryId,
    Guid vatRateId,
    string name,
    Money price,
    int durationInMinutes = 0,
    decimal? priceMaxAmount = null,
    int? durationMinMinutes = null,
    int? durationMaxMinutes = null,
    string? comboGroup = null,
    bool hidePrice = false,
    bool isAddon = false,
    string? description = null)
  {

    Guard.AgainstNegative(price.Amount, nameof(price));
    Guard.AgainstNegative(durationInMinutes, nameof(durationInMinutes));
    ValidatePriceRange(price, priceMaxAmount);
    ValidateDurationRange(durationMinMinutes, durationMaxMinutes);

    CategoryId = categoryId;
    Description = NormalizeDescription(description);
    VatRateId = vatRateId;
    Name = Guard.NormalizeRequiredText(name, nameof(name));
    Price = price;
    PriceMaxAmount = priceMaxAmount;
    DurationInMinutes = durationInMinutes;
    DurationMinMinutes = durationMinMinutes;
    DurationMaxMinutes = durationMaxMinutes;
    ComboGroup = NormalizeComboGroup(comboGroup);
    HidePrice = hidePrice;
    IsAddon = isAddon;
    if (isAddon)
    {
      // Dodatek nie może mieć własnych dodatków — czyścimy ewentualne wcześniejsze powiązania.
      _addons.Clear();
    }
  }

  /// <summary>
  /// Ustawia listę dozwolonych dodatków tej usługi (reconcile: usuwa nieobecne, dodaje nowe).
  /// Pomija self-referencje i duplikaty. Istnienie usług oraz ich flagę <see cref="IsAddon"/>
  /// weryfikuje handler komendy. Dla usługi będącej dodatkiem lista pozostaje pusta.
  /// </summary>
  public void SetAddons(IEnumerable<Guid> addonServiceIds)
  {
    if (IsAddon)
    {
      _addons.Clear();
      return;
    }

    var desired = addonServiceIds
      .Where(id => id != Guid.Empty && id != Id)
      .Distinct()
      .ToHashSet();

    _addons.RemoveAll(a => !desired.Contains(a.AddonServiceId));

    var existing = _addons.Select(a => a.AddonServiceId).ToHashSet();
    foreach (var addonId in desired)
    {
      if (existing.Add(addonId))
      {
        _addons.Add(new ServiceAddon(TenantId, Id, addonId));
      }
    }
  }

  /// <summary>
  /// Reconcile galerii zdjęć usługi z listy opisów (URL + miniatura + klucz storage). Kolejność listy
  /// wyznacza <see cref="ServiceImage.OrderIndex"/> (0 = cover). Zwraca klucze storage zdjęć USUNIĘTYCH
  /// w tej operacji — caller (handler) może je best-effort skasować z storage. Walidacja capa
  /// (<see cref="MaxImages"/>) jest tutaj (invariant) i w walidatorze komendy.
  /// </summary>
  public IReadOnlyList<string> SetImages(IEnumerable<ServiceImageData> images)
  {
    var desired = images.ToList();
    if (desired.Count > MaxImages)
    {
      throw new ArgumentException($"Usługa może mieć maksymalnie {MaxImages} zdjęć.", nameof(images));
    }

    var desiredKeys = desired.Select(i => i.StorageKey).ToHashSet();
    var removedKeys = _images
      .Where(existing => !desiredKeys.Contains(existing.StorageKey))
      .Select(existing => existing.StorageKey)
      .ToList();

    _images.Clear();
    var order = 0;
    foreach (var img in desired)
    {
      _images.Add(new ServiceImage(TenantId, Id, img.Url, img.ThumbnailUrl, img.StorageKey, order));
      order++;
    }

    return removedKeys;
  }

  /// <summary>Trim + pusty→null; górną granicę długości pilnuje też walidator komendy.</summary>
  private static string? NormalizeDescription(string? value)
  {
    var trimmed = value?.Trim();
    return string.IsNullOrEmpty(trimmed) ? null : trimmed;
  }

  /// <summary>Ustawia pozycję usługi przy ręcznym sortowaniu (drag&amp;drop). Nieujemna.</summary>
  public void SetOrder(int orderIndex)
  {
    Guard.AgainstNegative(orderIndex, nameof(orderIndex));
    OrderIndex = orderIndex;
  }

  /// <summary>Trim + pusty→null; górna granica długości pilnowana też walidatorem komendy.</summary>
  private static string? NormalizeComboGroup(string? value)
  {
    var trimmed = value?.Trim();
    return string.IsNullOrEmpty(trimmed) ? null : trimmed;
  }

  private static void ValidatePriceRange(Money price, decimal? priceMaxAmount)
  {
    if (priceMaxAmount is not { } maxAmount)
    {
      return;
    }

    Guard.AgainstNegative(maxAmount, nameof(priceMaxAmount));
    if (maxAmount < price.Amount)
    {
      throw new ArgumentException("Górna granica ceny nie może być mniejsza niż cena bazowa.", nameof(priceMaxAmount));
    }
  }

  private static void ValidateDurationRange(int? durationMinMinutes, int? durationMaxMinutes)
  {
    // Przedział wymaga obu granic albo żadnej (czas stały).
    if ((durationMinMinutes is null) != (durationMaxMinutes is null))
    {
      throw new ArgumentException("Przedział czasu wymaga obu granic (min i max).", nameof(durationMinMinutes));
    }

    if (durationMinMinutes is { } min && durationMaxMinutes is { } max)
    {
      if (min <= 0 || max <= 0)
      {
        throw new ArgumentException("Granice przedziału czasu muszą być dodatnie.", nameof(durationMinMinutes));
      }

      if (min > max)
      {
        throw new ArgumentException("Dolna granica czasu nie może przekraczać górnej.", nameof(durationMinMinutes));
      }
    }
  }

  public void Deactivate()
  {
    IsActive = false;
  }

  /// <summary>
  /// Odpina usługę od kategorii (<see cref="CategoryId"/> = <c>null</c>) — staje się „orphanem"
  /// pokazywanym w grupie „Bez kategorii". Używane przy usuwaniu kategorii: usługa zostaje
  /// aktywna, traci wyłącznie przypisanie do kategorii. Pozostałe pola bez zmian.
  /// </summary>
  public void DetachCategory()
  {
    CategoryId = null;
  }
}
