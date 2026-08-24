using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.Aggregates.EmployeeAggregate;

public class Employee : Entity, ITenantEntity, IAggregateRoot, ISoftDelete
{
  public Guid TenantId { get; private set; }
  public Guid? UserId { get; private set; }

  public string FirstName { get; private set; } = string.Empty;
  public string LastName { get; private set; } = string.Empty;
  public string Email { get; private set; } = string.Empty;
  public string? Specialization { get; private set; }

  public bool IsActive { get; private set; } = true;

  /// <summary>
  /// Czy pracownik jest rezerwowalnym specjalistą (pojawia się w kalendarzu, selektorach i publicznym
  /// bookingu). <c>false</c> = konto operacyjne bez własnego kalendarza, np. „Recepcja" (kiosk), które
  /// obsługuje wizyty innych, ale samo nie przyjmuje klientów.
  /// </summary>
  public bool IsBookable { get; private set; } = true;

  /// <summary>Sposób wyznaczania slotów: siatka (domyślnie) albo stałe godziny wpisane przez pracownika.</summary>
  public SlotGenerationMode SlotGenerationMode { get; private set; } = SlotGenerationMode.Grid;

  /// <summary>
  /// „Papierowy kalendarz": pracownik świadomie NIE prowadzi grafiku powtarzalnego — dostępność
  /// wpisuje dzień po dniu przez wyjątki (<see cref="Overrides"/>). Silnik dostępności obsługuje to
  /// od dawna (<c>IsAvailable</c>: urlop → wyjątek → grafik → niedostępny; wyjątek rozstrzyga
  /// samodzielnie, patrz OverrideOnlyFixedDayIntegrationTests), ale nie było jak tego ZADEKLAROWAĆ.
  ///
  /// Flaga NIE zmienia wyliczania dostępności — jest deklaracją intencji. Bez niej kreator nie umie
  /// odróżnić „jeszcze nie ustawiłam grafiku" od „świadomie go nie prowadzę" i w nieskończoność
  /// domaga się grafiku (<c>GetOnboardingStateQuery</c>), a panel podpowiada nie to, co trzeba.
  ///
  /// W tym trybie znika ograniczenie „dzień specjalny nie umie powiedzieć «wolne»": domyślnie nie
  /// pracujesz, więc dodajesz wyłącznie dni, w które pracujesz.
  /// </summary>
  public bool UsesAdHocSchedule { get; private set; }

  private readonly List<EmployeeSchedule> _schedules = new();
  public IReadOnlyCollection<EmployeeSchedule> Schedules => _schedules.AsReadOnly();

  private readonly List<EmployeeService> _services = new();
  public IReadOnlyCollection<EmployeeService> Services => _services.AsReadOnly();

  private readonly List<ScheduleOverride> _overrides = new();
  public IReadOnlyCollection<ScheduleOverride> Overrides => _overrides.AsReadOnly();
  private readonly List<EmployeeLeave> _leaves = new();
  public IReadOnlyCollection<EmployeeLeave> Leaves => _leaves.AsReadOnly();

  private readonly List<MonthPublication> _monthPublications = new();
  public IReadOnlyCollection<MonthPublication> MonthPublications => _monthPublications.AsReadOnly();

