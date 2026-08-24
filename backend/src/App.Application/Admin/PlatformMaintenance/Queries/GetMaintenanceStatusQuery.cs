using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.PlatformMaintenance.Queries;

/// <summary>
/// Admin-only: bieżący stan globalnego trybu serwisowego platformy. Czyta bezpośrednio z bazy
/// (nie z cache), żeby panel admina zawsze pokazywał prawdę o przełączniku. NIE dziedziczy
/// z TenantHandler — GlobalSettings jest globalny. Authorization na controllerze (SystemAdminOnly).
/// </summary>
public record MaintenanceStatusDto(bool Enabled, string? Message, DateTime? StartedAtUtc);

public record GetMaintenanceStatusQuery : IRequest<MaintenanceStatusDto>;

public sealed class GetMaintenanceStatusQueryHandler
    : IRequestHandler<GetMaintenanceStatusQuery, MaintenanceStatusDto>
{
  private readonly IApplicationDbContext _context;

  public GetMaintenanceStatusQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<MaintenanceStatusDto> Handle(GetMaintenanceStatusQuery request, CancellationToken ct)
  {
    var settings = await _context.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(ct);
    return settings is null
        ? new MaintenanceStatusDto(false, null, null)
        : new MaintenanceStatusDto(settings.MaintenanceEnabled, settings.MaintenanceMessage, settings.MaintenanceStartedAtUtc);
  }
}
