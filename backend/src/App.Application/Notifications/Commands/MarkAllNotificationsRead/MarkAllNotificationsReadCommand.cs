using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Commands.MarkAllNotificationsRead;

/// <summary>
/// Oznacza jako przeczytane nieprzeczytane powiadomienia ZALOGOWANEGO użytkownika. Recepcja
/// (Kiosk) czyści dzwonek całego salonu — pozostałe role wyłącznie własne, żeby jeden pracownik
/// nie gasił powiadomień właścicielce.
/// </summary>
public record MarkAllNotificationsReadCommand : IRequest;

internal class MarkAllNotificationsReadCommandHandler
    : TenantHandler<MarkAllNotificationsReadCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly ICurrentUserAccessor _currentUser;

  public MarkAllNotificationsReadCommandHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService,
      ICurrentUserAccessor currentUser)
      : base(currentTenantService)
  {
    _context = context;
    _currentUser = currentUser;
  }

  public override async Task Handle(MarkAllNotificationsReadCommand request, CancellationToken ct)
  {
    var query = _context.Notifications.Where(n => n.ReadAtUtc == null);

    if (!_currentUser.IsDeskAccount)
    {
      var userId = _currentUser.UserId;
      query = query.Where(n => n.RecipientUserId != null && n.RecipientUserId == userId);
    }

    var unread = await query.ToListAsync(ct);

    if (unread.Count == 0)
    {
      return;
    }

    var nowUtc = DateTime.UtcNow;
    foreach (var notification in unread)
    {
      notification.MarkRead(nowUtc);
    }

    await _context.SaveChangesAsync(ct);
  }
}
