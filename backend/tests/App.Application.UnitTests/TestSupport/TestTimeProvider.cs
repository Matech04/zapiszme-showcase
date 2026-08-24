namespace App.Application.UnitTests.TestSupport;

/// <summary>
/// Minimalny manualnie sterowany <see cref="TimeProvider"/> dla testów. Zaczyna w 2026-01-01 UTC,
/// <see cref="Advance"/> przesuwa czas o zadany interwał.
/// </summary>
internal sealed class TestTimeProvider : TimeProvider
{
  private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

  public override DateTimeOffset GetUtcNow() => _now;

  public void Advance(TimeSpan delta) => _now = _now.Add(delta);

  public void SetUtcNow(DateTimeOffset value) => _now = value;
}
