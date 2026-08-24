using MediatR;

namespace App.Application.UnitTests.Booking;

/// <summary>Przechwytuje opublikowane zdarzenia MediatR bez ich obsługi — do asercji w testach.</summary>
internal sealed class CapturingPublisher : IPublisher
{
  public List<INotification> Published { get; } = new();

  public Task Publish(object notification, CancellationToken cancellationToken = default)
  {
    if (notification is INotification n)
    {
      Published.Add(n);
    }
    return Task.CompletedTask;
  }

  public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
    where TNotification : INotification
  {
    Published.Add(notification);
    return Task.CompletedTask;
  }
}

/// <summary>Rzuca przy każdej publikacji — weryfikuje best-effort handlerów komend.</summary>
internal sealed class ThrowingPublisher : IPublisher
{
  public Task Publish(object notification, CancellationToken cancellationToken = default)
    => throw new InvalidOperationException("boom");

  public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
    where TNotification : INotification
    => throw new InvalidOperationException("boom");
}
