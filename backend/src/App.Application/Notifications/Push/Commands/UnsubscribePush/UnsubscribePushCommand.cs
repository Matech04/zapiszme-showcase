using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Push.Commands.UnsubscribePush;

/// <summary>Usuwa subskrypcję Web Push po endpoincie (użytkownik wyłączył powiadomienia na urządzeniu). No-op gdy brak.</summary>
public record UnsubscribePushCommand(string Endpoint) : IRequest;

internal class UnsubscribePushCommandHandler : TenantHandler<UnsubscribePushCommand>
{
  private readonly IApplicationDbContext _context;

  public UnsubscribePushCommandHandler(
    IApplicationDbContext context,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task Handle(UnsubscribePushCommand request, CancellationToken ct)
  {
    var subscription = await _context.PushSubscriptions
      .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);

    if (subscription is null)
    {
      return;
    }

    _context.PushSubscriptions.Remove(subscription);
    await _context.SaveChangesAsync(ct);
  }
}
