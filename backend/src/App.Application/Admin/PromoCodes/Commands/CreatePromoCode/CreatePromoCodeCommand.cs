using App.Application.Common.Interfaces;
using App.Domain.Aggregates.PromoCodeAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.PromoCodes.Commands.CreatePromoCode;

/// <summary>
/// Admin-only: tworzenie nowego kodu promocyjnego. NIE dziedziczy z TenantHandler — PromoCode jest
/// global, brak tenant context. Authorization na controllerze (<c>Policy="SystemAdminOnly"</c>).
/// </summary>
public record CreatePromoCodeCommand(
    string Code,
    PromoCodeKind Kind,
    PromoDiscountType DiscountType,
    decimal DiscountValue,
    int? DurationMonths,
    int? MaxTotalUses,
    int MaxUsesPerTenant,
    DateTime? ValidFrom,
    DateTime? ValidUntil,
    PromoCodeAppliesTo AppliesTo,
    string? Metadata) : IRequest<Guid>;

public class CreatePromoCodeCommandHandler : IRequestHandler<CreatePromoCodeCommand, Guid>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public CreatePromoCodeCommandHandler(IApplicationDbContext context, IUnitOfWork uow)
  {
    _context = context;
    _uow = uow;
  }

  public async Task<Guid> Handle(CreatePromoCodeCommand r, CancellationToken ct)
  {
    var normalized = PromoCode.NormalizeCode(r.Code);
    var exists = await _context.PromoCodes.AnyAsync(p => p.Code == normalized, ct);
    if (exists)
    {
      throw new InvalidOperationException($"PromoCode '{normalized}' już istnieje.");
    }

    var code = r.DiscountType switch
    {
      PromoDiscountType.PriceOverride => PromoCode.CreatePriceOverride(
        r.Code, r.DiscountValue, r.Kind, r.MaxTotalUses, r.MaxUsesPerTenant,
        r.ValidFrom, r.ValidUntil, r.AppliesTo, r.Metadata),
      PromoDiscountType.PercentOff => PromoCode.CreatePercentOff(
        r.Code, r.DiscountValue, r.DurationMonths ?? 1, r.Kind, r.MaxTotalUses, r.MaxUsesPerTenant,
        r.ValidFrom, r.ValidUntil, r.AppliesTo, r.Metadata),
      PromoDiscountType.FreeMonths => PromoCode.CreateFreeMonths(
        r.Code, (int)r.DiscountValue, r.Kind, r.MaxTotalUses, r.MaxUsesPerTenant,
        r.ValidFrom, r.ValidUntil, r.AppliesTo, r.Metadata),
      PromoDiscountType.TrialExtension => PromoCode.CreateTrialExtension(
        r.Code, (int)r.DiscountValue, r.Kind, r.MaxTotalUses, r.MaxUsesPerTenant,
        r.ValidFrom, r.ValidUntil, r.Metadata),
      _ => throw new ArgumentOutOfRangeException(nameof(r.DiscountType)),
    };

    _context.PromoCodes.Add(code);
    await _uow.SaveChangesAsync(ct);
    return code.Id;
  }
}
