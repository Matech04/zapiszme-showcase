using App.Application.Common.Validation;
using App.Application.ShiftTemplates.Commands.CreateShiftTemplate;
using App.Application.ShiftTemplates.Commands.UpdateShiftTemplate;
using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Application.UnitTests.ShiftTemplates;

/// <summary>
/// Walidator szablonu jest mode-aware: tryb stały wymaga FixedStartTimes, tryb siatki — WorkRanges.
/// Brak właściwego pola → odrzucenie (ValidationBehavior → 400).
/// </summary>
public sealed class ShiftTemplateValidatorTests
{
  private static readonly TimeRangeDto Range = new(new TimeOnly(9, 0), new TimeOnly(17, 0));
  private static readonly TimeOnly[] Times = { new(9, 0), new(12, 0) };

  [Fact]
  public void Create_fixed_without_times_is_invalid()
  {
    var v = new CreateShiftTemplateCommandValidator();
    var r = v.Validate(new CreateShiftTemplateCommand("Stałe", SlotGenerationMode.FixedStartTimes,
      Array.Empty<TimeRangeDto>(), Array.Empty<TimeRangeDto>(), Array.Empty<TimeOnly>()));
    Assert.False(r.IsValid);
  }

  [Fact]
  public void Create_grid_without_workranges_is_invalid()
  {
    var v = new CreateShiftTemplateCommandValidator();
    var r = v.Validate(new CreateShiftTemplateCommand("Pełny", SlotGenerationMode.Grid,
      Array.Empty<TimeRangeDto>(), Array.Empty<TimeRangeDto>(), null));
    Assert.False(r.IsValid);
  }

  [Fact]
  public void Create_valid_fixed_and_grid_pass()
  {
    var v = new CreateShiftTemplateCommandValidator();
    Assert.True(v.Validate(new CreateShiftTemplateCommand("Stałe", SlotGenerationMode.FixedStartTimes,
      Array.Empty<TimeRangeDto>(), Array.Empty<TimeRangeDto>(), Times)).IsValid);
    Assert.True(v.Validate(new CreateShiftTemplateCommand("Pełny", SlotGenerationMode.Grid,
      new[] { Range }, Array.Empty<TimeRangeDto>(), null)).IsValid);
  }

  [Fact]
  public void Create_without_name_is_invalid()
  {
    var v = new CreateShiftTemplateCommandValidator();
    var r = v.Validate(new CreateShiftTemplateCommand("  ", SlotGenerationMode.FixedStartTimes,
      Array.Empty<TimeRangeDto>(), Array.Empty<TimeRangeDto>(), Times));
    Assert.False(r.IsValid);
  }

  [Fact]
  public void Update_fixed_without_times_is_invalid()
  {
    var v = new UpdateShiftTemplateCommandValidator();
    var r = v.Validate(new UpdateShiftTemplateCommand(Guid.NewGuid(), "Stałe", SlotGenerationMode.FixedStartTimes,
      Array.Empty<TimeRangeDto>(), Array.Empty<TimeRangeDto>(), Array.Empty<TimeOnly>()));
    Assert.False(r.IsValid);
  }

  [Fact]
  public void Update_with_empty_id_is_invalid()
  {
    var v = new UpdateShiftTemplateCommandValidator();
    var r = v.Validate(new UpdateShiftTemplateCommand(Guid.Empty, "Stałe", SlotGenerationMode.FixedStartTimes,
      Array.Empty<TimeRangeDto>(), Array.Empty<TimeRangeDto>(), Times));
    Assert.False(r.IsValid);
  }
}
