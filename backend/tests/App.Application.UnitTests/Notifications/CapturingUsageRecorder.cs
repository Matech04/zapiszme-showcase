using App.Application.Notifications;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.UnitTests.Notifications;

/// <summary>Test double dla <see cref="ISmsUsageGuard"/> — domyślnie przepuszcza (w limicie).</summary>
public sealed class FakeSmsUsageGuard : ISmsUsageGuard
{
  private readonly bool _withinCap;
  public FakeSmsUsageGuard(bool withinCap = true) => _withinCap = withinCap;
  public Task<bool> IsWithinMonthlyCapAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(_withinCap);
}

/// <summary>Test double dla <see cref="INotificationUsageRecorder"/> — zapamiętuje zarejestrowane wysyłki.</summary>
public sealed class CapturingUsageRecorder : INotificationUsageRecorder
{
  public record Entry(string Channel, NotificationType Type, bool Success, decimal Points);

  public List<Entry> Entries { get; } = new();

  public Task RecordSmsAsync(Guid tenantId, NotificationType type, bool success, decimal points, CancellationToken ct)
  {
    Entries.Add(new Entry("Sms", type, success, points));
    return Task.CompletedTask;
  }

  public Task RecordEmailAsync(Guid tenantId, NotificationType type, bool success, CancellationToken ct)
  {
    Entries.Add(new Entry("Email", type, success, 0m));
    return Task.CompletedTask;
  }
}
