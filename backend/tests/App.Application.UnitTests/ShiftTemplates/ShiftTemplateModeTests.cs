using App.Application.Common.Interfaces;
using App.Application.ShiftTemplates.Commands.CreateShiftTemplate;
using App.Application.ShiftTemplates.Commands.UpdateShiftTemplate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.ShiftTemplates;

/// <summary>
/// Szablony zmian w obu trybach: tworzenie szablonu stałogodzinnego, edycja (zmiana trybu) i izolacja tenantów.
/// </summary>
public sealed class ShiftTemplateModeTests
{
  [Fact]
  public async Task Creates_fixed_template_with_start_times_and_no_work_ranges()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();

    var id = await new CreateShiftTemplateCommandHandler(new ShiftTemplateRepository(db), db, Tenant(tenantId))
      .Handle(new CreateShiftTemplateCommand(
        Name: "Stałe poranne",
        SlotGenerationMode: SlotGenerationMode.FixedStartTimes,
        WorkRanges: Array.Empty<TimeRangeDto>(),
        Breaks: Array.Empty<TimeRangeDto>(),
        FixedStartTimes: new[] { new TimeOnly(12, 0), new TimeOnly(9, 0) }), ct);

    var saved = await new ShiftTemplateRepository(db).GetByIdAsync(id);
    Assert.NotNull(saved);
    Assert.Equal(SlotGenerationMode.FixedStartTimes, saved!.SlotGenerationMode);
    Assert.Equal(new[] { new TimeOnly(9, 0), new TimeOnly(12, 0) }, saved.ScheduleDay.FixedStartTimes);
    Assert.Empty(saved.ScheduleDay.WorkRanges);
  }

  [Fact]
  public async Task Creates_grid_template_with_work_ranges()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();

    var id = await new CreateShiftTemplateCommandHandler(new ShiftTemplateRepository(db), db, Tenant(tenantId))
      .Handle(new CreateShiftTemplateCommand(
        Name: "Pełny dzień",
        SlotGenerationMode: SlotGenerationMode.Grid,
        WorkRanges: new[] { new TimeRangeDto(new TimeOnly(9, 0), new TimeOnly(17, 0)) },
        Breaks: new[] { new TimeRangeDto(new TimeOnly(12, 0), new TimeOnly(12, 30)) },
        FixedStartTimes: null), ct);

    var saved = await new ShiftTemplateRepository(db).GetByIdAsync(id);
    Assert.Equal(SlotGenerationMode.Grid, saved!.SlotGenerationMode);
    Assert.Single(saved.ScheduleDay.WorkRanges);
    Assert.Empty(saved.ScheduleDay.FixedStartTimes);
  }

  [Fact]
  public async Task Updates_grid_template_to_fixed_mode()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();

    var id = await new CreateShiftTemplateCommandHandler(new ShiftTemplateRepository(db), db, Tenant(tenantId))
      .Handle(new CreateShiftTemplateCommand(
        Name: "Zmiana",
        SlotGenerationMode: SlotGenerationMode.Grid,
        WorkRanges: new[] { new TimeRangeDto(new TimeOnly(9, 0), new TimeOnly(17, 0)) },
        Breaks: Array.Empty<TimeRangeDto>(),
        FixedStartTimes: null), ct);

    await new UpdateShiftTemplateCommandHandler(new ShiftTemplateRepository(db), db, Tenant(tenantId))
      .Handle(new UpdateShiftTemplateCommand(
        Id: id,
        Name: "Zmiana stała",
        SlotGenerationMode: SlotGenerationMode.FixedStartTimes,
        WorkRanges: Array.Empty<TimeRangeDto>(),
        Breaks: Array.Empty<TimeRangeDto>(),
        FixedStartTimes: new[] { new TimeOnly(10, 0), new TimeOnly(13, 0) }), ct);

    var saved = await new ShiftTemplateRepository(db).GetByIdAsync(id);
    Assert.Equal("Zmiana stała", saved!.Name);
    Assert.Equal(SlotGenerationMode.FixedStartTimes, saved.SlotGenerationMode);
    Assert.Equal(new[] { new TimeOnly(10, 0), new TimeOnly(13, 0) }, saved.ScheduleDay.FixedStartTimes);
    Assert.Empty(saved.ScheduleDay.WorkRanges);
  }

  [Fact]
  public async Task Update_cross_tenant_throws_TenantViolation()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();

    var id = await new CreateShiftTemplateCommandHandler(new ShiftTemplateRepository(db), db, Tenant(tenantId))
      .Handle(new CreateShiftTemplateCommand(
        Name: "Zmiana", SlotGenerationMode: SlotGenerationMode.Grid,
        WorkRanges: new[] { new TimeRangeDto(new TimeOnly(9, 0), new TimeOnly(17, 0)) },
        Breaks: Array.Empty<TimeRangeDto>(), FixedStartTimes: null), ct);

    var otherTenant = Guid.NewGuid();
    await Assert.ThrowsAsync<TenantViolation>(() =>
      new UpdateShiftTemplateCommandHandler(new ShiftTemplateRepository(db), db, Tenant(otherTenant))
        .Handle(new UpdateShiftTemplateCommand(
          Id: id, Name: "X", SlotGenerationMode: SlotGenerationMode.Grid,
          WorkRanges: new[] { new TimeRangeDto(new TimeOnly(8, 0), new TimeOnly(16, 0)) },
          Breaks: Array.Empty<TimeRangeDto>(), FixedStartTimes: null), ct));
  }

  // ── helpers ──

  private static FakeCurrentTenantService Tenant(Guid id) => new(id);

  private static (ApplicationDbContext db, Guid tenantId) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    return (db, tenantId);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
