using App.Domain.Aggregates.EmployeeAggregate;

public interface IEmployeeRepository
{
  Task AddAsync(Employee employee);
  Task<Employee?> GetByIdAsync(Guid id);

  /// <summary>
  /// Ładuje pracownika dla wyliczania dostępności: pełne usługi + grafiki (ze ScheduleDays),
  /// ale Overrides i Leaves OGRANICZONE do zakresu [from, to]. Bez ograniczenia graf rośnie
  /// w nieskończoność (Overrides/Leaves akumulują się), a jest ładowany na każde anonimowe
  /// available-slots / month-availability. Override (po dacie) i Leave (po przecięciu zakresu
  /// dat) muszą obejmować każdy override/urlop dotykający żądanego zakresu.
  /// </summary>
  Task<Employee?> GetForAvailabilityAsync(Guid id, DateOnly from, DateOnly to, CancellationToken ct);

  /// <summary>
  /// Wariant <see cref="GetForAvailabilityAsync"/> dla WIELU pracowników naraz — publiczna lista
  /// pracowników musi wiedzieć, którzy mają grafik w oknie rezerwacji, a pętla po pojedynczym
  /// getterze dawałaby N round-tripów na każde wejście na stronę salonu.
  /// </summary>
  Task<List<Employee>> GetManyForAvailabilityAsync(
      IReadOnlyList<Guid> ids, DateOnly from, DateOnly to, CancellationToken ct);

  /// <summary>
  /// Tylko pracownik i urlopy — bez grafiku / usług / override’ów, żeby uniknąć przypadkowych UPDATE przy SaveChanges.
  /// </summary>
  Task<Employee?> GetByIdWithLeavesAsync(Guid id);

  void Update(Employee employee);
  void Remove(Employee employee);
}