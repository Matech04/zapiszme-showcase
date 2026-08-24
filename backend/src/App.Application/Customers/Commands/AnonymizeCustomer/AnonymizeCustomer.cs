using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;

namespace App.Application.Customers.Commands.AnonymizeCustomer;

/// <summary>
/// RODO art. 17 (prawo do usunięcia / bycia zapomnianym) — jawne żądanie klienta.
/// Semantycznie identyczna z <c>DeleteCustomerCommand</c> („Usuń klienta” w CRM); obie dzielą
/// rdzeń <see cref="CustomerErasure"/>. Osobna komenda zostaje, bo nazwa dokumentuje intencję
/// na poziomie API i działa też na klientach już zdeaktywowanych.
/// </summary>
public record AnonymizeCustomerCommand(Guid Id) : IRequest;

internal class AnonymizeCustomerHandler : TenantHandler<AnonymizeCustomerCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public AnonymizeCustomerHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _uow = uow;
  }

  public override async Task Handle(AnonymizeCustomerCommand request, CancellationToken ct)
  {
    await CustomerErasure.EraseAsync(_context, request.Id, TenantId, ct);
    await _uow.SaveChangesAsync(ct);
  }
}
