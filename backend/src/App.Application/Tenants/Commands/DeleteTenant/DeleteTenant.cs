using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Commands.DeleteTenant;

/// <summary>
/// Twardo i trwale usuwa salon wraz z całym jego grafem danych (wizyty, klientki, pracownicy,
/// usługi, powiadomienia, zużycie, sesje wsparcia) oraz powiązane konta Identity.
/// Operacja dostępna wyłącznie dla administratora platformy (<c>SystemAdminOnly</c>) i jest
/// nieodwracalna. Cała praca dzieje się w <see cref="ITenantPurgeService"/> (jedno źródło prawdy
/// współdzielone z cleanup-em demo).
/// </summary>
public record DeleteTenantCommand(Guid Id) : IRequest;

public class DeleteTenantCommandHandler : IRequestHandler<DeleteTenantCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly ITenantPurgeService _purge;

  public DeleteTenantCommandHandler(IApplicationDbContext context, ITenantPurgeService purge)
  {
    _context = context;
    _purge = purge;
  }

  public async Task Handle(DeleteTenantCommand request, CancellationToken ct)
  {
    // Tenant nie ma query filtra, ale admin systemowy działa bez kontekstu tenanta —
    // IgnoreQueryFilters() jest tu obroną przed przyszłą zmianą i jest nieszkodliwe.
    var exists = await _context.Tenants.IgnoreQueryFilters()
      .AnyAsync(t => t.Id == request.Id, ct);

    if (!exists)
    {
      throw new NotFoundException(nameof(Tenant), request.Id);
    }

    await _purge.PurgeAsync(request.Id, ct);
  }
}
