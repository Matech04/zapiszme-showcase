using App.Domain.Aggregates.TenantAggregate;

namespace App.Domain.UnitTests;

public class DepositSettingsTests
{
  [Fact]
  public void Disabled_ShouldBeDisabledWithZadatekDefault()
  {
    var settings = DepositSettings.Disabled();

    Assert.False(settings.Enabled);
    Assert.Equal(DepositInstrument.Zadatek, settings.Instrument);
    Assert.Equal(0, settings.Value);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(101)]
  public void Percentage_OutOfRange_ShouldThrow(decimal value)
  {
    Assert.Throws<ArgumentException>(() =>
      new DepositSettings(true, DepositMode.Percentage, value, DepositInstrument.Zadatek));
  }

  [Fact]
  public void Fixed_Negative_ShouldThrow()
  {
    Assert.Throws<ArgumentException>(() =>
      new DepositSettings(true, DepositMode.Fixed, -5m, DepositInstrument.Zadatek));
  }

  [Fact]
  public void ComputeDefault_Percentage_ShouldReturnRoundedShare()
  {
    var settings = new DepositSettings(true, DepositMode.Percentage, 30m, DepositInstrument.Zadatek);

    var result = settings.ComputeDefault(new Money(100m, "PLN"));

    Assert.Equal(30m, result.Amount);
    Assert.Equal("PLN", result.Currency);
  }

  [Fact]
  public void ComputeDefault_Percentage_ShouldRoundToGrosze()
  {
    var settings = new DepositSettings(true, DepositMode.Percentage, 33.333m, DepositInstrument.Zadatek);

    var result = settings.ComputeDefault(new Money(100m, "PLN"));

    Assert.Equal(33.33m, result.Amount);
  }

  [Fact]
  public void ComputeDefault_Fixed_ShouldReturnFixedAmount()
  {
    var settings = new DepositSettings(true, DepositMode.Fixed, 50m, DepositInstrument.Zadatek);

    var result = settings.ComputeDefault(new Money(120m, "PLN"));

    Assert.Equal(50m, result.Amount);
  }

  [Fact]
  public void ComputeDefault_Fixed_ShouldNotExceedAppointmentTotal()
  {
    var settings = new DepositSettings(true, DepositMode.Fixed, 200m, DepositInstrument.Zadatek);

    var result = settings.ComputeDefault(new Money(80m, "PLN"));

    Assert.Equal(80m, result.Amount);
  }
}
