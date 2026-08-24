using App.Application.Common.Interfaces;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.PromoCodes.Commands.DeactivatePromoCode;

public record DeactivatePromoCodeCommand(Guid PromoCodeId) : IRequest;

public class DeactivatePromoCodeCommandHandler : IRequestHandler<DeactivatePromoCodeCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public DeactivatePromoCodeCommandHandler(IApplicationDbContext context, IUnitOfWork uow)
  {
    _context = context;
    _uow = uow;
  }

  public async Task Handle(DeactivatePromoCodeCommand r, CancellationToken ct)
  {
    var code = await _context.PromoCodes.FirstOrDefaultAsync(p => p.Id == r.PromoCodeId, ct)
      ?? throw new NotFoundException(nameof(PromoCode), r.PromoCodeId);
    code.Deactivate();
    await _uow.SaveChangesAsync(ct);
  }
}
