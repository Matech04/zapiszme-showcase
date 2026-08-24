using App.Application.Common.Interfaces;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.PromoCodes.Commands.UpdatePromoCodeValidity;

public record UpdatePromoCodeValidityCommand(Guid PromoCodeId, DateTime? ValidUntil) : IRequest;

public class UpdatePromoCodeValidityCommandHandler : IRequestHandler<UpdatePromoCodeValidityCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public UpdatePromoCodeValidityCommandHandler(IApplicationDbContext context, IUnitOfWork uow)
  {
    _context = context;
    _uow = uow;
  }

  public async Task Handle(UpdatePromoCodeValidityCommand r, CancellationToken ct)
  {
    var code = await _context.PromoCodes.FirstOrDefaultAsync(p => p.Id == r.PromoCodeId, ct)
      ?? throw new NotFoundException(nameof(PromoCode), r.PromoCodeId);
    code.ExtendValidity(r.ValidUntil);
    await _uow.SaveChangesAsync(ct);
  }
}