  public Employee(Guid tenantId, Guid? userId, string firstName, string lastName, string email)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    UserId = userId;
    UpdateEmail(email);
    UpdatePersonalData(firstName, lastName);
  }

  private Employee() { }

  public void Deactivate()
  {
    IsActive = false;
  }

  /// <summary>
  /// RODO art. 17 — trwale nadpisuje dane osobowe pracownicy (imię, nazwisko, e-mail, specjalizacja)
  /// i dezaktywuje rekord. Encja zostaje (FK z wizyt/grafiku → integralność historii salonu), ale
  /// osoby nie da się już zidentyfikować. Odpowiednik <c>Customer.Anonymize()</c> dla personelu.
  /// Email ustawiamy wprost, z pominięciem walidacji <see cref="UpdateEmail"/>.
  /// </summary>
  public void Anonymize()
  {
    FirstName = "Pracownik";
    LastName = "usunięty";
    Email = string.Empty;
    Specialization = null;
    IsActive = false;
  }

  public void UpdatePersonalData(string firstName, string lastName, string? specialization = null)
  {
    FirstName = Guard.NormalizeRequiredText(firstName, nameof(firstName));
    LastName = Guard.NormalizeRequiredText(lastName, nameof(lastName));
    Specialization = string.IsNullOrWhiteSpace(specialization) ? null : specialization.Trim();
  }

  public void Update(string firstName, string lastName, string? specialization = null) =>
    UpdatePersonalData(firstName, lastName, specialization);

  public void UpdateEmail(string email)
  {
    Email = Guard.NormalizeEmail(email);
  }

  public void LinkToUser(Guid userId)
  {
    UserId = userId;
  }

  /// <summary>Oznacza konto jako nierezerwowalne (np. „Recepcja"/kiosk) — znika z kalendarza i bookingu.</summary>
  public void MakeNonBookable()
  {
    IsBookable = false;
  }

  public void SetSlotGenerationMode(SlotGenerationMode mode)
  {
    SlotGenerationMode = mode;
  }

  /// <summary>
  /// Deklaruje (lub cofa) tryb „papierowego kalendarza" — patrz <see cref="UsesAdHocSchedule"/>.
  /// Nie kasuje istniejących grafików: pracownik może wrócić do powtarzalnego bez utraty ustawień,
  /// a o tym, czy grafik działa, i tak decyduje jego <c>IsActive</c>.
  /// </summary>
  public void SetUsesAdHocSchedule(bool usesAdHoc)
  {
    UsesAdHocSchedule = usesAdHoc;
  }

  public void AssignService(Guid tenantId, Guid serviceId, int? customDuration, Money? customPrice)
  {
    if (_services.Any(s => s.ServiceId == serviceId))
    {
      throw new AlreadyAssigned(nameof(serviceId));
    }

    _services.Add(new EmployeeService(tenantId, Id, serviceId, customDuration, customPrice));
  }

  public void UpdateService(Guid serviceId, int? customDuration, Money? customPrice)
  {
    var service = _services.FirstOrDefault(s => s.ServiceId == serviceId);
    if (service == null)
    {
      throw new NotFoundException(nameof(serviceId), serviceId);
    }

    service.Update(customDuration, customPrice);
  }

  public void UnassignService(Guid serviceId)
  {
    var service = _services.FirstOrDefault(s => s.ServiceId == serviceId);
    if (service == null)
    {
      throw new NotFoundException(nameof(serviceId), serviceId);
    }

    _services.Remove(service);
  }

  public TimeOnly CalculateEndTime(TimeOnly startTime, Guid serviceId, int defaultDuration)
  {
    var employeeService = _services.FirstOrDefault(s => s.ServiceId == serviceId);
    var duration = employeeService?.CustomDuration ?? defaultDuration;
    return startTime.AddMinutes(duration);
  }

  /// <summary>Czas trwania usługi dla tego pracownika (override z EmployeeService albo wartość domyślna). Do budowy pozycji combo.</summary>
  public int ResolveServiceDurationMinutes(Guid serviceId, int defaultDuration)
  {
    var employeeService = _services.FirstOrDefault(s => s.ServiceId == serviceId);
    return employeeService?.CustomDuration ?? defaultDuration;
  }

  /// <summary>Czy pracownik świadczy daną usługę (ma przypisanie EmployeeService).</summary>
  public bool OffersService(Guid serviceId) => _services.Any(s => s.ServiceId == serviceId);

  public Money CalculateTotalPrice(Guid serviceId, Money defaultPrice)
  {
    var employeeService = _services.FirstOrDefault(s => s.ServiceId == serviceId);

    if (employeeService == null)
    {
      throw new EmployeeServiceMissingException();
    }

    if (employeeService.CustomPrice != null)
    {
      return employeeService.CustomPrice;
    }
    else
    {
      return defaultPrice;
    }
  }
  public void RemoveSchedule(Guid scheduleId)
  {
    var schedule = _schedules.FirstOrDefault(s => s.Id == scheduleId);
    if (schedule == null)
    {
      throw new NotFoundException(nameof(EmployeeSchedule), scheduleId);
    }

    _schedules.Remove(schedule);
  }

  public void SetSchedule(DateRange activeRange, int numberOfCycles, IReadOnlyCollection<ScheduleDay> scheduleDays)
  {
    // Zachowanie kompatybilne ze starym API (np. SetWeeklySchedule w seederze):
    // - jeśli istnieje grafik o IDENTYCZNYM zakresie, traktujemy to jako aktualizację,
    // - w przeciwnym razie dodajemy nowy z walidacją overlap.
    var existingSchedule = _schedules.FirstOrDefault(s => s.ActiveRange == activeRange);
    if (existingSchedule != null)
    {
      existingSchedule.UpdateNumberOfCycles(numberOfCycles);
      existingSchedule.SetEmployeeSchedule(scheduleDays);
      return;
    }

    AddSchedule(activeRange, numberOfCycles, scheduleDays);
  }

  public Guid AddSchedule(DateRange activeRange, int numberOfCycles, IReadOnlyCollection<ScheduleDay> scheduleDays, bool isActive = true)
  {
    // Kolizję zakresów sprawdzamy tylko aktywny-vs-aktywny. Nieaktywny grafik może dowolnie nachodzić
    // (scenariusz „szkic / podmiana") — konflikt wychwytujemy dopiero przy jego aktywacji.
    if (isActive && _schedules.Any(s => s.IsActive && s.ActiveRange.OverlapsWith(activeRange)))
    {
      throw new SchedulesCollisionException();
    }

    var schedule = new EmployeeSchedule(activeRange, numberOfCycles, scheduleDays, isActive);
    _schedules.Add(schedule);
    return schedule.Id;
  }

  public void UpdateSchedule(Guid scheduleId, DateRange activeRange, int numberOfCycles, IReadOnlyCollection<ScheduleDay> scheduleDays, bool isActive = true)
  {
    var schedule = _schedules.FirstOrDefault(s => s.Id == scheduleId)
      ?? throw new NotFoundException(nameof(EmployeeSchedule), scheduleId);

    if (isActive)
    {
      var hasOverlap = _schedules
        .Where(s => s.Id != scheduleId && s.IsActive)
        .Any(s => s.ActiveRange.OverlapsWith(activeRange));
      if (hasOverlap)
      {
        throw new SchedulesCollisionException();
      }
    }

    schedule.UpdateActiveRange(activeRange);
    schedule.UpdateNumberOfCycles(numberOfCycles);
    schedule.SetEmployeeSchedule(scheduleDays);
    schedule.SetActive(isActive);
  }

  /// <summary>
  /// Włącza lub wyłącza istniejący grafik. Przy WŁĄCZANIU sprawdza kolizję zakresu z innymi
  /// aktywnymi grafikami (nie da się mieć dwóch nachodzących aktywnych) — rzuca
  /// <see cref="SchedulesCollisionException"/>. Wyłączenie zawsze dozwolone.
  /// </summary>
  public void SetScheduleActive(Guid scheduleId, bool isActive)
  {
    var schedule = _schedules.FirstOrDefault(s => s.Id == scheduleId)
      ?? throw new NotFoundException(nameof(EmployeeSchedule), scheduleId);

    if (isActive)
    {
      var hasOverlap = _schedules
        .Where(s => s.Id != scheduleId && s.IsActive)
        .Any(s => s.ActiveRange.OverlapsWith(schedule.ActiveRange));
      if (hasOverlap)
      {
        throw new SchedulesCollisionException();
      }
    }

    schedule.SetActive(isActive);
  }

  public void SetWeeklySchedule(Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> newWeeklySchedule)
  {
    var scheduleDays = newWeeklySchedule
      .Where(d => d.Value.Count > 0)
      .Select(d =>
      {
        // Klonujemy TimeRange-y per dzień: TimeRange jest owned entity z perspektywy EF,
        // a wspólne referencje (np. ten sam ranges-array dla wszystkich dni tygodnia)
        // powodują, że EF zapisuje tylko jedną instancję pod ostatnim właścicielem.
        var clonedRanges = d.Value.Select(r => new TimeRange(r.StartTime, r.EndTime)).ToList();
        var validatedRanges = TimeRange.ValidateRanges(clonedRanges);
        return new ScheduleDay(validatedRanges, breaks: null, cycleIndex: (int)d.Key);
      })
      .ToList();

    SetSchedule(new DateRange(DateOnly.MinValue, DateOnly.MaxValue), 1, scheduleDays);
  }

  public void SetScheduleOverride(DateOnly date, ScheduleDay scheduleDay)
  {
    var existingOverride = _overrides.FirstOrDefault(o => o.Date == date);
    if (existingOverride != null)
    {
      existingOverride.SetScheduleDay(scheduleDay);
    }
    else
    {
      _overrides.Add(new ScheduleOverride(date, scheduleDay));
    }
  }

  public void SetScheduleOverride(DateOnly date, IReadOnlyCollection<TimeRange>? workRanges)
  {
    if (workRanges == null || workRanges.Count == 0)
    {
      RemoveScheduleOverride(date);
      return;
    }

    SetScheduleOverride(date, new ScheduleDay(workRanges));
  }

  public void SetScheduleOverride(DateOnly date, IReadOnlyCollection<TimeRange>? workRanges, IReadOnlyCollection<TimeRange>? breaks)
  {
    if (workRanges == null || workRanges.Count == 0)
    {
      RemoveScheduleOverride(date);
      return;
    }

    SetScheduleOverride(date, new ScheduleDay(workRanges, breaks));
  }

  public void RemoveScheduleOverride(DateOnly date)
  {
    var scheduleOverride = _overrides.FirstOrDefault(x => x.Date == date);

    if (scheduleOverride != null)
    {
      _overrides.Remove(scheduleOverride);
    }
  }


  public bool HasLeaveOverlap(DateOnly startDate, DateOnly endDate)
  {
    var candidateRange = new DateRange(startDate, endDate);
    return _leaves.Any(l => candidateRange.OverlapsWith(l.DateRange));
  }

  public void AddLeave(DateOnly startDate, DateOnly endDate, AbsenceType absenceType = AbsenceType.Vacation)
  {
    var leaveRange = new DateRange(startDate, endDate);
    if (HasLeaveOverlap(startDate, endDate))
    {
      throw new LeaveOverlapException();
    }

    _leaves.Add(new EmployeeLeave(leaveRange, absenceType));
  }

  public void RemoveLeave(Guid leaveId)
  {
    var leave = _leaves.FirstOrDefault(l => l.Id == leaveId);
    if (leave != null)
    {
      _leaves.Remove(leave);
    }
  }


  /// <summary>Ustawia (lub aktualizuje) datę otwarcia zapisów na dany miesiąc. <c>null</c> = zamknięty bezterminowo.</summary>
  public void SetMonthPublication(int year, int month, DateOnly? opensOn)
  {
    var existing = _monthPublications.FirstOrDefault(p => p.Year == year && p.Month == month);
    if (existing != null)
    {
      existing.SetOpensOn(opensOn);
      return;
    }

    _monthPublications.Add(new MonthPublication(year, month, opensOn));
  }

  /// <summary>
  /// Usuwa jawną decyzję o publikacji miesiąca — miesiąc wraca pod domyślną regułę horyzontu.
  /// To NIE to samo co zamknięcie: brak wiersza oznacza „otwarty w granicach horyzontu".
  /// </summary>
  public void ClearMonthPublication(int year, int month)
  {
    var existing = _monthPublications.FirstOrDefault(p => p.Year == year && p.Month == month);
    if (existing != null)
    {
      _monthPublications.Remove(existing);
    }
  }

  public MonthPublication? FindMonthPublication(int year, int month) =>
    _monthPublications.FirstOrDefault(p => p.Year == year && p.Month == month);

  /// <summary>
  /// Czy <paramref name="date"/> jest w ogóle widoczna w PUBLICZNYM bookingu w dniu <paramref name="today"/>.
  /// Dotyczy wyłącznie rezerwacji online — personel w panelu wpisuje wizyty bez tego ograniczenia,
  /// dlatego reguła NIE jest wołana z <see cref="IsAvailable"/> ani <see cref="GetWorkingRanges"/>
  /// (te metody obsługują oba światy) tylko jawnie z handlerów bookingu.
  ///
  /// Jawny wiersz miesiąca ma pierwszeństwo nad horyzontem w obie strony: potrafi zamknąć miesiąc
  /// mieszczący się w horyzoncie i otworzyć taki, który jest poza nim.
  /// </summary>
  public bool IsDateOpenForOnlineBooking(DateOnly date, DateOnly today, int horizonDays)
  {
    var publication = FindMonthPublication(date.Year, date.Month);
    if (publication != null)
    {
      return publication.IsOpenAsOf(today);
    }

    return date <= today.AddDays(horizonDays);
  }

  public bool IsAvailable(TimeRange timeRange, DateOnly date)
  {
    if (_leaves.Any(l => l.DateRange.Contains(date)))
    {
      return false;
    }

    // Tryb stałych slotów nie ma okna pracy — to osobisty kalendarz pracownika. Dostępność ogranicza
    // wyłącznie kolizja z istniejącą wizytą (sprawdzana w AppointmentService). Ograniczenie „tylko
    // zdefiniowane sloty" dotyczy WYŁĄCZNIE rezerwacji online i żyje w CreateAppointmentCommand —
    // personel w panelu może wpisać dowolną godzinę.
    if (GetSlotModeForDate(date) == SlotGenerationMode.FixedStartTimes)
    {
      return true;
    }

    // Honoruj override TYLKO gdy pasuje do trybu (tu: tryb siatki → override nie-fixed).
    // Override w drugim trybie (np. osierocony po przełączeniu trybu pracownika) jest ignorowany —
    // inaczej jego puste przedziały pracy cicho zablokowałyby cały dzień.
    var scheduleOverride = _overrides.FirstOrDefault(o => o.Date == date && !o.IsFixed);
    if (scheduleOverride != null)
    {
      return scheduleOverride.IsRangeAvailable(timeRange);
    }

    var baseScheduleDay = ResolveScheduleDay(date);
    if (baseScheduleDay != null)
    {
      return baseScheduleDay.IsRangeAvailable(timeRange);
    }

    return false;
  }

  public bool IsAvailable(TimeOnly startTime, TimeOnly endTime, DateOnly date)
  {
    return IsAvailable(new TimeRange(startTime, endTime), date);
  }

  public List<TimeRange> GetWorkingRanges(DateOnly date)
  {
    if (_leaves.Any(l => l.DateRange.Contains(date)))
    {
      return new List<TimeRange>();
    }

    // Tylko override w trybie siatki (nie-fixed); niezgodny/osierocony pomijamy → grafik bazowy.
    var dayOverride = _overrides.FirstOrDefault(o => o.Date == date && !o.IsFixed);
    if (dayOverride?.ScheduleDay != null)
    {
      return dayOverride.ScheduleDay.GetAvailableWorkRanges();
    }

    var baseScheduleDay = ResolveScheduleDay(date);
    if (baseScheduleDay != null)
    {
      return baseScheduleDay.GetAvailableWorkRanges();
    }

    return new List<TimeRange>();
  }

  public List<TimeRange> GetWorkingSlots(DateOnly date) => GetWorkingRanges(date);

  /// <summary>
  /// Stałe godziny startu dla danego dnia (tryb FixedStartTimes). Ta sama kolejność rozwiązywania
  /// co <see cref="GetWorkingRanges"/>: urlop → pusto; override → godziny z override; inaczej dzień bazowy.
  /// </summary>
  public List<TimeOnly> GetFixedStartTimes(DateOnly date)
  {
    if (_leaves.Any(l => l.DateRange.Contains(date)))
    {
      return new List<TimeOnly>();
    }

    // Tylko override w trybie stałych godzin; niezgodny/osierocony pomijamy → grafik bazowy.
    var dayOverride = _overrides.FirstOrDefault(o => o.Date == date && o.IsFixed);
    if (dayOverride?.ScheduleDay != null)
    {
      return dayOverride.ScheduleDay.FixedStartTimes.ToList();
    }

    var baseScheduleDay = ResolveScheduleDay(date);
    if (baseScheduleDay != null)
    {
      return baseScheduleDay.FixedStartTimes.ToList();
    }

    return new List<TimeOnly>();
  }

  /// <summary>
  /// Efektywny tryb generowania slotów DLA KONKRETNEGO DNIA. Jeśli na ten dzień istnieje wyjątek
  /// (override) — decyduje jego tryb (kosmetyczka może budować grafik samymi „klikniętymi" dniami,
  /// bez grafiku tygodniowego, mieszając tryby per dzień). W przeciwnym razie globalny tryb pracownika.
  /// </summary>
  public SlotGenerationMode GetSlotModeForDate(DateOnly date)
  {
    var dayOverride = _overrides.FirstOrDefault(o => o.Date == date);
    if (dayOverride != null)
    {
      return dayOverride.IsFixed ? SlotGenerationMode.FixedStartTimes : SlotGenerationMode.Grid;
    }

    // Tryb wynika z FAKTYCZNEGO dnia grafiku obowiązującego dla daty (dzień „fixed" ma godziny startu),
    // a NIE z globalnego trybu pracownika. Inaczej przy kilku grafikach o różnych trybach (np. lipiec
    // statyczny, sierpień z przedziałami) globalny tryb = tryb OSTATNIO zapisanego grafiku, więc data
    // z drugiego grafiku byłaby liczona złym trybem → dzień „fixed" ma puste WorkRanges → brak slotów.
    var scheduleDay = ResolveScheduleDay(date);
    if (scheduleDay != null)
    {
      return scheduleDay.IsFixed ? SlotGenerationMode.FixedStartTimes : SlotGenerationMode.Grid;
    }

    return SlotGenerationMode;
  }

  /// <summary>Czy dla danego dnia obowiązuje tryb stałych godzin startu (uwzględnia override).</summary>
  public bool IsFixedForDate(DateOnly date) => GetSlotModeForDate(date) == SlotGenerationMode.FixedStartTimes;

  private ScheduleDay? ResolveScheduleDay(DateOnly date)
  {
    var matchingSchedule = _schedules
      .Where(s => s.IsActive && s.ActiveRange.Contains(date))
      .OrderByDescending(s => s.ActiveRange.StartDate)
      .FirstOrDefault();

    if (matchingSchedule == null)
    {
      return null;
    }

    if (matchingSchedule.NumberOfCycles <= 0)
    {
      return null;
    }

    var daysOffset = date.DayNumber - matchingSchedule.ActiveRange.StartDate.DayNumber;
    if (daysOffset < 0)
    {
      return null;
    }

    var weeksOffset = daysOffset / 7;
    var cycleWeekIndex = weeksOffset % matchingSchedule.NumberOfCycles;
    var dayOfWeekIndex = (int)date.DayOfWeek; // C#: Sunday = 0, Monday = 1, ...
    var cycleIndex = (cycleWeekIndex * 7) + dayOfWeekIndex;

    return matchingSchedule.ScheduleDays.FirstOrDefault(d => d.CycleIndex == cycleIndex);
  }

}