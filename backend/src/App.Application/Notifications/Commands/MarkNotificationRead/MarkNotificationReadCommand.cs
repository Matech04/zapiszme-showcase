using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Commands.MarkNotificationRead;

/// <summary>Oznacza pojedyncze powiadomienie bieżącego salonu jako przeczytane. No-op gdy nie znaleziono.</summary>
public record MarkNotificationReadCommand(Guid Id) : IRequest;

internal class MarkNotificationReadCommandHandler
    : TenantHandler<MarkNotificationReadCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly ICurrentUserAccessor _currentUser;

  public MarkNotificationReadCommandHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService,
      ICurrentUserAccessor currentUser)
      : base(currentTenantService)
  {
    _context = context;
    _currentUser = currentUser;
  }

  public override async Task Handle(MarkNotificationReadCommand request, CancellationToken ct)
  {
    // Query filter scope'uje do bieżącego tenanta — obce id po prostu nie zostanie znalezione.
    // Dodatkowo (poza Recepcją) tylko własne powiadomienie: cudze id = no-op, nie 403,
    // bo endpoint jest idempotentny i nie chcemy przez niego wyciekać istnienia rekordu.
    var query = _context.Notifications.Where(n => n.Id == request.Id);

    if (!_currentUser.IsDeskAccount)
    {
      var userId = _currentUser.UserId;
      query = query.Where(n => n.RecipientUserId != null && n.RecipientUserId == userId);
    }

    var notification = await query.FirstOrDefaultAsync(ct);

    if (notification is null)
    {
      return;
    }

    notification.MarkRead(DateTime.UtcNow);
    await _context.SaveChangesAsync(ct);
  }
}
