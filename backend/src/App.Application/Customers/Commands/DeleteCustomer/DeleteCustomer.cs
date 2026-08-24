using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;

namespace App.Application.Customers.Commands.DeleteCustomer;

/// <summary>
/// „Usuń klienta” w CRM. Trwale usuwa dane osobowe (RODO art. 17) i deaktywuje profil —
/// nie jest to zwykły soft-delete. Powód: deaktywowany klient był już dziś niewidoczny,
/// nieprzywracalny i nigdy więcej niedopasowywany przy rezerwacji, więc pozostawione PII
/// było czystą odpowiedzialnością prawną bez konsumenta. Rdzeń w <see cref="CustomerErasure"/>,
/// wspólny z <c>AnonymizeCustomerCommand</c>.
/// </summary>
public record DeleteCustomerCommand(Guid Id) : IRequest;

internal class DeleteCustomerHandler : TenantHandler<DeleteCustomerCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public DeleteCustomerHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _uow = uow;
  }

  public override async Task Handle(DeleteCustomerCommand request, CancellationToken ct)
  {
    await CustomerErasure.EraseAsync(_context, request.Id, TenantId, ct);
    await _uow.SaveChangesAsync(ct);
  }
}
