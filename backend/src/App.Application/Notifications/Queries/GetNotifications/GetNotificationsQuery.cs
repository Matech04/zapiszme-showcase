using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Queries.GetNotifications;

/// <summary>
/// Ostatnie trwałe powiadomienia in-app dla ZALOGOWANEGO użytkownika (zasila dzwonek w panelu).
/// Dzwonek jest osobisty: Owner/Manager/Employee widzą wyłącznie powiadomienia o wizytach, które
/// ich dotyczą (`RecipientUserId`). Wyjątkiem jest konto „Recepcja" (Kiosk) — wspólna konsola
/// salonu bez własnego kalendarza — które widzi powiadomienia całego zespołu.
/// </summary>
public record GetNotificationsQuery : IRequest<List<NotificationDto>>;

public record NotificationDto(
    Guid Id,
    int Type,
    string Subject,
    string ShortText,
    Guid? ReferenceId,
    DateTime CreatedAtUtc,
    bool Read);

internal class GetNotificationsQueryHandler
    : TenantHandler<GetNotificationsQuery, List<NotificationDto>>
{
  private const int MaxItems = 50;
  private readonly IApplicationDbContext _context;
  private readonly ICurrentUserAccessor _currentUser;

  public GetNotificationsQueryHandler(
      IApplicationDbContext context,
      ICurrentTenantService currentTenantService,
      ICurrentUserAccessor currentUser)
      : base(currentTenantService)
  {
    _context = context;
    _currentUser = currentUser;
  }

  public override async Task<List<NotificationDto>> Handle(
      GetNotificationsQuery request,
      CancellationToken ct)
  {
    // Query filter na Notifications automatycznie scope'uje do bieżącego tenanta.
    var query = _context.Notifications.AsNoTracking();

    // Dzwonek osobisty dla wszystkich poza Recepcją i trybem wsparcia (Admin w impersonacji —
    // widzi cały salon do diagnostyki; zakres i tak ograniczony przez query filter do tenanta).
    // Brak UserId (nie powinno się zdarzyć na uwierzytelnionym żądaniu) = brak powiadomień,
    // zamiast pokazania cudzych.
    if (!_currentUser.IsDeskAccount && !_currentUser.IsSupportSession)
    {
      var userId = _currentUser.UserId;
      query = query.Where(n => n.RecipientUserId != null && n.RecipientUserId == userId);
    }

    return await query
      .OrderByDescending(n => n.CreatedAtUtc)
      .Take(MaxItems)
      .Select(n => new NotificationDto(
        n.Id,
        (int)n.Type,
        n.Subject,
        n.ShortText,
        n.ReferenceId,
        n.CreatedAtUtc,
        n.ReadAtUtc != null))
      .ToListAsync(ct);
  }
}
