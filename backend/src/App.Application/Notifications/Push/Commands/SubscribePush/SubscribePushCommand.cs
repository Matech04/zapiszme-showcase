using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.NotificationAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Push.Commands.SubscribePush;

/// <summary>
/// Rejestruje (lub odświeża) subskrypcję Web Push urządzenia bieżącego użytkownika.
/// Endpoint jest globalnie unikalny — powtórna subskrypcja tego samego endpointu robi upsert.
/// </summary>
public record SubscribePushCommand(string Endpoint, string P256dh, string Auth) : IRequest;

internal class SubscribePushCommandHandler : TenantHandler<SubscribePushCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly ICurrentUserAccessor _currentUser;

  public SubscribePushCommandHandler(
    IApplicationDbContext context,
    ICurrentUserAccessor currentUser,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
    _currentUser = currentUser;
  }

  public override async Task Handle(SubscribePushCommand request, CancellationToken ct)
  {
    var userId = _currentUser.UserId
      ?? throw new InvalidOperationException("SubscribePush wymaga uwierzytelnionego użytkownika.");

    var nowUtc = DateTime.UtcNow;

    // Query filter scope'uje do tenanta; endpoint jest globalnie unikalny.
    var existing = await _context.PushSubscriptions
      .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);

    if (existing is null)
    {
      _context.PushSubscriptions.Add(PushSubscription.Create(
        TenantId, userId, request.Endpoint, request.P256dh, request.Auth, nowUtc));
    }
    else
    {
      // Ten sam endpoint mógł być wcześniej innego użytkownika na współdzielonym urządzeniu —
      // przejmujemy go dla bieżącego zalogowanego.
      existing.Refresh(userId, request.P256dh, request.Auth, nowUtc);
    }

    await _context.SaveChangesAsync(ct);
  }
}
