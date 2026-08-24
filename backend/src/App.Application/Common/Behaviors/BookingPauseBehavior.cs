using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Common.Behaviors;

/// <summary>
/// Blokuje operacje zapisu wizyt (komendy oznaczone <see cref="IAppointmentWriteRequest"/>), gdy salon
/// wstrzymał rezerwacje (<c>Tenant.BookingPaused</c>). Personel „zarządza" wizytami przez panel — przy
/// wstrzymaniu kalendarz jest ukryty w UI, a ten behavior egzekwuje blokadę także po stronie serwera
/// (obejście UI bezpośrednim wywołaniem API → <see cref="BookingPausedException"/>). Globalny tryb
/// serwisowy platformy blokuje wszystkie zapisy osobno, w <c>PlatformMaintenanceMiddleware</c>.
/// </summary>
public sealed class BookingPauseBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
  private readonly ICurrentTenantService _currentTenant;
  private readonly IApplicationDbContext _context;

  public BookingPauseBehavior(ICurrentTenantService currentTenant, IApplicationDbContext context)
  {
    _currentTenant = currentTenant;
    _context = context;
  }

  public async Task<TResponse> Handle(
    TRequest request,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken cancellationToken)
  {
    if (request is IAppointmentWriteRequest && _currentTenant.TenantId is Guid tenantId)
    {
      var paused = await _context.Tenants
          .AsNoTracking()
          .Where(t => t.Id == tenantId)
          .Select(t => t.BookingPaused)
          .FirstOrDefaultAsync(cancellationToken);

      if (paused)
      {
        throw new BookingPausedException();
      }
    }

    return await next();
  }
}
