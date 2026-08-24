using App.Application.Admin.PromoCodes.Commands.CreatePromoCode;
using App.Application.Admin.PromoCodes.Commands.DeactivatePromoCode;
using App.Application.Admin.PromoCodes.Commands.UpdatePromoCodeValidity;
using App.Application.Admin.PromoCodes.Dtos;
using App.Application.Admin.PromoCodes.Queries.GetPromoCodeRedemptions;
using App.Application.Admin.PromoCodes.Queries.GetPromoCodes;
using App.Domain.Aggregates.PromoCodeAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[Authorize(Policy = "SystemAdminOnly")]
[Route("api/admin/promocodes")]
public class AdminPromoCodesController : ApiControllerBase
{
  [HttpPost]
  public async Task<ActionResult<Guid>> Create([FromBody] CreatePromoCodeBody body)
  {
    var id = await Mediator.Send(new CreatePromoCodeCommand(
      body.Code,
      body.Kind,
      body.DiscountType,
      body.DiscountValue,
      body.DurationMonths,
      body.MaxTotalUses,
      body.MaxUsesPerTenant ?? 1,
      body.ValidFrom,
      body.ValidUntil,
      body.AppliesTo ?? PromoCodeAppliesTo.NewTenantsOnly,
      body.Metadata));
    return Ok(id);
  }

  [HttpGet]
  public async Task<ActionResult<List<PromoCodeDto>>> List([FromQuery] bool? isActive)
  {
    var result = await Mediator.Send(new GetPromoCodesQuery(isActive));
    return Ok(result);
  }

  [HttpGet("{id}/redemptions")]
  public async Task<ActionResult<List<PromoCodeRedemptionDto>>> Redemptions(Guid id)
  {
    var result = await Mediator.Send(new GetPromoCodeRedemptionsQuery(id));
    return Ok(result);
  }

  [HttpPost("{id}/deactivate")]
  public async Task<ActionResult> Deactivate(Guid id)
  {
    await Mediator.Send(new DeactivatePromoCodeCommand(id));
    return NoContent();
  }

  [HttpPatch("{id}/validity")]
  public async Task<ActionResult> UpdateValidity(Guid id, [FromBody] UpdateValidityBody body)
  {
    await Mediator.Send(new UpdatePromoCodeValidityCommand(id, body.ValidUntil));
    return NoContent();
  }
}

public sealed record CreatePromoCodeBody(
    string Code,
    PromoCodeKind Kind,
    PromoDiscountType DiscountType,
    decimal DiscountValue,
    int? DurationMonths,
    int? MaxTotalUses,
    int? MaxUsesPerTenant,
    DateTime? ValidFrom,
    DateTime? ValidUntil,
    PromoCodeAppliesTo? AppliesTo,
    string? Metadata);

public sealed record UpdateValidityBody(DateTime? ValidUntil);
