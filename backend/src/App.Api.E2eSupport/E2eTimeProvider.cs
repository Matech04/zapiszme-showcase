namespace App.Api.E2eSupport;

/// <summary>
/// Sterowalny zegar dla E2E: zamrożony (nie płynie sam), ale ustawialny w OBIE strony.
/// BCL-owy <c>FakeTimeProvider</c> nie pozwala cofać czasu (<c>SetUtcNow</c> rzuca), więc
/// <c>/api/_e2e/time/reset</c> po wcześniejszym <c>advance</c> w innym specu zwracał 500 i
/// zostawiał zegar przesunięty — psując availability/holdy w kolejnych testach. Ten provider
/// pozwala <see cref="SetUtcNow"/> wrócić do dowolnego punktu, więc reset jest niezawodny.
/// </summary>
public sealed class E2eTimeProvider : TimeProvider
{
  private long _utcTicks;

  public E2eTimeProvider(DateTimeOffset start) => _utcTicks = start.UtcTicks;

  public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

  public void Advance(TimeSpan delta) => Interlocked.Add(ref _utcTicks, delta.Ticks);

  public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);
}
