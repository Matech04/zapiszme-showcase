using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Customers.Commands.SetCustomerWhitelist;

/// <summary>
/// Masowe ustawienie/zdjęcie flagi `IsWhitelisted` na liście klientów (np. zaznaczeni w CRM).
/// </summary>
public record BulkSetCustomerWhitelistCommand(IReadOnlyCollection<Guid> CustomerIds, bool IsWhitelisted)
    : IRequest<int>;

internal class BulkSetCustomerWhitelistCommandHandler
    : TenantHandler<BulkSetCustomerWhitelistCommand, int>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public BulkSetCustomerWhitelistCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _uow = uow;
  }

  public override async Task<int> Handle(BulkSetCustomerWhitelistCommand request, CancellationToken ct)
  {
    if (request.CustomerIds.Count == 0)
    {
      return 0;
    }

    var ids = request.CustomerIds.Distinct().ToList();
    var customers = await _context.Customers
      .Where(c => c.TenantId == TenantId && ids.Contains(c.Id))
      .ToListAsync(ct);

    foreach (var customer in customers)
    {
      if (request.IsWhitelisted)
      {
        customer.Whitelist();
      }
      else
      {
        customer.Unwhitelist();
      }
    }

    await _uow.SaveChangesAsync(ct);
    return customers.Count;
  }
}
