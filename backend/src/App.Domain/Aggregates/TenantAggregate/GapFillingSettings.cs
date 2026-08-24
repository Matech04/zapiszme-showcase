namespace App.Domain.Aggregates.TenantAggregate;

public class GapFillingSettings
{
  public GapFillingMode Mode { get; private set; } = GapFillingMode.Disabled;

  /// <summary>Minutes of buffer after an appointment ends / before it starts when calculating adjacency.</summary>
  public int BufferMinutes { get; private set; } = 0;

  /// <summary>How many consecutive slots beyond the immediately adjacent one are also preferred.</summary>
  public int LookaheadSlots { get; private set; } = 1;

  private GapFillingSettings() { }

  public GapFillingSettings(GapFillingMode mode, int bufferMinutes, int lookaheadSlots)
  {
    Mode = mode;
    BufferMinutes = Math.Max(0, bufferMinutes);
    LookaheadSlots = Math.Max(1, lookaheadSlots);
  }
}
