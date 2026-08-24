using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Application.ShiftTemplates.Dtos;

public record ShiftTemplateDto(
  Guid Id,
  string Name,
  SlotGenerationMode SlotGenerationMode,
  IReadOnlyCollection<TimeRangeDto> WorkRanges,
  IReadOnlyCollection<TimeRangeDto> Breaks,
  IReadOnlyCollection<TimeOnly> FixedStartTimes
);
