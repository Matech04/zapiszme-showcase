using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Application.Employees.Dtos;

public record ScheduleOverrideDto(
  DateOnly Date,
  SlotGenerationMode SlotGenerationMode,
  IReadOnlyCollection<TimeRangeDto>? WorkRanges,
  IReadOnlyCollection<TimeRangeDto>? Breaks,
  IReadOnlyCollection<TimeOnly>? FixedStartTimes
);
