using App.Domain.Aggregates.GlobalSettingsAggregate;

namespace App.Domain.UnitTests;

public class GlobalSettingsTests
{
  private static readonly DateTime Now = new(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void CreateDefault_ShouldBeDisabled_WithSingletonId()
  {
    var settings = GlobalSettings.CreateDefault();

    Assert.False(settings.MaintenanceEnabled);
    Assert.Null(settings.MaintenanceMessage);
    Assert.Null(settings.MaintenanceStartedAtUtc);
    Assert.Equal(GlobalSettings.SingletonId, settings.Id);
  }

  [Fact]
  public void SetMaintenance_Enable_WithMessage_ShouldTrimStoreAndStampTime()
  {
    var settings = GlobalSettings.CreateDefault();

    settings.SetMaintenance(true, "  Trwa aktualizacja  ", Now);

    Assert.True(settings.MaintenanceEnabled);
    Assert.Equal("Trwa aktualizacja", settings.MaintenanceMessage);
    Assert.Equal(Now, settings.MaintenanceStartedAtUtc);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void SetMaintenance_Enable_WithBlankMessage_ShouldStoreNull(string? message)
  {
    var settings = GlobalSettings.CreateDefault();

    settings.SetMaintenance(true, message, Now);

    Assert.True(settings.MaintenanceEnabled);
    Assert.Null(settings.MaintenanceMessage);
  }

  [Fact]
  public void SetMaintenance_ReEnable_ShouldKeepFirstStartedAt()
  {
    var settings = GlobalSettings.CreateDefault();
    settings.SetMaintenance(true, "pierwszy", Now);

    var later = Now.AddMinutes(30);
    settings.SetMaintenance(true, "edytowany komunikat", later);

    Assert.Equal(Now, settings.MaintenanceStartedAtUtc);
    Assert.Equal("edytowany komunikat", settings.MaintenanceMessage);
  }

  [Fact]
  public void SetMaintenance_Disable_ShouldClearMessageAndTime()
  {
    var settings = GlobalSettings.CreateDefault();
    settings.SetMaintenance(true, "trwają prace", Now);

    settings.SetMaintenance(false, null, Now.AddHours(1));

    Assert.False(settings.MaintenanceEnabled);
    Assert.Null(settings.MaintenanceMessage);
    Assert.Null(settings.MaintenanceStartedAtUtc);
  }

  [Fact]
  public void SetMaintenance_Enable_WithOverlongMessage_ShouldClampToMaxLength()
  {
    var settings = GlobalSettings.CreateDefault();
    var longMessage = new string('x', GlobalSettings.MaintenanceMessageMaxLength + 50);

    settings.SetMaintenance(true, longMessage, Now);

    Assert.Equal(GlobalSettings.MaintenanceMessageMaxLength, settings.MaintenanceMessage!.Length);
  }
}
