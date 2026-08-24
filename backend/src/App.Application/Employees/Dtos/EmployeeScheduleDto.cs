using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Application.Employees.Dtos;

public record EmployeeScheduleDayDto(
  int CycleIndex,
  IReadOnlyCollection<TimeRangeDto> WorkRanges,
  IReadOnlyCollection<TimeRangeDto> Breaks,
  // Tryb FixedStartTimes: godziny startu slotów dla tego dnia (zamiast przedziałów). Puste/null = dzień wolny.
  IReadOnlyCollection<TimeOnly>? FixedStartTimes = null
);

public record EmployeeScheduleDto(
  DateOnly ActiveFrom,
  DateOnly ActiveTo,
  int NumberOfCycles,
  List<EmployeeScheduleDayDto> Days,
  Guid? Id = null,
  SlotGenerationMode SlotGenerationMode = SlotGenerationMode.Grid,
  // Czy grafik obowiązuje. Nieaktywny = zapisany „szkic", nie generuje wolnych terminów.
  bool IsActive = true
);
