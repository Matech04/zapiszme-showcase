using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

/// <summary>
/// GUARD-001..009 — testy bezpośrednie statycznych metod <see cref="Guard"/>.
/// </summary>
public sealed class GuardTests
{
  // GUARD-001: AgainstEmptyString throws dla null/empty/whitespace
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("\t\n")]
  public void AgainstEmptyString_throws_for_invalid_input(string? value)
  {
    Assert.Throws<ArgumentException>(() => Guard.AgainstEmptyString(value!, "field"));
  }

  // GUARD-002: NormalizeRequiredText trims
  [Theory]
  [InlineData("  hello  ", "hello")]
  [InlineData("\tworld\n", "world")]
  [InlineData("no-trim", "no-trim")]
  public void NormalizeRequiredText_trims(string input, string expected)
  {
    Assert.Equal(expected, Guard.NormalizeRequiredText(input, "field"));
  }

  // GUARD-003: NormalizeOptionalText returns empty for null, trims non-null
  [Fact]
  public void NormalizeOptionalText_returns_empty_for_null()
  {
    Assert.Equal(string.Empty, Guard.NormalizeOptionalText(null));
  }

  [Theory]
  [InlineData("  text  ", "text")]
  [InlineData("", "")]
  [InlineData("plain", "plain")]
  public void NormalizeOptionalText_trims_non_null(string input, string expected)
  {
    Assert.Equal(expected, Guard.NormalizeOptionalText(input));
  }

  // GUARD-004: AgainstNegative throws dla wartości ujemnych
  [Theory]
  [InlineData(-0.01)]
  [InlineData(-1)]
  [InlineData(-100.5)]
  public void AgainstNegative_throws_for_negative(decimal value)
  {
    Assert.Throws<ArgumentException>(() => Guard.AgainstNegative(value, "field"));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(0.01)]
  [InlineData(100)]
  public void AgainstNegative_passes_for_zero_or_positive(decimal value)
  {
    Guard.AgainstNegative(value, "field"); // does not throw
  }

  // GUARD-005: AgainstInvalidRange throws poza [min, max]; przechodzi na granicach
  [Theory]
  [InlineData(0.0)]
  [InlineData(0.5)]
  [InlineData(1.0)]
  public void AgainstInvalidRange_passes_at_boundary_and_inside(decimal value)
  {
    Guard.AgainstInvalidRange(value, 0m, 1m, "value");
  }

  [Theory]
  [InlineData(-0.01)]
  [InlineData(1.01)]
  [InlineData(-100)]
  [InlineData(100)]
  public void AgainstInvalidRange_throws_outside(decimal value)
  {
    Assert.Throws<ArgumentException>(() => Guard.AgainstInvalidRange(value, 0m, 1m, "value"));
  }

  // GUARD-006: NormalizeEmail walidacja i lowercase
  [Theory]
  [InlineData("USER@Example.COM", "user@example.com")]
  [InlineData("  john.doe@example.com  ", "john.doe@example.com")]
  public void NormalizeEmail_lowercases_and_trims(string input, string expected)
  {
    Assert.Equal(expected, Guard.NormalizeEmail(input));
  }

  // MailAddress jest dość liberalny — testujemy tylko jasno błędne formaty, na których konstruktor faktycznie rzuca.
  [Theory]
  [InlineData("not-an-email")]
  [InlineData("@missing.local")]
  public void NormalizeEmail_throws_for_invalid_format(string input)
  {
    Assert.ThrowsAny<Exception>(() => Guard.NormalizeEmail(input));
  }

  // GUARD-007: AgainstInvalidTimeRange throws gdy start >= end
  [Theory]
  [InlineData(10, 0, 10, 0)]
  [InlineData(10, 0, 9, 0)]
  [InlineData(23, 59, 0, 0)]
  public void AgainstInvalidTimeRange_throws_when_start_ge_end(int sH, int sM, int eH, int eM)
  {
    var start = new TimeOnly(sH, sM);
    var end = new TimeOnly(eH, eM);

    Assert.Throws<InvalidTimeRangeException>(() => Guard.AgainstInvalidTimeRange(start, end));
  }

  [Fact]
  public void AgainstInvalidTimeRange_passes_when_start_before_end()
  {
    Guard.AgainstInvalidTimeRange(new TimeOnly(9, 0), new TimeOnly(17, 0));
  }

  // GUARD-008: AgainstInvalidAppointmentSlotStepMinutes — dozwolone {5, 10, 15, 30}
  [Theory]
  [InlineData(1)]
  [InlineData(5)]
  [InlineData(7)]
  [InlineData(10)]
  [InlineData(15)]
  [InlineData(20)]
  [InlineData(30)]
  [InlineData(60)]
  [InlineData(240)]
  public void AgainstInvalidAppointmentSlotStepMinutes_passes_for_allowed_values(int value)
  {
    Guard.AgainstInvalidAppointmentSlotStepMinutes(value);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(-15)]
  [InlineData(241)]
  public void AgainstInvalidAppointmentSlotStepMinutes_throws_for_disallowed(int value)
  {
    Assert.Throws<ArgumentException>(() => Guard.AgainstInvalidAppointmentSlotStepMinutes(value));
  }

  // GUARD-009: ReplaceSpaces lowercases + zamienia spacje na myślniki
  [Theory]
  [InlineData("My Salon", "my-salon")]
  [InlineData("  My  SALON  ", "my--salon")]
  [InlineData("salon", "salon")]
  public void ReplaceSpaces_lowercases_and_replaces_spaces(string input, string expected)
  {
    Assert.Equal(expected, Guard.ReplaceSpaces(input));
  }
}
