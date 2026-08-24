using App.Application.Appointments.Queries.GetAppointmentById;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Payments.Commands.GenerateDepositLink;
using App.Application.Payments.Commands.SendDepositLink;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

public record GenerateDepositLinkRequest(decimal? AmountOverride = null);

/// <summary>Opcjonalny kanał wysyłki; null = domyślny kanał salonu (CustomerVerificationChannel).</summary>
public record SendDepositLinkRequest(CustomerVerificationChannel? Channel = null);

/// <summary>
/// Zadatki inicjowane z panelu (model ręczny): personel generuje link płatności dla istniejącej
/// wizyty i wysyła go klientce (kopiuj / SMS). <c>GeneralAccess</c> wpuszcza każdego pracownika, ale
/// akcje na konkretnej wizycie podlegają tej samej kontroli własności co reszta mutacji wizyt:
/// Owner/Manager/Admin/Kiosk — dowolna wizyta; Employee — tylko własna (chyba że polityka salonu = TeamFull).
/// </summary>
[Authorize(Policy = "GeneralAccess")]
public class DepositsController : ApiControllerBase
{
  /// <summary>Generuje (lub regeneruje) link zadatku dla wizyty. Kwota prefill z ustawień, opcjonalnie nadpisana.</summary>
  [HttpPost("{appointmentId:guid}/link")]
  public async Task<ActionResult<GenerateDepositLinkResult>> GenerateLink(
    [FromRoute] Guid appointmentId,
    [FromBody] GenerateDepositLinkRequest request,
    CancellationToken ct)
  {
    var result = await Mediator.Send(
      new GenerateDepositLinkCommand(appointmentId, request.AmountOverride), ct);
    return Ok(result);
  }

  /// <summary>Wysyła istniejący link zadatku do klienta wybranym kanałem (SMS/e-mail).</summary>
  [HttpPost("{appointmentId:guid}/send")]
  public async Task<ActionResult<SendDepositLinkResult>> Send(
    [FromRoute] Guid appointmentId,
    [FromBody] SendDepositLinkRequest request,
    CancellationToken ct)
  {
    var result = await Mediator.Send(
      new SendDepositLinkCommand(appointmentId, request.Channel), ct);
    return Ok(result);
  }

}
