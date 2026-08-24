using App.Application.Notifications;

namespace App.Api.E2eSupport;

/// <summary>
/// Testowy <see cref="IRealtimeNotifier"/> — przechwytuje pushe zamiast wysyłać przez SignalR.
/// Pozwala asercję „kanał in-app zawołał realtime" bez podnoszenia socketu.
/// </summary>
public sealed class TestRealtimeNotifier : IRealtimeNotifier
{
  private readonly List<(Guid TenantId, Guid? RecipientUserId, RealtimeNotificationDto Dto)> _calls = new();
  private readonly object _gate = new();

  public IReadOnlyList<(Guid TenantId, Guid? RecipientUserId, RealtimeNotificationDto Dto)> Calls
  {
    get
    {
      lock (_gate) { return _calls.ToArray(); }
    }
  }

  public Task NotifyRecipientAsync(
    Guid tenantId,
    Guid? recipientUserId,
    RealtimeNotificationDto notification,
    CancellationToken ct)
  {
    lock (_gate) { _calls.Add((tenantId, recipientUserId, notification)); }
    return Task.CompletedTask;
  }
}
